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
        // Палитра
        private readonly Color[] _levelColors = new Color[] 
        { 
            Color.magenta,      // L0
            Color.yellow,       // L1
            Color.cyan,         // L2
            new Color(0,1,0.5f) // L3
        };

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            
            if (settings.ShowRiverGizmos) 
            {
                DrawRivers(settings); // <-- Передаем настройки с маской
            }

            if (settings.ShowDebugWireframe) 
            {
                DrawGrid(settings);
            }
        }

        private void DrawRivers(MapSettings settings)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, HydrologyData, DetailLevelData>()
                .Build();
            
            if (query.IsEmpty) return;

            var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            var hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);
            var levels = query.ToComponentDataArray<DetailLevelData>(Allocator.Temp);
            
            // Используем маску ОТЛАДКИ (Debug Mask)
            int debugMask = settings.RiverDebugMask;

            var posMap = new NativeParallelHashMap<int, float3>(cells.Length, Allocator.Temp);
            
            for (int i = 0; i < cells.Length; i++) 
            {
                int lvl = (int)levels[i].Level;
                float heightOffset = 160.0f - (lvl * 30.0f);
                posMap.TryAdd(cells[i].SiteIndex, new float3(cells[i].Centroid.x, heightOffset, cells[i].Centroid.y));
            }

            for (int i = 0; i < hydro.Length; i++)
            {
                var h = hydro[i];
                if (h.IsRiver && h.FlowTargetIndex != -1)
                {
                    // Проверка Уровня
                    int lvl = (int)levels[i].Level;
                    if ((debugMask & (1 << lvl)) == 0) continue; // Не рисуем

                    if (posMap.TryGetValue(cells[i].SiteIndex, out float3 start) &&
                        posMap.TryGetValue(h.FlowTargetIndex, out float3 end))
                    {
                        // Цвет
                        Color c = (lvl < _levelColors.Length) ? _levelColors[lvl] : Color.white;
                        
                        Debug.DrawLine(start, end, c);
                        Debug.DrawLine(start, start + new float3(0, 10, 0), c * 0.7f); // Pin
                    }
                }
            }
            
            posMap.Dispose();
            cells.Dispose();
            hydro.Dispose();
            levels.Dispose();
        }

        private void DrawGrid(MapSettings settings)
        {
            int mask = settings.DebugLevelMask;
            var customColors = settings.DebugLayerColors;

            foreach (var (verts, lvlData) in SystemAPI.Query<DynamicBuffer<CellPolygonVertex>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)lvlData.ValueRO.Level;
                if ((mask & (1 << lvl)) == 0) continue;
                if (verts.Length < 2) continue;

                Color c = Color.white;
                if (lvl < customColors.Length) c = new Color(customColors[lvl].x, customColors[lvl].y, customColors[lvl].z);
                else if (lvl < _levelColors.Length) c = _levelColors[lvl];

                float yOffset = 10.0f + (lvl * 2.0f);
                var vArray = verts.AsNativeArray();
                for (int i = 0; i < vArray.Length; i++) 
                {
                    float3 a = vArray[i].Value;
                    float3 b = vArray[(i + 1) % vArray.Length].Value;
                    Debug.DrawLine(new float3(a.x, yOffset, a.z), new float3(b.x, yOffset, b.z), c);
                }
            }
        }
    }
}