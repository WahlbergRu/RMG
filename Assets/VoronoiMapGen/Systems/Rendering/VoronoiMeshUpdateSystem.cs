using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Rendering;
using VoronoiMapGen.Systems.Rendering;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMeshCreateSystem))]
    public partial class VoronoiMeshUpdateSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Пустая система для будущего расширения
        }
    }
}