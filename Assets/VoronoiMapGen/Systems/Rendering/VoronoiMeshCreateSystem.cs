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
        }

        public void OnUpdate(ref SystemState state)
        {
            // 1. Запрос сущностей. ВАЖНО: Включаем VoronoiSite, чтобы читать позицию
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, VoronoiSite, CellPolygonVertex, CellTriIndex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            // 2. Логика завершения
            if (query.IsEmpty)
            {
                if (!SystemAPI.HasSingleton<VoronoiMeshGeneratedTag>())
                {
                    var tagEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponent<VoronoiMeshGeneratedTag>(tagEntity);
                    Debug.Log("--- Visualization Complete. ---");
                }
                return;
            }

            using var entities = query.ToEntityArray(Allocator.Temp);
            int countToProcess = math.min(entities.Length, BATCH_SIZE);

            // Нам НЕ НУЖЕН sitePos HashMap. Мы будем брать позицию прямо из сущности.
            // Это предотвращает баг со смещением уровней.

            var mda = UnityEngine.Mesh.AllocateWritableMeshData(countToProcess);
            var meshes = new UnityEngine.Mesh[countToProcess];

            // --- ЦИКЛ 1: ГЕОМЕТРИЯ ---
            for (int i = 0; i < countToProcess; i++)
            {
                meshes[i] = new UnityEngine.Mesh { indexFormat = IndexFormat.UInt32 };
                meshes[i].name = $"CellMesh_{i}";

                var e = entities[i];
                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                var triIndices = state.EntityManager.GetBuffer<CellTriIndex>(e);
                var md = mda[i];

                // Фикс ArgumentException (пустой меш)
                if (verts.Length < 3 || triIndices.Length < 3)
                {
                    md.SetVertexBufferParams(0, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                    md.SetIndexBufferParams(0, IndexFormat.UInt32);
                    md.subMeshCount = 1;
                    md.SetSubMesh(0, new SubMeshDescriptor(0, 0), MeshUpdateFlags.DontRecalculateBounds);
                    continue; 
                }

                // Прямой доступ к компоненту (Фикс разлета)
                var site = state.EntityManager.GetComponentData<VoronoiSite>(e);
                float2 center = site.Position;

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

            // --- ЦИКЛ 2: КОМПОНЕНТЫ ---
            for (int i = 0; i < countToProcess; i++)
            {
                var e = entities[i];
                if (!state.EntityManager.Exists(e)) continue;

                // Всегда помечаем как обработанное
                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                var detailData = state.EntityManager.GetComponentData<DetailLevelData>(e);

                // === ФИЛЬТР ВИДИМОСТИ ===
                // Рисуем только последний уровень (L2), чтобы не было каши.
                // (Предполагаем, что макс уровень = 2. Если у вас 3 уровня, ставьте < 2 или < 3)
                if ((int)detailData.Level < 2) 
                {
                    continue; 
                }
                // ========================

                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                if (verts.Length < 3) continue;

                meshes[i].RecalculateBounds();
                var b = meshes[i].bounds;
                b.extents = new Vector3(b.extents.x, 50.0f, b.extents.z); // Толстые границы для Scene View
                meshes[i].bounds = b;

                var site = state.EntityManager.GetComponentData<VoronoiSite>(e);
                float3 entityPos = new float3(site.Position.x, 0, site.Position.y);

                if (!state.EntityManager.HasComponent<LocalTransform>(e))
                    state.EntityManager.AddComponentData(e, new LocalTransform { Position = entityPos, Rotation = quaternion.identity, Scale = 1f });

                if (!state.EntityManager.HasComponent<RenderBounds>(e))
                    state.EntityManager.AddComponentData(e, new RenderBounds { Value = new AABB { Center = float3.zero, Extents = new float3(1000, 50, 1000) } });

                // Раскраска
                if (!state.EntityManager.HasComponent<Unity.Rendering.URPMaterialPropertyBaseColor>(e))
                {
                    float4 color = new float4(0.5f, 0.5f, 0.5f, 1);
                    
                    // Приоритет биомам
                    if (state.EntityManager.HasComponent<CellBiome>(e))
                    {
                        var biome = state.EntityManager.GetComponentData<CellBiome>(e);
                        color = GetBiomeColor(biome.Type);
                    }
                    else
                    {
                        // Фоллбек по уровням
                        int level = (int)detailData.Level;
                        if (level == 0) color = new float4(0, 0, 1, 1);
                        else if (level == 1) color = new float4(0, 1, 0, 1);
                        else color = new float4(1, 0.5f, 0, 1);
                    }
                    
                    state.EntityManager.AddComponentData(e, new Unity.Rendering.URPMaterialPropertyBaseColor { Value = color });
                }

                var mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, i);
                RenderMeshUtility.AddComponents(e, state.EntityManager, in desc, rma, mmi);
            }
            
            // sitePos.Dispose(); <--- УБРАНО, так как мы его не создавали
        }

        private static float4 GetBiomeColor(BiomeType type)
        {
            switch (type)
            {
                case BiomeType.Ocean: return new float4(0.0f, 0.2f, 0.7f, 1.0f);
                case BiomeType.Coast: return new float4(0.9f, 0.8f, 0.6f, 1.0f);
                case BiomeType.Ice: return new float4(0.8f, 0.9f, 1.0f, 1.0f);
                case BiomeType.Desert: return new float4(0.9f, 0.8f, 0.4f, 1.0f);
                case BiomeType.Grassland: return new float4(0.3f, 0.7f, 0.2f, 1.0f);
                case BiomeType.Forest: return new float4(0.1f, 0.4f, 0.1f, 1.0f);
                case BiomeType.Mountain: return new float4(0.5f, 0.5f, 0.5f, 1.0f);
                case BiomeType.Snow: return new float4(0.95f, 0.95f, 0.95f, 1.0f);
                default: return new float4(1, 0, 1, 1);
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
                mat.color = Color.blue;
            }
            mat.enableInstancing = true;
            return mat;
        }
    }
}