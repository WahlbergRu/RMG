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
            if (!SystemAPI.TryGetSingleton<MapSettings>(out MapSettings settings)) return;
            if (!settings.ShowDebugWireframe && !settings.ShowRiverGizmos) return;
            
            if (settings.ShowDebugWireframe) DrawGrid(settings);

            if (settings.ShowRiverGizmos) DrawRivers(settings);
        }

        private void DrawRivers(MapSettings settings)
        {
            Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;
            NativeArray<TerrainVisualData> styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity).ToNativeArray(Allocator.Temp);

            EntityQuery query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, HydrologyData, DetailLevelData, CellBiome>()
                .Build();

            if (query.IsEmpty) return;

            NativeArray<VoronoiCell> cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            NativeArray<HydrologyData> hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);
            NativeArray<DetailLevelData> levels = query.ToComponentDataArray<DetailLevelData>(Allocator.Temp);
            NativeArray<CellBiome> biomes = query.ToComponentDataArray<CellBiome>(Allocator.Temp);

            int debugMask = settings.RiverDebugMask;

            NativeParallelHashMap<int, float3> posMap = new NativeParallelHashMap<int, float3>(cells.Length, Allocator.Temp);

            // 1. Собираем позиции
            for (int i = 0; i < cells.Length; i++)
            {
                int lvl = (int)levels[i].Level;
                int styleIdx = Mathf.Clamp(lvl, 0, styles.Length - 1);
                float heightScale = styles[styleIdx].HeightScale;

                float elevation = biomes[i].Elevation;
                if (biomes[i].Type == BiomeType.Ocean) elevation = 0.1f;

                float yPos = math.pow(math.max(0, elevation), 1.5f) * heightScale + 1.0f;
                float3 worldPos = new float3(cells[i].Centroid.x, yPos, cells[i].Centroid.y);

                int key = (lvl << 24) + cells[i].SiteIndex;
                posMap.TryAdd(key, worldPos);
            }

            // 2. Рисуем связи
            for (int i = 0; i < hydro.Length; i++)
            {
                HydrologyData h = hydro[i];
                int lvl = (int)levels[i].Level;

                if ((debugMask & (1 << lvl)) == 0) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                int myKey = (lvl << 24) + cells[i].SiteIndex;

                if (!posMap.TryGetValue(myKey, out float3 start)) continue;

                // --- ЛОГИКА ОТРИСОВКИ ---

                // А. ТУПИК (Озеро/Яма)
                if (h.FlowTargetIndex == -1)
                {
                    // ИСПРАВЛЕНИЕ: Используем Debug.DrawLine вместо Gizmos
                    // Рисуем высокий красный столб с перекрестием
                    float3 top = start + new float3(0, 15, 0);

                    Debug.DrawLine(start, top, Color.red); // Столб

                    // Перекрестие наверху
                    float crossSize = 3.0f;
                    Debug.DrawLine(top - new float3(crossSize, 0, 0), top + new float3(crossSize, 0, 0), Color.red);
                    Debug.DrawLine(top - new float3(0, 0, crossSize), top + new float3(0, 0, crossSize), Color.red);

                    continue;
                }

                // Б. ПОТОК
                int targetKey = (lvl << 24) + h.FlowTargetIndex;
                if (posMap.TryGetValue(targetKey, out float3 end))
                {
                    // Если поток сильный - цветная линия
                    if (h.IsRiver)
                    {
                        Color c = lvl < _levelColors.Length ? _levelColors[lvl] : Color.white;
                        Debug.DrawLine(start, end, c);

                        // "Шпилька" посередине, чтобы видеть направление
                        float3 mid = (start + end) * 0.5f;
                        Debug.DrawLine(mid, mid + new float3(0, 5, 0), c);
                    }
                    else
                    {
                        // Слабый сток - серая тонкая линия
                        Color weakColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
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
            int mask = settings.DebugLevelMask;
            FixedList128Bytes<float4> customColors = settings.DebugLayerColors;

            foreach ((DynamicBuffer<CellPolygonVertex> verts, RefRO<DetailLevelData> lvlData) in
                     SystemAPI.Query<DynamicBuffer<CellPolygonVertex>, RefRO<DetailLevelData>>())
            {
                int lvl = (int)lvlData.ValueRO.Level;
                if ((mask & (1 << lvl)) == 0) continue;
                if (verts.Length < 2) continue;

                Color c = Color.white;
                if (customColors.Length > lvl)
                    c = new Color(customColors[lvl].x, customColors[lvl].y, customColors[lvl].z);
                else if (lvl < _levelColors.Length)
                    c = _levelColors[lvl];

                float yOffset = 10.0f + lvl * 5.0f;
                NativeArray<CellPolygonVertex> vArray = verts.AsNativeArray();
                for (int i = 0; i < vArray.Length; i++)
                {
                    float3 a = vArray[i].Value;
                    float3 b = vArray[(i + 1) % vArray.Length].Value;
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