using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.MapGeneration.Jobs;
using VoronoiMapGen.Features.Utils;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Features.MapGeneration.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private int m_CurrentLevel;
        private MapHistoryData m_History;
        private bool m_IsComplete;
        private bool m_IsInitialized;
        private NativeArray<LevelSettings> m_LevelSettings;
        private MapSettings m_Settings;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
        }

        protected override void OnDestroy()
        {
            // --- CLEANUP SAFETY FIX ---
            // 1. Обязательно ждем завершения всех запланированных этой системой джоб
            this.Dependency.Complete();

            // 2. Безопасное освобождение NativeArray
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();
            
            // 3. Очистка истории
            if (m_History != null) m_History.Dispose();
        }

        // Остальной код остается тем же (ProcessSingleLevel, Initialize и т.д.)
        // Дублировать огромный файл смысла нет, если он у вас уже был из предыдущего шага "Пункт 2".
        // Если вы затирали его, я приведу сокращенную версию Update (она не менялась с пункта 2):

        protected override void OnUpdate()
        {
            if (m_IsComplete) return;
            if (!m_IsInitialized)
            {
                Initialize();
                return;
            }

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

            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
            {
                Enabled = false;
                return;
            }

            m_Settings = SystemAPI.GetSingleton<MapSettings>();
            var buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

            var count = m_LevelSettings.Length;
            m_History = new MapHistoryData(count);

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialized. Generating {count} levels...");
        }

        private void ProcessSingleLevel(int level)
        {
           // --- ИСПОЛЬЗУЙТЕ ЛОГИКУ ИЗ ШАГА 2 (Она корректна и включает передачу Ownership) ---
           // Единственное отличие - правильный OnDestroy в начале этого ответа.
           // Если вы скопировали Шаг 2 (Оптимизация памяти), просто замените OnDestroy.
           
           // Для полной ясности дублирую вызов (полная копия из Шага 2):
           
            Debug.Log($"Processing L{level}");
            var levelSettings = m_LevelSettings[level];
            
            NativeArray<float2> sites = default;
            NativeArray<VoronoiSite> meta = default;
            NativeArray<TectonicPlateData> tectonicData = default;
            NativeArray<ClimateData> climateData = default;
            NativeArray<HydrologyData> hydrologyData = default;
            NativeArray<BiomeData> biomeData = default;

            var cellsList = new NativeList<VoronoiCell>(Allocator.Persistent);
            var edgesList = new NativeList<VoronoiEdge>(Allocator.Persistent);

            NativeArray<float2> cachedVerts = default;
            NativeArray<int> cachedCounts = default;
            NativeArray<VoronoiEdge> cachedEdges = default;
            var loadedFromCache = false;

            if (m_Settings.UseCache && MapCacheUtils.LoadLevel(m_Settings.Seed, level,
                    out sites, out meta, out tectonicData, out climateData, out hydrologyData, out biomeData,
                    out cachedVerts, out cachedCounts, out cachedEdges))
            {
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

            if (!loadedFromCache)
            {
                NativeArray<VoronoiCell> pCells = default;
                NativeArray<VoronoiSite> pMeta = default;
                NativeArray<HydrologyData> pHydro = default;
                NativeArray<TectonicPlateData> pTect = default;
                NativeArray<ClimateData> pClim = default;

                if (m_History.TryGetLevel(level - 1, out var parentData))
                {
                    pCells = parentData.Cells;
                    pMeta = parentData.Meta;
                    pHydro = parentData.Hydrology;
                    pTect = parentData.Tectonics;
                    pClim = parentData.Climate;
                }

                var (rawSites, rawMeta) = SiteGenerator.Generate(
                    m_Settings, levelSettings, level, pCells, pMeta, pHydro, pTect, pClim);

                (sites, meta) = MapProcessingHelpers.FilterValidSites(rawSites, rawMeta, Allocator.Persistent);
                rawSites.Dispose(); rawMeta.Dispose();

                var tri = new NativeList<TriangleIndices>(Allocator.TempJob);
                var cv = new NativeList<float2>(Allocator.TempJob);
                var cc = new NativeList<int>(Allocator.TempJob);

                for (var iter = 0; iter <= levelSettings.RelaxationIterations; iter++)
                {
                    var isLast = iter == levelSettings.RelaxationIterations;
                    DelaunayBuilder.Triangulate(sites, ref tri, m_Settings.MapSize);
                    cv.Clear(); cc.Clear();
                    VoronoiBuilder.BuildCells(sites, tri, m_Settings.MapSize, ref cv, ref cc);
                    if (!isLast) ApplyLloydRelaxation(sites, cv, cc, m_Settings.MapSize);
                }

                var count = sites.Length;
                tectonicData = new NativeArray<TectonicPlateData>(count, Allocator.Persistent);
                climateData = new NativeArray<ClimateData>(count, Allocator.Persistent);
                biomeData = new NativeArray<BiomeData>(count, Allocator.Persistent);
                hydrologyData = new NativeArray<HydrologyData>(count, Allocator.Persistent);

                var dTm = new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
                var dCm = new NativeArray<ClimateData>(0, Allocator.TempJob);

                new TectonicGenerationJob
                {
                    Seed = m_Settings.Seed + level * 77,
                    MapSize = m_Settings.MapSize,
                    Level = level,
                    Sites = sites,
                    SiteMeta = meta,
                    ParentTectonics = level == 0 ? dTm : pTect,
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
                    ParentClimate = level == 0 ? dCm : pClim,
                    Climate = climateData,
                    Biomes = biomeData
                }.Schedule(count, 64).Complete();

                dTm.Dispose(); dCm.Dispose();

                var tempEdges = MapProcessingHelpers.ExtractEdgesFromDelaunay(tri, Allocator.TempJob);
                var maxDistSq = MapProcessingHelpers.CalculateAdaptiveGraphLimit(tri, sites, tectonicData, level);

                var neighborsMap = new NativeParallelMultiHashMap<int, NeighborInfo>(tempEdges.Length * 2, Allocator.TempJob);
                new BuildNeighborGraphJob { Edges = tempEdges, SitePositions = sites, Tectonics = tectonicData, MaxConnectionDistSq = maxDistSq, NeighborsMap = neighborsMap }.Schedule().Complete();

                var tempCellsForHydro = new NativeArray<VoronoiCell>(count, Allocator.TempJob);
                for (var i = 0; i < count; i++) tempCellsForHydro[i] = new VoronoiCell { SiteIndex = i, Centroid = sites[i] };

                new CalculateHydrologyJob { Cells = tempCellsForHydro, Tectonics = tectonicData, Climate = climateData, NeighborsMap = neighborsMap, Hydrology = hydrologyData }.Schedule().Complete();

                tempCellsForHydro.Dispose(); neighborsMap.Dispose(); tempEdges.Dispose();

                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, tri, cv, cc, ref cellsList, ref edgesList);

                if (m_Settings.UseCache)
                    MapCacheUtils.SaveLevel(m_Settings.Seed, level, sites, meta, tectonicData, climateData, hydrologyData, biomeData, cv, cc, edgesList);

                tri.Dispose(); cv.Dispose(); cc.Dispose();
            }

            var finalCellsArray = new NativeArray<VoronoiCell>(cellsList.Length, Allocator.Persistent);
            finalCellsArray.CopyFrom(cellsList.AsArray());
            
            var finalEdgesArray = new NativeArray<VoronoiEdge>(edgesList.Length, Allocator.Persistent);
            finalEdgesArray.CopyFrom(edgesList.AsArray());

            var currentLevelData = new MapLevelData
            {
                LevelIndex = level,
                Sites = sites,
                Meta = meta,
                Cells = finalCellsArray,
                Edges = finalEdgesArray,
                Tectonics = tectonicData,
                Climate = climateData,
                Hydrology = hydrologyData,
                Biomes = biomeData
            };

            EntityCreationPipeline.CreateEntities(EntityManager, currentLevelData, levelSettings, m_Settings.MapSize, edgesList);

            m_History.StoreLevel(currentLevelData);

            cellsList.Dispose(); 
            edgesList.Dispose();
        }

        private void ApplyLloydRelaxation(NativeArray<float2> sites, NativeList<float2> verts, NativeList<int> counts, float2 mapSize)
        {
            var offset = 0;
            for (var i = 0; i < sites.Length; i++)
            {
                var vCount = counts[i];
                if (vCount > 0)
                {
                    var c = float2.zero;
                    var area = 0.0f;
                    for (var k = 0; k < vCount; k++)
                    {
                        var curr = verts[offset + k];
                        var next = verts[offset + (k + 1) % vCount];
                        var a = curr.x * next.y - next.x * curr.y;
                        area += a;
                        c += (curr + next) * a;
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
            Enabled = false;
        }
    }
}