using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs; 
using VoronoiMapGen.Utils; 
using VoronoiMapGen.Systems.Utils; // Для MapProcessingHelpers
using VoronoiMapGen.Systems.Data;  // Для MapHistoryData

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // Менеджер истории для хранения данных между уровнями
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
            
            // Если карта уже была сгенерирована (например, при повторном входе в PlayMode без сброса домена), отключаемся
            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity)) 
            { 
                Enabled = false; 
                return; 
            }

            m_Settings = SystemAPI.GetSingleton<MapSettings>();
            var buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

            int count = m_LevelSettings.Length;
            
            // Инициализируем хранилище истории
            m_History = new MapHistoryData(count);

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialized. Generating {count} levels...");
        }

        private void ProcessSingleLevel(int level)
        {
            Debug.Log($"--- Processing Level {level} ---");
            LevelSettings levelSettings = m_LevelSettings[level];

            // 1. Объявляем переменные для данных текущего уровня
            NativeArray<float2> sites = default;
            NativeArray<VoronoiSite> meta = default;
            NativeArray<TectonicPlateData> tectonicData = default;
            NativeArray<ClimateData> climateData = default;
            NativeArray<HydrologyData> hydrologyData = default;
            NativeArray<BiomeData> biomeData = default;

            NativeList<VoronoiCell> finalCells = new NativeList<VoronoiCell>(Allocator.Persistent);
            NativeList<VoronoiEdge> finalEdges = new NativeList<VoronoiEdge>(Allocator.Persistent);

            // Переменные для загрузки кэша геометрии
            NativeArray<float2> cachedVerts = default;
            NativeArray<int> cachedCounts = default;
            NativeArray<VoronoiEdge> cachedEdges = default;

            bool loadedFromCache = false;

            // --- ЭТАП 1: ПОПЫТКА ЗАГРУЗКИ ИЗ КЭША ---
            if (m_Settings.UseCache && MapCacheUtils.LoadLevel(m_Settings.Seed, level, 
                out sites, out meta, out tectonicData, out climateData, out hydrologyData, out biomeData,
                out cachedVerts, out cachedCounts, out cachedEdges))
            {
                Debug.Log($"[MapGen] L{level}: Loaded from Cache.");
                loadedFromCache = true;

                // Если загрузили из кэша, нужно восстановить структуру клеток и ребер из плоских массивов
                var tempVertsList = new NativeList<float2>(cachedVerts.Length, Allocator.Temp);
                tempVertsList.AddRange(cachedVerts);

                var tempCountsList = new NativeList<int>(cachedCounts.Length, Allocator.Temp);
                tempCountsList.AddRange(cachedCounts);

                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, 
                    new NativeList<TriangleIndices>(0, Allocator.Temp), // Треугольники не нужны при загрузке из кэша
                    tempVertsList, 
                    tempCountsList, 
                    ref finalCells, 
                    ref finalEdges);
                
                // В кэше сохранены точные обрезанные ребра, используем их
                finalEdges.Clear();
                finalEdges.AddRange(cachedEdges);

                cachedVerts.Dispose(); 
                cachedCounts.Dispose(); 
                cachedEdges.Dispose();
            }
            
            // --- ЭТАП 2: ПРОЦЕДУРНАЯ ГЕНЕРАЦИЯ (Если нет кэша) ---
            if (!loadedFromCache)
            {
                // Получаем данные предыдущего уровня из истории
                m_History.TryGetPreviousLevel(level, 
                    out var pCells, out var pSites, out var pMeta, 
                    out var pHydro, out var pTect, out var pClim);

                // 2.1 Генерация сайтов (точек)
                // Исправлен вызов: передаем только нужные массивы (pSites не нужен)
                var (rawSites, rawMeta) = SiteGenerator.Generate(
                    m_Settings, 
                    levelSettings, 
                    level, 
                    pCells, 
                    pMeta, 
                    pHydro, 
                    pTect, 
                    pClim
                );

                // Фильтруем "призраков" (точки за границами)
                (sites, meta) = MapProcessingHelpers.FilterValidSites(rawSites, rawMeta, Allocator.Persistent);
                rawSites.Dispose(); 
                rawMeta.Dispose();

                // 2.2 Триангуляция и Диаграмма Вороного
                var tri = new NativeList<TriangleIndices>(Allocator.TempJob);
                var cv = new NativeList<float2>(Allocator.TempJob); 
                var cc = new NativeList<int>(Allocator.TempJob);

                for (int iter = 0; iter <= levelSettings.RelaxationIterations; iter++)
                {
                    bool isLast = (iter == levelSettings.RelaxationIterations);
                    DelaunayBuilder.Triangulate(sites, ref tri, m_Settings.MapSize);
                    
                    cv.Clear(); 
                    cc.Clear();
                    VoronoiBuilder.BuildCells(sites, tri, m_Settings.MapSize, ref cv, ref cc);
                    
                    if (!isLast) ApplyLloydRelaxation(sites, cv, cc, m_Settings.MapSize);
                }

                // 2.3 Генерация данных симуляции (Геология, Климат)
                int count = sites.Length;
                tectonicData = new NativeArray<TectonicPlateData>(count, Allocator.Persistent);
                climateData = new NativeArray<ClimateData>(count, Allocator.Persistent);
                biomeData = new NativeArray<BiomeData>(count, Allocator.Persistent);
                hydrologyData = new NativeArray<HydrologyData>(count, Allocator.Persistent);

                // Пустые массивы для L0, если нет родителей (Unity Jobs не любят null)
                var dTm = new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
                var dCm = new NativeArray<ClimateData>(0, Allocator.TempJob);

                new TectonicGenerationJob {
                    Seed = m_Settings.Seed + level * 77, 
                    MapSize = m_Settings.MapSize, 
                    Level = level,
                    Sites = sites, 
                    SiteMeta = meta, 
                    ParentTectonics = (level == 0) ? dTm : pTect, 
                    TectonicData = tectonicData
                }.Schedule(count, 64).Complete();

                new ClimateGenerationJob {
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
                
                dTm.Dispose(); 
                dCm.Dispose();

                // 2.4 Построение Графа и Гидрология
                NativeList<VoronoiEdge> tempEdges = MapProcessingHelpers.ExtractEdgesFromDelaunay(tri, Allocator.TempJob);
                float maxDistSq = MapProcessingHelpers.CalculateAdaptiveGraphLimit(tri, sites, tectonicData, level);

                var neighborsMap = new NativeParallelMultiHashMap<int, NeighborInfo>(tempEdges.Length * 2, Allocator.TempJob);
                new BuildNeighborGraphJob { 
                    Edges = tempEdges, 
                    SitePositions = sites, 
                    Tectonics = tectonicData, 
                    MaxConnectionDistSq = maxDistSq, 
                    NeighborsMap = neighborsMap 
                }.Schedule().Complete();

                var tempCellsForHydro = new NativeArray<VoronoiCell>(count, Allocator.TempJob);
                for(int i = 0; i < count; i++) tempCellsForHydro[i] = new VoronoiCell { SiteIndex = i, Centroid = sites[i] };

                new CalculateHydrologyJob {
                    Cells = tempCellsForHydro, 
                    Tectonics = tectonicData, 
                    Climate = climateData,
                    NeighborsMap = neighborsMap, 
                    Hydrology = hydrologyData
                }.Schedule().Complete();

                tempCellsForHydro.Dispose(); 
                neighborsMap.Dispose(); 
                tempEdges.Dispose();

                // 2.5 Финальная сборка геометрии (Cells + Edges)
                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, tri, cv, cc, ref finalCells, ref finalEdges);

                if (m_Settings.UseCache)
                {
                    MapCacheUtils.SaveLevel(m_Settings.Seed, level, sites, meta, tectonicData, climateData, hydrologyData, biomeData, cv, cc, finalEdges);
                }
                
                tri.Dispose(); 
                cv.Dispose(); 
                cc.Dispose();
            }

            // --- ЭТАП 3: СОЗДАНИЕ ECS СУЩНОСТЕЙ ---
            EntityCreationPipeline.CreateEntities(
                EntityManager, level, levelSettings, m_Settings.MapSize,
                sites, meta, tectonicData, climateData, biomeData, hydrologyData, finalCells, finalEdges
            );

            // --- ЭТАП 4: СОХРАНЕНИЕ В ИСТОРИЮ (Для детей) ---
            // MapHistoryData создает глубокие копии массивов, поэтому мы можем спокойно удалить свои локальные.
            m_History.StoreLevel(level, sites, meta, finalCells, tectonicData, climateData, hydrologyData);

            // --- ЭТАП 5: ОЧИСТКА ПАМЯТИ ---
            // Обязательно удаляем массивы, созданные с Allocator.Persistent в начале метода,
            // так как копии уже лежат в m_History.
            if (sites.IsCreated) sites.Dispose();
            if (meta.IsCreated) meta.Dispose();
            if (tectonicData.IsCreated) tectonicData.Dispose();
            if (climateData.IsCreated) climateData.Dispose();
            if (biomeData.IsCreated) biomeData.Dispose();
            if (hydrologyData.IsCreated) hydrologyData.Dispose();
            
            if (finalCells.IsCreated) finalCells.Dispose();
            if (finalEdges.IsCreated) finalEdges.Dispose();
        }

        private void ApplyLloydRelaxation(NativeArray<float2> sites, NativeList<float2> verts, NativeList<int> counts, float2 mapSize)
        {
            int offset = 0;
            for (int i = 0; i < sites.Length; i++) {
                int vCount = counts[i];
                if (vCount > 0) {
                    float2 c = float2.zero; float area = 0.0f;
                    for (int k = 0; k < vCount; k++) {
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