using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs; 
using VoronoiMapGen.Utils; 

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // --- Persistent Storage (Данные между уровнями) ---
        // Храним данные предыдущих уровней, чтобы дети (L1) могли читать родителей (L0)
        private NativeArray<VoronoiCell>[] m_LevelCells;
        private NativeArray<float2>[] m_LevelSites;
        private NativeArray<VoronoiSite>[] m_LevelMeta;
        
        // Хранение данных симуляции для передачи детям
        private NativeArray<HydrologyData>[] m_LevelHydrology;
        private NativeArray<TectonicPlateData>[] m_LevelTectonics;
        private NativeArray<ClimateData>[] m_LevelClimate;

        private int m_CurrentLevel = 0;
        private bool m_IsInitialized = false;
        private bool m_IsComplete = false;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
        }

        protected override void OnDestroy()
        {
            // Полная очистка памяти при остановке
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();
            
            if (m_LevelCells != null)
            {
                for (int i = 0; i < m_LevelCells.Length; i++)
                {
                    if (m_LevelCells[i].IsCreated) m_LevelCells[i].Dispose();
                    if (m_LevelSites[i].IsCreated) m_LevelSites[i].Dispose();
                    if (m_LevelMeta[i].IsCreated) m_LevelMeta[i].Dispose();
                    
                    if (m_LevelHydrology[i].IsCreated) m_LevelHydrology[i].Dispose();
                    if (m_LevelTectonics[i].IsCreated) m_LevelTectonics[i].Dispose();
                    if (m_LevelClimate[i].IsCreated) m_LevelClimate[i].Dispose();
                }
            }
        }

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

            int count = m_LevelSettings.Length;
            
            // Инициализация массивов массивов
            m_LevelCells = new NativeArray<VoronoiCell>[count];
            m_LevelSites = new NativeArray<float2>[count];
            m_LevelMeta = new NativeArray<VoronoiSite>[count];
            
            m_LevelHydrology = new NativeArray<HydrologyData>[count];
            m_LevelTectonics = new NativeArray<TectonicPlateData>[count];
            m_LevelClimate = new NativeArray<ClimateData>[count];

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialized. Total Levels: {count}");
        }

        private void ProcessSingleLevel(int level)
        {
            Debug.Log($"--- Processing Level {level} ---");
            LevelSettings levelSettings = m_LevelSettings[level];

            // 1. ПОДГОТОВКА ДАННЫХ РОДИТЕЛЯ
            // Берем данные предыдущего уровня, если он есть
            NativeArray<VoronoiCell> pCells = (level > 0) ? m_LevelCells[level - 1] : default;
            NativeArray<float2> pSites = (level > 0) ? m_LevelSites[level - 1] : default;
            NativeArray<VoronoiSite> pMeta = (level > 0) ? m_LevelMeta[level - 1] : default;
            
            // Данные симуляции родителя (для оценки Suitability при генерации детей)
            NativeArray<HydrologyData> pHydro = (level > 0) ? m_LevelHydrology[level - 1] : default;
            NativeArray<TectonicPlateData> pTect = (level > 0) ? m_LevelTectonics[level - 1] : default;
            NativeArray<ClimateData> pClim = (level > 0) ? m_LevelClimate[level - 1] : default;

            // 2. ГЕНЕРАЦИЯ СЫРЫХ ТОЧЕК (SEEDING)
            var (rawSites, rawMeta) = SiteGenerator.Generate(
                m_Settings, m_LevelSettings, levelSettings, 
                level, pCells, pSites, pMeta,
                pHydro, pTect, pClim
            );

            // Фильтрация пустых слотов (-1)
            int validCount = 0;
            for(int i=0; i<rawSites.Length; i++) if (rawMeta[i].Value > -0.5f) validCount++;
            
            var sites = new NativeArray<float2>(validCount, Allocator.Persistent);
            var meta = new NativeArray<VoronoiSite>(validCount, Allocator.Persistent);
            
            int idx = 0;
            for(int i=0; i<rawSites.Length; i++) {
                if (rawMeta[i].Value > -0.5f) {
                    sites[idx] = rawSites[i];
                    meta[idx] = rawMeta[i];
                    // Обновляем индекс в структуре
                    var m = meta[idx]; m.Index = idx; meta[idx] = m;
                    idx++;
                }
            }
            rawSites.Dispose();
            rawMeta.Dispose();

            // 3. ГЕОМЕТРИЯ И РЕЛАКСАЦИЯ (RELAXATION LOOP)
            int iterations = levelSettings.RelaxationIterations;
            
            NativeList<TriangleIndices> triangles = new NativeList<TriangleIndices>(Allocator.TempJob);
            NativeList<float2> cellVertices = new NativeList<float2>(Allocator.TempJob); 
            NativeList<int> cellCounts = new NativeList<int>(Allocator.TempJob);

            for (int iter = 0; iter <= iterations; iter++)
            {
                bool isLast = (iter == iterations);
                DelaunayBuilder.Triangulate(sites, ref triangles, m_Settings.MapSize);
                cellVertices.Clear(); cellCounts.Clear();
                VoronoiBuilder.BuildCells(sites, triangles, m_Settings.MapSize, ref cellVertices, ref cellCounts);

                if (!isLast) ApplyLloydRelaxation(sites, cellVertices, cellCounts, m_Settings.MapSize);
            }

            // 4. СБОРКА ФИНАЛЬНОЙ ГЕОМЕТРИИ (Нужны ребра для симуляции)
            var finalCells = new NativeList<VoronoiCell>(sites.Length, Allocator.TempJob);
            var finalEdges = new NativeList<VoronoiEdge>(triangles.Length * 3, Allocator.TempJob);

            AssembleFinalGeometry(level, sites, meta, cellVertices, cellCounts, ref finalCells, ref finalEdges);

            triangles.Dispose();
            cellVertices.Dispose();
            cellCounts.Dispose();

            // 5. СИМУЛЯЦИЯ МИРА (SIMULATION PIPELINE)
            var tectonicData = new NativeArray<TectonicPlateData>(validCount, Allocator.TempJob);
            var climateData = new NativeArray<ClimateData>(validCount, Allocator.TempJob);
            var biomeData = new NativeArray<BiomeData>(validCount, Allocator.TempJob);
            var hydrologyData = new NativeArray<HydrologyData>(validCount, Allocator.TempJob);

            // ШАГ A: Тектоника (Форма острова и высоты)
            new TectonicGenerationJob
            {
                Seed = m_Settings.Seed + level * 777,
                MapSize = m_Settings.MapSize,
                Sites = sites,
                TectonicData = tectonicData
            }.Schedule(validCount, 64).Complete();

            // ШАГ B: Взаимодействие Плит (Хребты и Берега) - Только для L0
            if (level == 0)
            {
                new TectonicInteractionJob
                {
                    Edges = finalEdges,
                    TectonicData = tectonicData
                }.Run(); // Run синхронно, т.к. меняем данные соседей
            }

            // ШАГ C: Климат (Температура и Базовая Влажность от Ветра)
            new ClimateGenerationJob
            {
                Seed = m_Settings.Seed + level * 888,
                MapSize = m_Settings.MapSize,
                Sites = sites,
                Tectonics = tectonicData,
                Hydrology = hydrologyData, // Пока пустой, но нужен структуре
                Climate = climateData,
                Biomes = biomeData
            }.Schedule(validCount, 64).Complete();

            // ШАГ D: Гидрология (Реки и Озера)
            var neighborsMap = new NativeParallelMultiHashMap<int, int>(finalEdges.Length * 2, Allocator.TempJob);
            new BuildNeighborGraphJob 
            { 
                Edges = finalEdges, 
                SiteCount = validCount, 
                NeighborsMap = neighborsMap 
            }.Schedule().Complete();

            new CalculateHydrologyJob
            {
                Cells = finalCells.AsArray(),
                Tectonics = tectonicData,
                Climate = climateData,
                NeighborsMap = neighborsMap,
                Hydrology = hydrologyData
            }.Schedule().Complete();

            neighborsMap.Dispose();

            // ШАГ E: Уточнение Биомов (Оазисы и Зеленые коридоры)
            // Реки появились на шаге D, теперь они меняют биомы (пустыня -> оазис)
            new ApplyRiverBiomesJob
            {
                Hydrology = hydrologyData,
                Biomes = biomeData
            }.Schedule(validCount, 64).Complete();


            // 6. СОЗДАНИЕ СУЩНОСТЕЙ (ENTITY CREATION)
            EntityCreationPipeline.CreateEntities(
                EntityManager, level, levelSettings, m_Settings.MapSize,
                sites, meta,
                tectonicData, climateData, biomeData, hydrologyData,
                finalCells, finalEdges
            );

            // 7. СОХРАНЕНИЕ ДЛЯ СЛЕДУЮЩЕГО УРОВНЯ (PERSISTENCE)
            m_LevelSites[level] = sites;
            m_LevelMeta[level] = meta;
            m_LevelCells[level] = new NativeArray<VoronoiCell>(finalCells.Length, Allocator.Persistent);
            m_LevelCells[level].CopyFrom(finalCells.AsArray());

            m_LevelHydrology[level] = new NativeArray<HydrologyData>(hydrologyData.Length, Allocator.Persistent);
            m_LevelHydrology[level].CopyFrom(hydrologyData);

            m_LevelTectonics[level] = new NativeArray<TectonicPlateData>(tectonicData.Length, Allocator.Persistent);
            m_LevelTectonics[level].CopyFrom(tectonicData);

            m_LevelClimate[level] = new NativeArray<ClimateData>(climateData.Length, Allocator.Persistent);
            m_LevelClimate[level].CopyFrom(climateData);

            // Очистка локальных временных данных (TempJob)
            finalCells.Dispose();
            finalEdges.Dispose();
            tectonicData.Dispose();
            climateData.Dispose();
            biomeData.Dispose();
            hydrologyData.Dispose();
        }

        // --- Helpers ---

        private void ApplyLloydRelaxation(NativeArray<float2> sites, NativeList<float2> verts, NativeList<int> counts, float2 mapSize)
        {
            int offset = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                int vCount = counts[i];
                if (vCount > 0)
                {
                    float2 centroid = float2.zero;
                    float signedArea = 0.0f;
                    for (int k = 0; k < vCount; k++)
                    {
                        float2 curr = verts[offset + k];
                        float2 next = verts[offset + (k + 1) % vCount];
                        float a = curr.x * next.y - next.x * curr.y;
                        signedArea += a;
                        centroid += (curr + next) * a;
                    }
                    if (math.abs(signedArea) > 1e-6f) {
                        signedArea *= 3.0f;
                        centroid /= signedArea;
                        sites[i] = math.clamp(centroid, float2.zero, mapSize);
                    }
                }
                offset += vCount;
            }
        }

        private void AssembleFinalGeometry(
            int level, 
            NativeArray<float2> sites, 
            NativeArray<VoronoiSite> meta, 
            NativeList<float2> verts, 
            NativeList<int> counts, 
            ref NativeList<VoronoiCell> outCells, 
            ref NativeList<VoronoiEdge> outEdges)
        {
            int vertOffset = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                int vCount = counts[i];
                float2 centroid = sites[i];

                outCells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = centroid,
                    Level = level,
                    ParentRegionIndex = meta[i].ParentIndex,
                    ParentEntity = Entity.Null
                });
                
                for (int k = 0; k < vCount; k++)
                {
                    outEdges.Add(new VoronoiEdge
                    {
                        SiteA = i, SiteB = -1, 
                        VertexA = verts[vertOffset + k],
                        VertexB = verts[vertOffset + (k + 1) % vCount],
                        Level = level
                    });
                }
                vertOffset += vCount;
            }
            
            // Логические ребра для графа
            NativeList<TriangleIndices> tris = new NativeList<TriangleIndices>(Allocator.Temp);
            DelaunayBuilder.Triangulate(sites, ref tris, new float2(10000,10000));
            
            for(int i=0; i<tris.Length; i++)
            {
                var t = tris[i];
                outEdges.Add(new VoronoiEdge { SiteA = t.A, SiteB = t.B, Level = level });
                outEdges.Add(new VoronoiEdge { SiteA = t.B, SiteB = t.C, Level = level });
                outEdges.Add(new VoronoiEdge { SiteA = t.C, SiteB = t.A, Level = level });
            }
            tris.Dispose();
        }

        private void CompleteGeneration()
        {
            m_IsComplete = true;
            var sEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            EntityManager.AddComponent<MapGeneratedTag>(sEntity);
            EntityManager.RemoveComponent<MapGenerationInProgress>(sEntity);
            
            Debug.Log("[MapGen] Generation Complete!");
            Enabled = false; 
        }
    }
}