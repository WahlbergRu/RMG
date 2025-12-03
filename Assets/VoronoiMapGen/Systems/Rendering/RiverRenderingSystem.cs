// using Unity.Entities;
// using Unity.Collections;
// using UnityEngine;
// using VoronoiMapGen.Components;
// using VoronoiMapGen.Utils;
//
// namespace VoronoiMapGen.Systems.Rendering
// {
//     [UpdateInGroup(typeof(PresentationSystemGroup))]
//     public partial class RiverRenderingSystem : SystemBase
//     {
//         private Material _riverMaterial;
//         private bool _isBuilt = false;
//
//         protected override void OnCreate()
//         {
//             // ОБЯЗАТЕЛЬНО использовать URP шейдер для DOTS
//             var shader = Shader.Find("Universal Render Pipeline/Unlit"); // Для воды Unlit подойдет
//             if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
//             
//             if (shader == null) {
//                 Debug.LogError("RiverSystem: URP Shader missing!");
//                 return;
//             }
//
//             _riverMaterial = new Material(shader);
//             _riverMaterial.color = new Color(0.0f, 0.4f, 0.8f, 1.0f);
//             _riverMaterial.enableInstancing = true; // Важно!
//             
//             RequireForUpdate<GeometryBuiltTag>();
//             RequireForUpdate<MapGeneratedTag>();
//         }
//
//         protected override void OnUpdate()
//         {
//             if (_isBuilt) return;
//
//             if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
//
//             // Вызываем наш статический билдер
//             Debug.Log("[RiverSystem] Starting river mesh generation...");
//             // RiverMeshBuilder.Build(EntityManager, _riverMaterial, settings);
//             
//             _isBuilt = true;
//         }
//     }
// }