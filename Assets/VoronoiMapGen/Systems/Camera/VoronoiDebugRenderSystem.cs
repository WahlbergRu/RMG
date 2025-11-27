using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiDebugRenderSystem : SystemBase
    {
        private readonly Color[] _levelColors = new Color[]
        {
            Color.black,            // L0
            new Color(1f, 0.8f, 0f),// L1 (Yellow)
            Color.white,            // L2 (White)
            Color.cyan              // L3
        };

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (!settings.ShowDebugWireframe) return;
            
            int mask = settings.DebugLevelMask;

            // 1. Отрисовка контуров ячеек (Voronoi Edges)
            // Мы берем данные из CellPolygonVertex, которые уже построены
            foreach (var (vertsBuffer, levelData) in SystemAPI.Query<DynamicBuffer<CellPolygonVertex>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)levelData.ValueRO.Level;
                if ((mask & (1 << lvl)) == 0) continue;
                if (vertsBuffer.Length < 2) continue;

                Color c = (lvl < _levelColors.Length) ? _levelColors[lvl] : Color.magenta;
                
                // Поднимаем линии повыше, чтобы не мерцали с землей
                float yOffset = 2.0f + (lvl * 1.0f); 

                var verts = vertsBuffer.AsNativeArray();
                for (int i = 0; i < verts.Length; i++)
                {
                    float3 a = verts[i].Value;
                    float3 b = verts[(i + 1) % verts.Length].Value; // Замыкаем

                    // Рисуем чуть выше земли
                    Debug.DrawLine(
                        new float3(a.x, yOffset, a.z), 
                        new float3(b.x, yOffset, b.z), 
                        c
                    );
                }
            }
            
            // 2. Отрисовка направления рек (Опционально, для отладки гидрологии)
            // Если включен дебаг для L1 (Regional)
            if ((mask & (1 << 1)) != 0) 
            {
                 foreach (var (hydro, cell) in SystemAPI.Query<RefRO<HydrologyData>, RefRO<VoronoiCell>>())
                 {
                     if (hydro.ValueRO.IsRiver)
                     {
                         // Рисуем маленькую красную точку в центре речной ячейки
                         float3 c = new float3(cell.ValueRO.Centroid.x, 5f, cell.ValueRO.Centroid.y);
                         Debug.DrawLine(c, c + new float3(0, 2, 0), Color.blue);
                     }
                 }
            }
        }
    }
}