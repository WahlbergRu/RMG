using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Обновляет геометрию только у тех ячеек, где стоит CellDirtyFlag.
    /// Перезаписывает существующие Mesh через MeshData (без перевешивания рендер-компонентов).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMeshCreateSystem))]
    public partial struct VoronoiMeshUpdateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<CellDirtyFlag, VoronoiCellMeshTag, CellPolygonVertex, CellTriIndex, MaterialMeshInfo>()
                .WithAll<RenderMeshArray>() // shared component present
                .Build();

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

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

                // берём текущий Mesh из RenderMeshArray по индексам из MaterialMeshInfo
                var mmi = state.EntityManager.GetComponentData<MaterialMeshInfo>(e);
                var rma = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(e);
                meshes[i] = rma.GetMesh(mmi);
            }

            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.Default);

            // снимаем флаг
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var e in entities) ecb.RemoveComponent<CellDirtyFlag>(e);
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
