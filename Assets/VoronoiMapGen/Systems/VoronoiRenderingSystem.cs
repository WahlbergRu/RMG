// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Rendering;
// using Unity.Transforms;
// using UnityEngine;
//
// namespace VoronoiMapGen.Systems
// {
//     [UpdateInGroup(typeof(PresentationSystemGroup))]
//     public partial class VoronoiRenderingSystem : SystemBase
//     {
//         private EntityQuery _edgeQuery;
//         private EntityQuery _cellQuery;
//         private EntityQuery _mapGeneratedQuery;
//
//         protected override void OnCreate()
//         {
//             _edgeQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiEdge>());
//             _cellQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiCell>());
//             _mapGeneratedQuery = GetEntityQuery(ComponentType.ReadOnly<Components.MapGeneratedTag>());
//         }
//
//         protected override void OnUpdate()
//         {
//             if (_mapGeneratedQuery.IsEmpty)
//                 return;
//         }
//     }
// }