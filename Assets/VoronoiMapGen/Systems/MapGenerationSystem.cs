using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using VoronoiMapGen.Utils;
using VoronoiMapGen.Systems.Utils;
using VoronoiMapGen.Systems.Data; // Подключаем наши новые структуры

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // Менеджер истории теперь хранит MapLevelData (пакеты данных), а не россыпь массивов
        private MapHistoryData m_History;

        private int m_CurrentLevel = 0;
        private bool m_IsInitialized = false;
        private bool m_IsComplete = false;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
        }

        protected override void OnDestroy()
        {
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();
            // Очистка всей истории одной строкой
            if (m_History != null) m_History.Dispose();
        }

        protected override void OnUpdate()
        {
            if (m_IsComplete) return;
            if (!m_IsInitialized) { Initialize(); return; }

            if (m_CurrentLevel < m_LevelSettings.Length)
            {
                ProcessSingleLevel(m_CurrentLevel);
                m_CurrentLevel++;
            }
            else
            {
                CompleteGeneration();
            }
        }

        private void Initialize()
        {
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();

            // Если карта уже есть - выключаемся
            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
            {
                Enabled = false;
                return;
            }

            m_Settings = SystemAPI.GetSingleton<MapSettings>();
            var buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

            int count = m_LevelSettings.Length;
            m_History = new MapHistoryData(count);

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialized. Generating {count} levels...");
        }

        private void ProcessSingleLevel(int level)
        {
            Debug.Log($"--- Processing Level {level} ---");
            LevelSettings levelSettings = m_LevelSettings[level];

            // 1. Локальные переменные для данных текущего уровня.
            // Мы выделим их здесь, заполним, а в конце упакуем в MapLevelData.
            NativeArray<float2> sites = default;
            NativeArray<VoronoiSite> meta = default;
            NativeArray<TectonicPlateData> tectonicData = default;
            NativeArray<ClimateData> climateData = default;
            NativeArray<HydrologyData> hydrologyData = default;
            NativeArray<BiomeData> biomeData = default;

            NativeList<VoronoiCell> cellsList = new NativeList<VoronoiCell>(Allocator.Persistent);
            NativeList<VoronoiEdge> edgesList = new NativeList<VoronoiEdge>(Allocator.Persistent);

            // Кэш
            NativeArray<float2> cachedVerts = default;
            NativeArray<int> cachedCounts = default;
            NativeArray<VoronoiEdge> cachedEdges = default;

            bool loadedFromCache = false;

            // --- ЭТАП 1: КЭШ ---
            if (m_Settings.UseCache && MapCacheUtils.LoadLevel(m_Settings.Seed, level,
                out sites, out meta, out tectonicData, out climateData, out hydrologyData, out biomeData,
                out cachedVerts, out cachedCounts, out cachedEdges))
            {
                Debug.Log($"[MapGen] L{level}: Loaded from Cache.");
                loadedFromCache = true;

                var tempVertsList = new NativeList<float2>(cachedVerts.Length, Allocator.Temp);
                tempVertsList.AddRange(cachedVerts);
                var tempCountsList = new NativeList<int>(cachedCounts.Length, Allocator.Temp);
                tempCountsList.AddRange(cachedCounts);

                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta,
                    new NativeList<TriangleIndices>(0, Allocator.Temp),
                    tempVertsList, tempCountsList,
                    ref cellsList, ref edgesList);

                edgesList.Clear();
                edgesList.AddRange(cachedEdges);

                cachedVerts.Dispose(); cachedCounts.Dispose(); cachedEdges.Dispose();
            }

            // --- ЭТАП 2: ПРОЦЕДУРНАЯ ГЕНЕРАЦИЯ ---
            if (!loadedFromCache)
            {
                // Подготовка родительских данных
                NativeArray<VoronoiCell> pCells = default;
                NativeArray<VoronoiSite> pMeta = default;
                NativeArray<HydrologyData> pHydro = default;
                NativeArray<TectonicPlateData> pTect = default;
                NativeArray<ClimateData> pClim = default;

                // Получаем пакет данных родителя (если это не L0)
                if (m_History.TryGetLevel(level - 1, out MapLevelData parentData))
                {
                    pCells = parentData.Cells;
                    pMeta = parentData.Meta;
                    pHydro = parentData.Hydrology;
                    pTect = parentData.Tectonics;
                    pClim = parentData.Climate;
                }

                // 2.1 Генерация сайтов
                var (rawSites, rawMeta) = SiteGenerator.Generate(
                    m_Settings, levelSettings, level,
                    pCells, pMeta, pHydro, pTect, pClim
                );

                (sites, meta) = MapProcessingHelpers.FilterValidSites(rawSites, rawMeta, Allocator.Persistent);
                rawSites.Dispose(); rawMeta.Dispose();

                // 2.2 Геометрия
                var tri = new NativeList<TriangleIndices>(Allocator.TempJob);
                var cv = new NativeList<float2>(Allocator.TempJob);
                var cc = new NativeList<int>(Allocator.TempJob);

                for (int iter = 0; iter <= levelSettings.RelaxationIterations; iter++)
                {
                    bool isLast = (iter == levelSettings.RelaxationIterations);
                    DelaunayBuilder.Triangulate(sites, ref tri, m_Settings.MapSize);
                    cv.Clear(); cc.Clear();
                    VoronoiBuilder.BuildCells(sites, tri, m_Settings.MapSize, ref cv, ref cc);
                    if (!isLast) ApplyLloydRelaxation(sites, cv, cc, m_Settings.MapSize);
                }

                // 2.3 Данные
                int count = sites.Length;
                tectonicData = new NativeArray<TectonicPlateData>(count, Allocator.Persistent);
                climateData = new NativeArray<ClimateData>(count, Allocator.Persistent);
                biomeData = new NativeArray<BiomeData>(count, Allocator.Persistent);
                hydrologyData = new NativeArray<HydrologyData>(count, Allocator.Persistent);

                // Заглушки для Jobs (если нет родителей)
                var dTm = new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
                var dCm = new NativeArray<ClimateData>(0, Allocator.TempJob);

                new TectonicGenerationJob
                {
                    Seed = m_Settings.Seed + level * 77,
                    MapSize = m_Settings.MapSize,
                    Level = level,
                    Sites = sites,
                    SiteMeta = meta,
                    ParentTectonics = (level == 0) ? dTm : pTect,
                    TectonicData = tectonicData
                }.Schedule(count, 64).Complete();

                new ClimateGenerationJob
                {
                    Seed = m_Settings.Seed + level * 88,
                    MapSize = m_Settings.MapSize,
                    Level = level,
                    Sites = sites,
                    SiteMeta = meta,
                    Tectonics = tectonicData,
                    ParentClimate = (level == 0) ? dCm : pClim,
                    Climate = climateData,
                    Biomes = biomeData
                }.Schedule(count, 64).Complete();

                dTm.Dispose(); dCm.Dispose();

                // 2.4 Гидрология
                NativeList<VoronoiEdge> tempEdges = MapProcessingHelpers.ExtractEdgesFromDelaunay(tri, Allocator.TempJob);
                float maxDistSq = MapProcessingHelpers.CalculateAdaptiveGraphLimit(tri, sites, tectonicData, level);

                var neighborsMap = new NativeParallelMultiHashMap<int, NeighborInfo>(tempEdges.Length * 2, Allocator.TempJob);
                new BuildNeighborGraphJob
                {
                    Edges = tempEdges,
                    SitePositions = sites,
                    Tectonics = tectonicData,
                    MaxConnectionDistSq = maxDistSq,
                    NeighborsMap = neighborsMap
                }.Schedule().Complete();

                var tempCellsForHydro = new NativeArray<VoronoiCell>(count, Allocator.TempJob);
                for (int i = 0; i < count; i++) tempCellsForHydro[i] = new VoronoiCell { SiteIndex = i, Centroid = sites[i] };

                new CalculateHydrologyJob
                {
                    Cells = tempCellsForHydro,
                    Tectonics = tectonicData,
                    Climate = climateData,
                    NeighborsMap = neighborsMap,
                    Hydrology = hydrologyData
                }.Schedule().Complete();

                tempCellsForHydro.Dispose(); neighborsMap.Dispose(); tempEdges.Dispose();

                // 2.5 Сборка геометрии
                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, tri, cv, cc, ref cellsList, ref edgesList);

                if (m_Settings.UseCache)
                {
                    MapCacheUtils.SaveLevel(m_Settings.Seed, level, sites, meta, tectonicData, climateData, hydrologyData, biomeData, cv, cc, edgesList);
                }

                tri.Dispose(); cv.Dispose(); cc.Dispose();
            }

            // --- ЭТАП 3: УПАКОВКА ДАННЫХ (Refactoring Magic) ---
            
            // Превращаем List ячеек в Array для хранения в структуре
            var finalCellsArray = new NativeArray<VoronoiCell>(cellsList.Length, Allocator.Persistent);
            finalCellsArray.CopyFrom(cellsList.AsArray());

            // Создаем единый пакет данных уровня
            var currentLevelData = new MapLevelData
            {
                LevelIndex = level,
                Sites = sites,
                Meta = meta,
                Cells = finalCellsArray,
                Tectonics = tectonicData,
                Climate = climateData,
                Hydrology = hydrologyData,
                Biomes = biomeData
            };

            // --- ЭТАП 4: СОЗДАНИЕ ECS ---
            EntityCreationPipeline.CreateEntities(
                EntityManager,
                currentLevelData, // Передаем пакет
                levelSettings,
                m_Settings.MapSize,
                edgesList // Edges отдельно, т.к. это List
            );

            // --- ЭТАП 5: СОХРАНЕНИЕ В ИСТОРИЮ ---
            // StoreLevel делает глубокую копию, так что мы можем безопасно очищать локальные данные
            m_History.StoreLevel(currentLevelData);

            // --- ЭТАП 6: ОЧИСТКА ---
            // Вызываем Dispose у структуры, она очистит все NativeArray внутри (sites, meta, tectonic и т.д.)
            currentLevelData.Dispose();
            
            // Очищаем списки
            cellsList.Dispose();
            edgesList.Dispose();
        }

        private void ApplyLloydRelaxation(NativeArray<float2> sites, NativeList<float2> verts, NativeList<int> counts, float2 mapSize)
        {
            int offset = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                int vCount = counts[i];
                if (vCount > 0)
                {
                    float2 c = float2.zero; float area = 0.0f;
                    for (int k = 0; k < vCount; k++)
                    {
                        float2 curr = verts[offset + k];
                        float2 next = verts[offset + (k + 1) % vCount];
                        float a = curr.x * next.y - next.x * curr.y;
                        area += a; c += (curr + next) * a;
                    }
                    if (math.abs(area) > 1e-6f) sites[i] = math.clamp(c / (area * 3.0f), 0, mapSize);
                }
                offset += vCount;
            }
        }

        private void CompleteGeneration()
        {
            m_IsComplete = true;
            var sEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            EntityManager.AddComponent<MapGeneratedTag>(sEntity);
            EntityManager.RemoveComponent<MapGenerationInProgress>(sEntity);

            Debug.Log("[MapGen] Complete! System disabled.");
            Enabled = false;
        }
    }
}