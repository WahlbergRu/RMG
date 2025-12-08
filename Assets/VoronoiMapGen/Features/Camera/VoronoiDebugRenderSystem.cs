using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiDebugRenderSystem : SystemBase
    {
        private readonly Color[] _levelColors =
        {
            Color.magenta,
            Color.yellow,
            Color.cyan,
            new(0, 1, 0.5f)
        };

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (!settings.ShowDebugWireframe && !settings.ShowRiverGizmos) return;
            
            if (settings.ShowDebugWireframe) DrawGrid(settings);

            if (settings.ShowRiverGizmos) DrawRivers(settings);
        }

        private void DrawRivers(MapSettings settings)
        {
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;
            var styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity).ToNativeArray(Allocator.Temp);

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, HydrologyData, DetailLevelData, CellBiome>()
                .Build();

            if (query.IsEmpty) return;

            var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            var hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);
            var levels = query.ToComponentDataArray<DetailLevelData>(Allocator.Temp);
            var biomes = query.ToComponentDataArray<CellBiome>(Allocator.Temp);

            var debugMask = settings.RiverDebugMask;

            var posMap = new NativeParallelHashMap<int, float3>(cells.Length, Allocator.Temp);

            // 1. Собираем позиции
            for (var i = 0; i < cells.Length; i++)
            {
                var lvl = (int)levels[i].Level;
                var styleIdx = Mathf.Clamp(lvl, 0, styles.Length - 1);
                var heightScale = styles[styleIdx].HeightScale;

                var elevation = biomes[i].Elevation;
                if (biomes[i].Type == BiomeType.Ocean) elevation = 0.1f;

                var yPos = math.pow(math.max(0, elevation), 1.5f) * heightScale + 1.0f;
                var worldPos = new float3(cells[i].Centroid.x, yPos, cells[i].Centroid.y);

                var key = (lvl << 24) + cells[i].SiteIndex;
                posMap.TryAdd(key, worldPos);
            }

            // 2. Рисуем связи
            for (var i = 0; i < hydro.Length; i++)
            {
                var h = hydro[i];
                var lvl = (int)levels[i].Level;

                if ((debugMask & (1 << lvl)) == 0) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                var myKey = (lvl << 24) + cells[i].SiteIndex;

                if (!posMap.TryGetValue(myKey, out var start)) continue;

                // --- ЛОГИКА ОТРИСОВКИ ---

                // А. ТУПИК (Озеро/Яма)
                if (h.FlowTargetIndex == -1)
                {
                    // ИСПРАВЛЕНИЕ: Используем Debug.DrawLine вместо Gizmos
                    // Рисуем высокий красный столб с перекрестием
                    var top = start + new float3(0, 15, 0);

                    Debug.DrawLine(start, top, Color.red); // Столб

                    // Перекрестие наверху
                    var crossSize = 3.0f;
                    Debug.DrawLine(top - new float3(crossSize, 0, 0), top + new float3(crossSize, 0, 0), Color.red);
                    Debug.DrawLine(top - new float3(0, 0, crossSize), top + new float3(0, 0, crossSize), Color.red);

                    continue;
                }

                // Б. ПОТОК
                var targetKey = (lvl << 24) + h.FlowTargetIndex;
                if (posMap.TryGetValue(targetKey, out var end))
                {
                    // Если поток сильный - цветная линия
                    if (h.IsRiver)
                    {
                        var c = lvl < _levelColors.Length ? _levelColors[lvl] : Color.white;
                        Debug.DrawLine(start, end, c);

                        // "Шпилька" посередине, чтобы видеть направление
                        var mid = (start + end) * 0.5f;
                        Debug.DrawLine(mid, mid + new float3(0, 5, 0), c);
                    }
                    else
                    {
                        // Слабый сток - серая тонкая линия
                        var weakColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                        Debug.DrawLine(start, end, weakColor);
                    }
                }
            }

            posMap.Dispose();
            cells.Dispose();
            hydro.Dispose();
            levels.Dispose();
            biomes.Dispose();
            styles.Dispose();
        }

        private void DrawGrid(MapSettings settings)
        {
            var mask = settings.DebugLevelMask;
            var customColors = settings.DebugLayerColors;

            foreach (var (verts, lvlData) in
                     SystemAPI.Query<DynamicBuffer<CellPolygonVertex>, RefRO<DetailLevelData>>())
            {
                var lvl = (int)lvlData.ValueRO.Level;
                if ((mask & (1 << lvl)) == 0) continue;
                if (verts.Length < 2) continue;

                var c = Color.white;
                if (customColors.Length > lvl)
                    c = new Color(customColors[lvl].x, customColors[lvl].y, customColors[lvl].z);
                else if (lvl < _levelColors.Length)
                    c = _levelColors[lvl];

                var yOffset = 10.0f + lvl * 5.0f;
                var vArray = verts.AsNativeArray();
                for (var i = 0; i < vArray.Length; i++)
                {
                    var a = vArray[i].Value;
                    var b = vArray[(i + 1) % vArray.Length].Value;
                    Debug.DrawLine(
                        new float3(a.x, yOffset, a.z),
                        new float3(b.x, yOffset, b.z),
                        c
                    );
                }
            }
        }
    }
}