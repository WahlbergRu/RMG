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

namespace VoronoiMapGen.Systems.Rendering
{
    /// <summary>
    /// Первый проход рендера: создаёт по entity уникальный Mesh через MeshData,
    /// вешает RenderMeshArray+MaterialMeshInfo и индивидуальный цвет URP.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiGeometryBuildSystem))]
    public partial struct VoronoiMeshCreateSystem : ISystem
    {
        private static Material s_CellMaterial;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            s_CellMaterial ??= EnsureDefaultCellMaterial();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Проверяем, завершена ли генерация геометрии и не выполнена ли ещё отрисовка
            if (!SystemAPI.HasSingleton<GeometryBuiltTag>()) return;
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;
            if (SystemAPI.HasSingleton<RenderingBuiltTag>()) return;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            // siteIndex -> pos
            var siteQ = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
            using var sites = siteQ.ToComponentDataArray<VoronoiSite>(Allocator.Temp);
            var sitePos = new NativeParallelHashMap<int, float2>(sites.Length, Allocator.Temp);
            foreach (var s in sites) sitePos[s.Index] = s.Position;

            // пакетно готовим MeshData
            var mda = Mesh.AllocateWritableMeshData(entities.Length);
            var meshes = new Mesh[entities.Length];

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                var triPairs = state.EntityManager.GetBuffer<CellTriIndex>(e);
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(e);

                var md = mda[i];
                md.SetVertexBufferParams(verts.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                md.SetIndexBufferParams(triPairs.Length, IndexFormat.UInt32);

                var vb = md.GetVertexData<Vector3>();
                for (int v = 0; v < verts.Length; v++)
                {
                    var vertex = verts[v].Value;
                    float y = 0f;
                    
                    // Применяем высоту из TerrainData, если он есть
                    if (state.EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(e))
                    {
                        var terrain = state.EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(e);
                        y = terrain.Elevation * 100.0f;
                    }
                    
                    vb[v] = new Vector3(vertex.x - cell.Centroid.x, y, vertex.z - cell.Centroid.y);
                }

                var ib = md.GetIndexData<int>();
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
            var rma = new RenderMeshArray(new[] { s_CellMaterial }, meshes);
            var desc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(e);

                // позиция: local override либо позиция сайта
                float3 pos;
                if (state.EntityManager.HasComponent<VoronoiCell>(e))
                    pos = new float3(cell.Centroid.x, 0f, cell.Centroid.y);
                else
                {
                    var siteIndex = cell.SiteIndex;
                    sitePos.TryGetValue(siteIndex, out var sp);
                    pos = new float3(sp.x, 0f, sp.y);
                    
                    // Применяем высоту из TerrainData
                    if (state.EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(e))
                    {
                        var terrain = state.EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(e);
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
                    var biome = state.EntityManager.GetComponentData<CellBiome>(e);
                    color = GetBiomeColor(biome.Type);
                }

                

                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                // привязка меша i в RenderMeshArray
                var mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, i);
                RenderMeshUtility.AddComponents(e, state.EntityManager, in desc, rma, mmi);
                
                var meshEntity = MeshUtils.CreateMeshEntity(state.EntityManager, meshes[i], s_CellMaterial, pos, color);
                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                // Добавляем WorldRenderBounds для корректного рендеринга
                // if (!state.EntityManager.HasComponent<WorldRenderBounds>(e))
                // // {
                //     var mesh = meshes[i];
                //     var bounds = mesh.bounds;
                //     var extents = new float3(
                //         math.max(1f, bounds.extents.x), // Минимум 1 для безопасности
                //         math.max(1f, bounds.extents.y),
                //         math.max(1f, bounds.extents.z)
                //     );
                //     // var bounds = new AABB {
                //     //     // Центр учитывает позицию объекта и центр меша
                //     //     Center = new float3(pos.x, pos.y + (mesh.bounds.max.y + mesh.bounds.min.y) * 0.5f, pos.z),
                //     //     // Extents соответствуют реальным размерам меша (но не меньше 1 для безопасности)
                //     //     Extents = new float3(
                //     //         math.max(1f, mesh.bounds.extents.x * 1.2f), 
                //     //         math.max(1f, mesh.bounds.extents.y * 1.2f), 
                //     //         math.max(1f, mesh.bounds.extents.z * 1.2f)
                //     //     )
                //     // };
                //     state.EntityManager.AddComponentData(e, new WorldRenderBounds {
                //         Value = new AABB {
                //             Center = pos,
                //             Extents = extents
                //         }
                //         
                //     });
                // }
            }

            // помечаем, что всё создано
            state.EntityManager.CreateSingleton<RenderingBuiltTag>();

            sitePos.Dispose();
        }

        private static Material EnsureDefaultCellMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader)
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

    // Необходимые вспомогательные структуры
    public struct RenderingBuiltTag : IComponentData {}
    public struct VoronoiCellMeshTag : IComponentData {}
}