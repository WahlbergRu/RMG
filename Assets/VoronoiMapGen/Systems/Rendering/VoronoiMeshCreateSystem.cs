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

namespace VoronoiMapGen.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct VoronoiMeshCreateSystem : ISystem
    {
        private static Material s_DefaultMaterial;

        public void OnCreate(ref SystemState state)
        {
            if (s_DefaultMaterial == null) s_DefaultMaterial = RenderUtils.EnsureMaterial();
            state.RequireForUpdate<MapGeneratedTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            if (query.IsEmpty) return;

            var settings = SystemAPI.GetSingleton<MapSettings>();
            int debugMask = settings.DebugLevelMask;

            using var entities = query.ToEntityArray(Allocator.Temp);
            int count = math.min(entities.Length, 2000); // Batch limit

            // Кэш позиций для локального пивота
            var siteQuery = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
            var sitesArr = siteQuery.ToComponentDataArray<VoronoiSite>(Allocator.Temp);
            var sitePosMap = new NativeParallelHashMap<int, float2>(sitesArr.Length, Allocator.Temp);
            foreach (var s in sitesArr) sitePosMap.TryAdd(s.Index, s.Position);
            sitesArr.Dispose();

            var mda = Mesh.AllocateWritableMeshData(count);
            var meshes = new Mesh[count];

            for (int i = 0; i < count; i++)
            {
                meshes[i] = new Mesh { name = $"Cell_{i}" };
                var e = entities[i];
                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                var tris = state.EntityManager.GetBuffer<CellTriIndex>(e);
                var md = mda[i];

                if (verts.Length < 3) {
                    md.SetVertexBufferParams(0, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                    md.SetIndexBufferParams(0, IndexFormat.UInt32);
                    md.subMeshCount = 1;
                    continue;
                }

                float2 center = float2.zero;
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(e);
                if (sitePosMap.TryGetValue(cell.SiteIndex, out float2 p)) center = p;

                md.SetVertexBufferParams(verts.Length, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                md.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);

                var vb = md.GetVertexData<Vector3>(0);
                for (int v = 0; v < verts.Length; v++)
                    vb[v] = new Vector3(verts[v].Value.x - center.x, 0, verts[v].Value.z - center.y);

                var ib = md.GetIndexData<int>();
                for (int t = 0; t < tris.Length; t++) ib[t] = tris[t].Value;

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), MeshUpdateFlags.DontRecalculateBounds);
            }

            Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);
            var rma = new RenderMeshArray(new[] { s_DefaultMaterial }, meshes);
            var desc = new RenderMeshDescription(ShadowCastingMode.Off, false);

            for (int i = 0; i < count; i++)
            {
                var e = entities[i];
                if (!state.EntityManager.Exists(e)) continue;
                
                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                var lvl = (int)state.EntityManager.GetComponentData<DetailLevelData>(e).Level;
                if ((debugMask & (1 << lvl)) == 0) continue; // Проверка галочки в инспекторе

                var cell = state.EntityManager.GetComponentData<VoronoiCell>(e);
                float2 center = float2.zero;
                if (sitePosMap.TryGetValue(cell.SiteIndex, out float2 p)) center = p;

                // Bounds fix
                meshes[i].RecalculateBounds();
                var b = meshes[i].bounds; b.extents += new Vector3(0, 10, 0); meshes[i].bounds = b;

                // Transform
                state.EntityManager.AddComponentData(e, new LocalTransform { Position = new float3(center.x, lvl * 0.1f, center.y), Rotation = quaternion.identity, Scale = 1f });
                state.EntityManager.AddComponentData(e, new RenderBounds { Value = b.ToAABB() });

                if (!state.EntityManager.HasComponent<URPMaterialPropertyBaseColor>(e))
                {
                    float4 color = new float4(1, 0, 1, 1); // Magenta (ошибка)

                    // Если есть данные биома - используем их
                    if (state.EntityManager.HasComponent<CellBiome>(e))
                    {
                        var biome = state.EntityManager.GetComponentData<CellBiome>(e);
                        color = RenderUtils.GetBiomeColor(biome.Type);
        
                        // Добавляем вариативность цвета в зависимости от температуры/влажности
                        // Чтобы карта не выглядела как плоская раскраска
                        float tint = biome.Temperature * 0.1f; 
                        color.x += tint; 
                        color.y += tint * 0.5f;
                    }
    
                    state.EntityManager.AddComponentData(e, new URPMaterialPropertyBaseColor { Value = color });
                }

                RenderMeshUtility.AddComponents(e, state.EntityManager, desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, i));
            }
            sitePosMap.Dispose();
        }
    }
}