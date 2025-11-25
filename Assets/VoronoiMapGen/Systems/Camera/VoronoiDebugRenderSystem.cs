// Файл: Systems/Camera/VoronoiDebugRenderSystem.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiDebugRenderSystem : SystemBase
    {
        // Цвета для разных уровней детализации (до 6 уровней)
        private readonly Color[] _levelColors = new Color[]
        {
            Color.red,      // L0 (Глобальный)
            Color.green,    // L1 (Региональный)
            Color.cyan,     // L2 (Поселения)
            Color.yellow,   // L3
            Color.magenta,  // L4
            Color.white     // L5
        };

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (!settings.ShowDebugWireframe) return;
            
            int mask = settings.DebugLevelMask;

            foreach (var (edge, levelData) in SystemAPI.Query<RefRO<VoronoiEdge>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)levelData.ValueRO.Level;
                
                // === ПРОВЕРКА МАСКИ ===
                // Сдвигаем 1 на lvl позиций влево. 
                // Если в маске на этой позиции есть 1, результат & будет > 0.
                bool isLevelVisible = (mask & (1 << lvl)) != 0;

                if (!isLevelVisible) continue;
                // ======================

                Color c = (lvl < _levelColors.Length) ? _levelColors[lvl] : Color.gray;
                float yOffset = 1.0f + (lvl * 0.5f); 

                float3 start = new float3(edge.ValueRO.VertexA.x, yOffset, edge.ValueRO.VertexA.y);
                float3 end = new float3(edge.ValueRO.VertexB.x, yOffset, edge.ValueRO.VertexB.y);

                Debug.DrawLine(start, end, c);
            }
        }
    }
}