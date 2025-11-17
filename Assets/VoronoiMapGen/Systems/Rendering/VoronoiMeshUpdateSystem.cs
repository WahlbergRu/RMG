// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Entities.Graphics;
// using Unity.Rendering;
// using UnityEngine;
// using UnityEngine.Rendering;
// using VoronoiMapGen.Components;
// using VoronoiMapGen.Systems.Rendering;
//
// namespace VoronoiMapGen.Systems
// {
//     /// <summary>
//     /// Обновляет геометрию только у тех ячеек, где стоит CellDirtyFlag.
//     /// Перезаписывает существующие Mesh через MeshData (без перевешивания рендер-компонентов).
//     /// </summary>
//     [BurstCompile]
//     [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
//     [UpdateInGroup(typeof(PresentationSystemGroup))]
//     [UpdateAfter(typeof(VoronoiMeshCreateSystem))]
//     public partial struct VoronoiMeshUpdateSystem : ISystem
//     {
//         public void OnUpdate(ref SystemState state)
//         {
//             var query = SystemAPI.QueryBuilder()
//                 .WithAll<CellDirtyFlag, VoronoiCellMeshTag, CellPolygonVertex, CellTriIndex, MaterialMeshInfo>()
//                 .WithAll<RenderMeshArray>() // shared component present
//                 .Build();
//
//             using var entities = query.ToEntityArray(Allocator.Temp);
//             if (entities.Length == 0) return;
//
//             var mda = UnityEngine.Mesh.AllocateWritableMeshData(entities.Length);
//             var meshes = new UnityEngine.Mesh[entities.Length];
//
//             Debug.Log("entities " + entities.Length);
//
//             for (int i = 0; i < entities.Length; i++)
//             {
//                 var e = entities[i];
//                 var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(e);
//                 var triPairs = state.EntityManager.GetBuffer<CellTriIndex>(e);
//
//                 var md = mda[i];
//                 md.SetVertexBufferParams(verts.Length,
//                     new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
//                 int requiredIndexCount = (triPairs.Length - 2) * 3;
//                 md.SetIndexBufferParams(requiredIndexCount, IndexFormat.UInt32);
//
//                 var vb = md.GetVertexData<Vector3>();
//                 for (int v = 0; v < verts.Length; v++)
//                     vb[v] = new Vector3(verts[v].Value.x, 0f, verts[v].Value.y);
//
//                 var ib = md.GetIndexData<int>();
//                 // Debug.Log("ib " + ib.Length);
//                 // Debug.Log("triPairs " + triPairs.Length);
//
//
//                 int idx = 0;
//                 // Цикл от 0 до triPairs.Length - 3 (включительно), чтобы использовать i и i+1
//                 for (int it = 0; it < triPairs.Length - 2; it++)
//                 {
//                     // Проверяем, не выйдем ли мы за пределы массива перед записью
//                     if (idx + 2 < ib.Length)
//                     {
//                         ib[idx++] = 0; // Индекс центральной точки (C)
//                         ib[idx++] = triPairs[it].Value;     // Индекс текущей вершины (P[i])
//                         ib[idx++] = triPairs[it + 1].Value; // Индекс следующей вершины (P[i+1])
//                         // Это формирует треугольник (C, P[i], P[i+1])
//                     }
//                     else
//                     {
//                         // Это условие в норме не должно сработать, если SetIndexBufferParams был рассчитан правильно
//                         Debug.LogError($"Index buffer would overflow at tri index {it}. Current idx: {idx}, Buffer length: {ib.Length}");
//                         break;
//                     }
//                 }
//
//
//                 md.subMeshCount = 1;
//                 md.SetSubMesh(0, new SubMeshDescriptor(0, idx) { topology = MeshTopology.Triangles },
//                               MeshUpdateFlags.DontRecalculateBounds);
//
//                 // берём текущий Mesh из RenderMeshArray по индексам из MaterialMeshInfo
//                 var mmi = state.EntityManager.GetComponentData<MaterialMeshInfo>(e);
//                 var rma = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(e);
//                 meshes[i] = rma.GetMesh(mmi);
//             }
//
//             Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);
//
//             // снимаем флаг
//             var ecb = new EntityCommandBuffer(Allocator.Temp);
//             foreach (var e in entities) ecb.RemoveComponent<CellDirtyFlag>(e);
//             ecb.Playback(state.EntityManager);
//             ecb.Dispose();
//         }
//     }
// }