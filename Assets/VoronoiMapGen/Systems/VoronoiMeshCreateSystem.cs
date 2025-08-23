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

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Первый проход рендера: создаёт по entity уникальный Mesh через MeshData,
    /// вешает RenderMeshArray+MaterialMeshInfo и индивидуальный цвет URP (белый по умолчанию).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiGeometryBuildSystem))]
    public partial struct VoronoiMeshCreateSystem : ISystem
    {
        private static Material s_DefaultMaterial;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            s_DefaultMaterial ??= EnsureDefaultMaterial();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;
            if (SystemAPI.HasSingleton<VoronoiMeshGeneratedTag>()) return;

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
            var mda = UnityEngine.Mesh.AllocateWritableMeshData(entities.Length);
            var meshes = new UnityEngine.Mesh[entities.Length];

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
                var triPairs = state.EntityManager.GetBuffer<CellTriIndex>(e);

                var md = mda[i];
                md.SetVertexBufferParams(verts.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                md.SetIndexBufferParams(triPairs.Length * 3 / 2, IndexFormat.UInt32);

                var vb = md.GetVertexData<Vector3>();
                for (int v = 0; v < verts.Length; v++)
                    vb[v] = new Vector3(verts[v].Value.x, 0f, verts[v].Value.y);

                var ib = md.GetIndexData<int>();
                int idx = 0;
                for (int t = 0; t < triPairs.Length; t += 2)
                {
                    ib[idx++] = 0;
                    ib[idx++] = triPairs[t + 0].Value;
                    ib[idx++] = triPairs[t + 1].Value;
                }

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, idx) { topology = MeshTopology.Triangles },
                              MeshUpdateFlags.DontRecalculateBounds);

                meshes[i] = new UnityEngine.Mesh { indexFormat = IndexFormat.UInt32 };
            }

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.Default);

            // общий RenderMeshArray: 1 материал, N мешей (индивидуальность — по MeshArrayIndex)
            var rma = new RenderMeshArray(new[] { s_DefaultMaterial }, meshes);
            var desc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];

                // позиция: local override либо позиция сайта
                float3 pos;
                if (state.EntityManager.HasComponent<CellLocalPosition>(e))
                    pos = state.EntityManager.GetComponentData<CellLocalPosition>(e).Value;
                else
                {
                    var siteIndex = state.EntityManager.GetComponentData<VoronoiCell>(e).SiteIndex;
                    sitePos.TryGetValue(siteIndex, out var sp);
                    pos = new float3(sp.x, 0f, sp.y);
                }

                if (!state.EntityManager.HasComponent<LocalTransform>(e))
                    state.EntityManager.AddComponentData(e, new LocalTransform
                    {
                        Position = pos,
                        Rotation = quaternion.identity,
                        Scale = 1f
                    });

                // индивидуальный цвет (URP property)
                if (!state.EntityManager.HasComponent<Unity.Rendering.URPMaterialPropertyBaseColor>(e))
                    state.EntityManager.AddComponentData(e, new Unity.Rendering.URPMaterialPropertyBaseColor
                    {
                        Value = new float4(1, 1, 1, 1)
                    });

                state.EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                // привязка меша i в RenderMeshArray
                var mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, i);
                RenderMeshUtility.AddComponents(e, state.EntityManager, in desc, rma, mmi);
            }

            // помечаем, что всё создано
            var tag = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<VoronoiMeshGeneratedTag>(tag);

            sitePos.Dispose();
        }

        private static Material EnsureDefaultMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Legacy Shaders/Diffuse");
            return new Material(shader);
        }
    }
}
