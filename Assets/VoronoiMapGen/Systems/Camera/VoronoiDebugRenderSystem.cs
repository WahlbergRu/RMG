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
            // 1. Получаем настройки. Если их нет - выходим.
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;

            // 2. Если галочка выключена - не тратим ресурсы процессора.
            if (!settings.ShowDebugWireframe) return;

            int targetLevel = settings.DebugLevelToDraw;

            // 3. Проходим по всем рёбрам
            foreach (var (edge, levelData) in SystemAPI.Query<RefRO<VoronoiEdge>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)levelData.ValueRO.Level;

                // Фильтр: Если задан конкретный уровень (!= -1) и он не совпадает — пропускаем
                if (targetLevel != -1 && lvl != targetLevel) continue;

                // Выбираем цвет для уровня
                Color c = (lvl < _levelColors.Length) ? _levelColors[lvl] : Color.gray;

                // Смещение по высоте, чтобы уровни не слипались визуально
                // L0 ниже, L1 чуть выше, L2 еще выше.
                float yOffset = 1.0f + (lvl * 0.5f); 

                float3 start = new float3(edge.ValueRO.VertexA.x, yOffset, edge.ValueRO.VertexA.y);
                float3 end = new float3(edge.ValueRO.VertexB.x, yOffset, edge.ValueRO.VertexB.y);

                Debug.DrawLine(start, end, c);
            }
        }
    }
}