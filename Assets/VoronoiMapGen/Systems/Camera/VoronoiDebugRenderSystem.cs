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
        // Палитра для дальтоников (High Contrast)
        private readonly Color[] _levelColors = new Color[]
        {
            Color.black,            // L0: Черный (Контуры стран)
            new Color(1f, 0.8f, 0f),// L1: Золотой/Желтый (Регионы) - хорошо виден на синем/зеленом
            Color.white,            // L2: Белый (Города/Детали)
            Color.cyan              // L3+: Циан
        };

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (!settings.ShowDebugWireframe) return;
            
            int mask = settings.DebugLevelMask;

            foreach (var (vertsBuffer, levelData) in SystemAPI.Query<DynamicBuffer<CellPolygonVertex>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)levelData.ValueRO.Level;
                
                // Пропускаем выключенные уровни
                if ((mask & (1 << lvl)) == 0) continue;
                if (vertsBuffer.Length < 2) continue;

                Color c = (lvl < _levelColors.Length) ? _levelColors[lvl] : Color.magenta;
                
                // СИЛЬНОЕ разнесение по высоте ("Этажерка")
                // L0 = 50м, L1 = 100м, L2 = 150м. 
                // Так они не будут "мерцать" друг в друге.
                float yOffset = 50.0f + (lvl * 50.0f); 

                var verts = vertsBuffer.AsNativeArray();
                for (int i = 0; i < verts.Length; i++)
                {
                    float3 a = verts[i].Value;
                    float3 b = verts[(i + 1) % verts.Length].Value; // Замыкаем полигон

                    float3 start = new float3(a.x, yOffset, a.z);
                    float3 end = new float3(b.x, yOffset, b.z);

                    // ТРЮК С ТОЛЩИНОЙ
                    if (lvl == 0)
                    {
                        // Рисуем жирную черную линию для L0 (3 линии рядом)
                        DrawThickLine(start, end, c, 0.4f); 
                    }
                    else if (lvl == 1)
                    {
                        // Рисуем чуть жирнее для L1 (2 линии)
                        DrawThickLine(start, end, c, 0.2f);
                    }
                    else
                    {
                        // Обычная тонкая линия для L2
                        Debug.DrawLine(start, end, c);
                    }
                }
            }
        }

        // Хелпер для имитации толщины Debug.DrawLine
        private void DrawThickLine(float3 a, float3 b, Color color, float width)
        {
            // Центральная
            Debug.DrawLine(a, b, color);
            
            // Смещения по X и Z
            float3 offset1 = new float3(width, 0, width);
            float3 offset2 = new float3(-width, 0, width);
            
            Debug.DrawLine(a + offset1, b + offset1, color);
            Debug.DrawLine(a - offset1, b - offset1, color);
            // Можно добавить еще, если нужно жирнее
        }
    }
}