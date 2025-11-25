using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct VoronoiMeshCreateSystem : ISystem
    {
        private static Material s_DefaultMaterial;
        private const int BATCH_SIZE = 2000;

        public void OnCreate(ref SystemState state)
        {
            if (s_DefaultMaterial == null) s_DefaultMaterial = EnsureDefaultMaterial();
            state.RequireForUpdate<MapGeneratedTag>();
            state.RequireForUpdate<MapSettings>(); // Нужно, чтобы узнать кол-во уровней
        }

        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            if (query.IsEmpty)
            {
                if (!SystemAPI.HasSingleton<VoronoiMeshGeneratedTag>())
                {
                    var tagEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponent<VoronoiMeshGeneratedTag>(tagEntity);
                    Debug.Log("--- Visualization Complete ---");
                }
                return;
            }

            // 1. Узнаем, какой уровень является ПОСЛЕДНИМ
            var settings = SystemAPI.GetSingleton<MapSettings>();
            int maxLevelIndex = settings.LevelsCount - 1;

            using var entities = query.ToEntityArray(Allocator.Temp);
            int countToProcess = math.min(entities.Length, BATCH_SIZE);

            // Кэширование позиций (только для центрирования меша)
            var siteQ = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
            using var sites = siteQ.ToComponentDataArray<VoronoiSite>(Allocator.Temp);
            var sitePos = new NativeParallelHashMap<int, float2>(sites.Length, Allocator.Temp);
            foreach (var s in sites) sitePos[s.Index] = s.Position;

            var mda = UnityEngine.Mesh.AllocateWritableMeshData(countToProcess);
            var meshes = new UnityEngine.Mesh[countToProcess];

            // --- ЦИКЛ 1: ГЕНЕРАЦИЯ МЕШЕЙ ---
            for (int i = 0; i < countToProcess; i++)
            {
                meshes[i] = new UnityEngine.Mesh { indexFormat = IndexFormat.UInt32 };
                meshes[i].name = $"CellMesh_{i}";

                var e = entities[i];
                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                var triIndices = state.EntityManager.GetBuffer<CellTriIndex>(e);
                var md = mda[i];

                // Если данных мало - пустой меш
                if (verts.Length < 3 || triIndices.Length < 3)
                {
                    md.SetVertexBufferParams(0, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                    md.SetIndexBufferParams(0, IndexFormat.UInt32);
                    md.subMeshCount = 1;
                    md.SetSubMesh(0, new SubMeshDescriptor(0, 0), MeshUpdateFlags.DontRecalculateBounds);
                    continue; 
                }

                float2 center = float2.zero;
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(e);
                if (sitePos.TryGetValue(cell.SiteIndex, out float2 p)) center = p;

                md.SetVertexBufferParams(verts.Length, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                md.SetIndexBufferParams(triIndices.Length, IndexFormat.UInt32);

                var vb = md.GetVertexData<Vector3>(stream: 0);
                for (int v = 0; v < verts.Length; v++)
                {
                    float3 worldPos = verts[v].Value;
                    vb[v] = new Vector3(worldPos.x - center.x, 0f, worldPos.z - center.y);
                }

                var ib = md.GetIndexData<int>();
                for (int idx = 0; idx < triIndices.Length; idx++) ib[idx] = triIndices[idx].Value;

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, triIndices.Length) { topology = MeshTopology.Triangles }, MeshUpdateFlags.DontRecalculateBounds);
            }

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);

            var rma = new RenderMeshArray(new[] { s_DefaultMaterial }, meshes);
            var desc = new RenderMeshDescription(shadowCastingMode: ShadowCastingMode.Off, receiveShadows: false);

            // --- ЦИКЛ 2: НАЗНАЧЕНИЕ КОМПОНЕНТОВ ---
            for (int i = 0; i < countToProcess; i++)
            {
                var e = entities[i];
                if (!state.EntityManager.Exists(e)) continue;

                // Всегда помечаем, что прошли
                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                var detailData = state.EntityManager.GetComponentData<DetailLevelData>(e);

                // === ФИЛЬТР: Скрываем всё, кроме последнего уровня ===
                // Если это L0 или L1, мы просто не вешаем RenderMesh
                if ((int)detailData.Level != maxLevelIndex)
                {
                    continue;
                }
                // ===================================================

                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                if (verts.Length < 3) continue;

                meshes[i].RecalculateBounds();
                var b = meshes[i].bounds;
                b.extents = new Vector3(b.extents.x, 50.0f, b.extents.z);
                meshes[i].bounds = b;

                var site = state.EntityManager.GetComponentData<VoronoiSite>(e);
                // Ставим высоту 0, так как теперь слой один, Z-fighting не страшен
                float3 entityPos = new float3(site.Position.x, 0, site.Position.y);

                if (!state.EntityManager.HasComponent<LocalTransform>(e))
                    state.EntityManager.AddComponentData(e, new LocalTransform { Position = entityPos, Rotation = quaternion.identity, Scale = 1f });

                if (!state.EntityManager.HasComponent<RenderBounds>(e))
                    state.EntityManager.AddComponentData(e, new RenderBounds { Value = new AABB { Center = float3.zero, Extents = new float3(1000, 50, 1000) } });

                // Раскраска по БИОМАМ
                if (!state.EntityManager.HasComponent<Unity.Rendering.URPMaterialPropertyBaseColor>(e))
                {
                    float4 color = new float4(1, 0, 1, 1); // Розовый ошибка

                    if (state.EntityManager.HasComponent<CellBiome>(e))
                    {
                        var biome = state.EntityManager.GetComponentData<CellBiome>(e);
                        color = GetBiomeColor(biome.Type);
                    }
                    else
                    {
                        // Если биомов нет, красим в белый
                        color = new float4(1, 1, 1, 1);
                    }
                    
                    state.EntityManager.AddComponentData(e, new Unity.Rendering.URPMaterialPropertyBaseColor { Value = color });
                }

                var mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, i);
                RenderMeshUtility.AddComponents(e, state.EntityManager, in desc, rma, mmi);
            }

            sitePos.Dispose();
        }

        private static float4 GetBiomeColor(BiomeType type)
        {
            switch (type)
            {
                case BiomeType.Ocean: return new float4(0.0f, 0.2f, 0.7f, 1.0f); // Синий
                case BiomeType.Coast: return new float4(0.9f, 0.8f, 0.5f, 1.0f); // Песок
                case BiomeType.Ice: return new float4(0.9f, 0.95f, 1.0f, 1.0f);
                case BiomeType.Desert: return new float4(0.8f, 0.5f, 0.1f, 1.0f);
                case BiomeType.Grassland: return new float4(0.2f, 0.7f, 0.2f, 1.0f); // Трава
                case BiomeType.Forest: return new float4(0.0f, 0.4f, 0.0f, 1.0f); // Лес
                case BiomeType.Mountain: return new float4(0.4f, 0.4f, 0.4f, 1.0f); // Горы
                case BiomeType.Snow: return new float4(1.0f, 1.0f, 1.0f, 1.0f);
                default: return new float4(0.5f, 0.5f, 0.5f, 1);
            }
        }

        private static Material EnsureDefaultMaterial()
        {
            string path = "Materials/CellMaterial";
            Material mat = Resources.Load<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                mat = new Material(shader);
                mat.enableInstancing = true;
            }
            return mat;
        }
    }
}