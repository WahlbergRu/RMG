// Assets\VoronoiMapGen\Systems\Rendering\VoronoiMeshCreateSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using TerrainData = VoronoiMapGen.Components.TerrainData;

namespace VoronoiMapGen.Systems.Rendering
{
    /// <summary>
    /// Первый проход рендера: создаёт по entity уникальный Mesh через MeshData,
    /// вешает RenderMeshArray+MaterialMeshInfo и индивидуальный цвет URP.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct VoronoiMeshCreateSystem : ISystem
    {
        private static Material s_CellMaterial;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            s_CellMaterial ??= EnsureDefaultCellMaterial();
        }

        //[BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Проверяем, завершена ли генерация геометрии и не выполнена ли ещё отрисовка
            if (!SystemAPI.HasSingleton<GeometryBuiltTag>()) return;
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;
            if (SystemAPI.HasSingleton<VoronoiMeshGeneratedTag>()) return;
            
            //Debug.Log("i'm here");

            EntityQuery query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            // siteIndex -> pos
            EntityQuery siteQ = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
            using NativeArray<VoronoiSite> sites = siteQ.ToComponentDataArray<VoronoiSite>(Allocator.Temp);
            NativeParallelHashMap<int, float2> sitePos = new NativeParallelHashMap<int, float2>(sites.Length, Allocator.Temp);
            foreach (VoronoiSite s in sites) sitePos[s.Index] = s.Position;

            // пакетно готовим MeshData
            Mesh.MeshDataArray mda = Mesh.AllocateWritableMeshData(entities.Length);
            Mesh[] meshes = new Mesh[entities.Length];

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                DynamicBuffer<CellPolygonVertex> verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                DynamicBuffer<CellTriIndex> triPairs = state.EntityManager.GetBuffer<CellTriIndex>(e);
                VoronoiCell cell = state.EntityManager.GetComponentData<VoronoiCell>(e);

                Mesh.MeshData md = mda[i];
                md.SetVertexBufferParams(verts.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                md.SetIndexBufferParams(triPairs.Length, IndexFormat.UInt32);

                NativeArray<Vector3> vb = md.GetVertexData<Vector3>();
                for (int v = 0; v < verts.Length; v++)
                {
                    float3 vertex = verts[v].Value;
                    float y = 0f;
                    
                    // Применяем высоту из TerrainData, если он есть
                    if (state.EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(e))
                    {
                        TerrainData terrain = state.EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(e);
                        y = terrain.Elevation * 100.0f;
                    }
                    
                    vb[v] = new Vector3(vertex.x - cell.Centroid.x, y, vertex.z - cell.Centroid.y);
                }

                NativeArray<int> ib = md.GetIndexData<int>();
                for (int t = 0; t < triPairs.Length; t++)
                {
                    ib[t] = triPairs[t].Value;
                }

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, triPairs.Length) { topology = MeshTopology.Triangles },
                              MeshUpdateFlags.DontRecalculateBounds);

                meshes[i] = new Mesh { indexFormat = IndexFormat.UInt32 };
            }

            Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.Default);

            // общий RenderMeshArray: 1 материал, N мешей (индивидуальность — по MeshArrayIndex)
            RenderMeshArray rma = new RenderMeshArray(new[] { s_CellMaterial }, meshes);
            RenderMeshDescription desc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                VoronoiCell cell = state.EntityManager.GetComponentData<VoronoiCell>(e);

                // позиция: local override либо позиция сайта
                float3 pos;
                if (state.EntityManager.HasComponent<VoronoiCell>(e))
                    pos = new float3(cell.Centroid.x, 0f, cell.Centroid.y);
                else
                {
                    int siteIndex = cell.SiteIndex;
                    sitePos.TryGetValue(siteIndex, out float2 sp);
                    pos = new float3(sp.x, 0f, sp.y);
                    
                    // Применяем высоту из TerrainData
                    if (state.EntityManager.HasComponent<TerrainData>(e))
                    {
                        TerrainData terrain = state.EntityManager.GetComponentData<TerrainData>(e);
                        pos.y = terrain.Elevation * 100.0f;
                    }
                }

                if (!state.EntityManager.HasComponent<LocalTransform>(e))
                    state.EntityManager.AddComponentData(e, new LocalTransform
                    {
                        Position = pos,
                        Rotation = quaternion.identity,
                        Scale = 1f
                    });

                // индивидуальный цвет (URP property)
                float4 color = new float4(0.3f, 0.7f, 0.3f, 1.0f); // Стандартный цвет для ячеек
                
                // Только если есть компонент CellBiome, используем его цвет
                if (state.EntityManager.HasComponent<CellBiome>(e))
                {
                    CellBiome biome = state.EntityManager.GetComponentData<CellBiome>(e);
                    color = GetBiomeColor(biome.Type);
                }

                //var meshEntity = MeshUtils.CreateMeshEntity(state.EntityManager, meshes[i], s_CellMaterial, pos, color);
                
                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);
                
                // привязка меша i в RenderMeshArray
                MaterialMeshInfo mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, i);
                RenderMeshUtility.AddComponents(e, state.EntityManager, in desc, rma, mmi);
                

            }

            // помечаем, что всё создано
            state.EntityManager.CreateSingleton<VoronoiMeshGeneratedTag>();

            sitePos.Dispose();
        }

        private static Material EnsureDefaultCellMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = new Material(shader)
            {
                name = "CellMaterial",
                enableInstancing = true
            };
            material.color = new Color(0.5f, 0.7f, 0.5f, 1.0f);
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Smoothness", 0.5f);
            return material;
        }
        
        private static float4 GetBiomeColor(BiomeType biomeType)
        {
            return biomeType switch
            {
                BiomeType.Ocean => new float4(0.1f, 0.2f, 0.6f, 1.0f),
                BiomeType.Coast => new float4(0.8f, 0.8f, 0.4f, 1.0f),
                BiomeType.Desert => new float4(0.9f, 0.8f, 0.5f, 1.0f),
                BiomeType.Grassland => new float4(0.3f, 0.7f, 0.3f, 1.0f),
                BiomeType.Forest => new float4(0.1f, 0.5f, 0.1f, 1.0f),
                BiomeType.Mountain => new float4(0.6f, 0.6f, 0.6f, 1.0f),
                BiomeType.Snow => new float4(0.9f, 0.9f, 1.0f, 1.0f),
                _ => new float4(0.5f, 0.5f, 0.5f, 1.0f)
            };
        }
    }
}