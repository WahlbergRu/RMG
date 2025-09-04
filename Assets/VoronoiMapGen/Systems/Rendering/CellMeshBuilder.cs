// using System.Collections.Generic;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Rendering;
// using Unity.Transforms;
// using UnityEngine;
// using UnityEngine.Rendering;
// using VoronoiMapGen.Components;
//
// namespace VoronoiMapGen.Rendering
// {
//     public static class CellMeshBuilder
//     {
//         public static void Build(EntityManager em, Material material)
//         {
//             var query = em.CreateEntityQuery(
//                 ComponentType.ReadOnly<CellPolygonVertex>(),
//                 ComponentType.ReadOnly<CellTriIndex>(),
//                 ComponentType.ReadOnly<VoronoiCell>(),
//                 ComponentType.Exclude<VoronoiCellMeshTag>());
//
//             using var entities = query.ToEntityArray(Allocator.Temp);
//             if (entities.Length == 0) return;
//
//             var meshes   = new List<Mesh>();
//             var cellList = new List<Entity>();
//
//             foreach (var entity in entities)
//             {
//                 if (!em.Exists(entity)) continue;
//
//                 var verts = em.GetBuffer<CellPolygonVertex>(entity);
//                 var tris  = em.GetBuffer<CellTriIndex>(entity);
//
//                 if (verts.Length < 3 || tris.Length < 3) continue;
//
//                 meshes.Add(CreateMeshFromCellLocal(em, entity, verts, tris));
//                 cellList.Add(entity);
//             }
//
//             if (cellList.Count == 0) return;
//
//             var renderMeshArray = new RenderMeshArray(new[] { material }, meshes.ToArray());
//             var desc = new RenderMeshDescription(ShadowCastingMode.On, true);
//
//             for (int i = 0; i < cellList.Count; i++)
//                 SetupCellEntity(em, cellList[i], renderMeshArray, desc, i);
//         }
//
//         private static Mesh CreateMeshFromCellLocal(EntityManager em, Entity entity,
//             DynamicBuffer<CellPolygonVertex> verts, DynamicBuffer<CellTriIndex> tris)
//         {
//             var cell = em.GetComponentData<VoronoiCell>(entity);
//             var c = cell.Centroid;
//
//             var mesh = new Mesh
//             {
//                 name = $"CellMesh_{entity.Index}",
//                 indexFormat = IndexFormat.UInt32
//             };
//
//             var vArray = new Vector3[verts.Length];
//
//             for (int i = 0; i < verts.Length; i++)
//             {
//                 float3 v = verts[i].Value; 
//
//                 vArray[i] = new Vector3(v.x - c.x, v.y, v.z - c.y);
//             }
//
//             var tArray = new int[tris.Length];
//             for (int i = 0; i < tris.Length; i++)
//                 tArray[i] = tris[i].Value;
//
//             mesh.SetVertices(vArray);
//             mesh.SetTriangles(tArray, 0);
//             mesh.RecalculateBounds();
//             mesh.RecalculateNormals();
//             return mesh;
//         }
//
//         private static void SetupCellEntity(EntityManager em, Entity entity,
//             RenderMeshArray renderMeshArray, RenderMeshDescription desc, int meshIndex)
//         {
//             if (!em.Exists(entity)) return;
//
//             float height = em.HasComponent<CellHeight>(entity) 
//                 ? em.GetComponentData<CellHeight>(entity).Value 
//                 : 0f;
//
//             float3 pos = em.HasComponent<CellLocalPosition>(entity)
//                 ? em.GetComponentData<CellLocalPosition>(entity).Value
//                 : new float3(em.GetComponentData<VoronoiCell>(entity).Centroid.x, height,
//                     em.GetComponentData<VoronoiCell>(entity).Centroid.y);
//
//             RenderMeshUtility.AddComponents(entity, em, desc, renderMeshArray,
//                 MaterialMeshInfo.FromRenderMeshArrayIndices(0, meshIndex));
//
//             if (em.HasComponent<CellBiome>(entity))
//             {
//                 var biome = em.GetComponentData<CellBiome>(entity);
//                 em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = BiomeColors.Get(biome.Type) });
//             }
//
//             em.AddComponentData(entity, LocalTransform.FromPosition(pos));
//             em.AddComponent<VoronoiCellMeshTag>(entity);
//         }
//     }
// }
