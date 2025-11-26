using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs; // Наши джобы
using VoronoiMapGen.Utils; // Наши утилиты (Delaunay, Clipper)

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // Хранилище данных между уровнями (для передачи родительских данных детям)
        private NativeArray<VoronoiCell>[] m_LevelCells;
        private NativeArray<float2>[] m_LevelSites;
        private NativeArray<VoronoiSite>[] m_LevelMeta;

        private int m_CurrentLevel = 0;
        private bool m_IsInitialized = false;
        private bool m_IsComplete = false;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
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
            m_LevelCells = new NativeArray<VoronoiCell>[count];
            m_LevelSites = new NativeArray<float2>[count];
            m_LevelMeta = new NativeArray<VoronoiSite>[count];

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialized. Total Levels: {count}");
        }

        private void ProcessSingleLevel(int level)
        {
            Debug.Log($"--- Processing Level {level} ---");

            // 1. Получаем данные родителя (если уровень > 0)
            NativeArray<VoronoiCell> pCells = (level > 0) ? m_LevelCells[level - 1] : default;
            NativeArray<float2> pSites = (level > 0) ? m_LevelSites[level - 1] : default;
            NativeArray<VoronoiSite> pMeta = (level > 0) ? m_LevelMeta[level - 1] : default;

            // 2. Генерируем сырые точки (Sites)
            var (rawSites, rawMeta) = SiteGenerator.Generate(
                m_Settings, m_LevelSettings, m_LevelSettings[level], 
                level, pCells, pSites, pMeta
            );

            // 3. Фильтрация: Убираем пустые слоты (-1), чтобы работать только с реальными данными
            int validCount = 0;
            for(int i=0; i<rawSites.Length; i++) {
                if (rawMeta[i].Value > -0.5f) validCount++;
            }
            
            var sites = new NativeArray<float2>(validCount, Allocator.Persistent);
            var meta = new NativeArray<VoronoiSite>(validCount, Allocator.Persistent);
            
            int idx = 0;
            for(int i=0; i<rawSites.Length; i++) {
                if (rawMeta[i].Value > -0.5f) {
                    sites[idx] = rawSites[i];
                    meta[idx] = rawMeta[i];
                    // Обновляем индекс, так как массив сжался
                    var m = meta[idx]; m.Index = idx; meta[idx] = m;
                    idx++;
                }
            }
            rawSites.Dispose();
            rawMeta.Dispose();

            // 4. ГЕНЕРАЦИЯ ДАННЫХ МИРА (Тектоника, Климат, Биомы)
            // Считаем это ДО релаксации, чтобы данные привязались к "душе" ячейки.
            // При релаксации точка сдвинется, но она унесет свои свойства (океан/лес) с собой.
            
            var tectonicData = new NativeArray<TectonicPlateData>(validCount, Allocator.TempJob);
            var climateData = new NativeArray<ClimateData>(validCount, Allocator.TempJob);
            var biomeData = new NativeArray<BiomeData>(validCount, Allocator.TempJob);

            // А. Тектоника (Океан vs Суша)
            new TectonicGenerationJob
            {
                Seed = m_Settings.Seed + level * 777,
                MapSize = m_Settings.MapSize,
                Sites = sites,
                TectonicData = tectonicData
            }.Schedule(validCount, 64).Complete();

            // Б. Климат и Биомы
            new ClimateGenerationJob
            {
                Seed = m_Settings.Seed + level * 888,
                MapSize = m_Settings.MapSize,
                Sites = sites,
                Tectonics = tectonicData,
                Climate = climateData,
                Biomes = biomeData
            }.Schedule(validCount, 64).Complete();


            // 5. РЕЛАКСАЦИЯ ЛЛОЙДА (Геометрия)
            // Делаем ячейки красивыми и ровными
            int iterations = m_LevelSettings[level].RelaxationIterations;
            
            var triangles = new NativeList<TriangleIndices>(Allocator.TempJob);
            var cellVertices = new NativeList<float2>(Allocator.TempJob); 
            var cellCounts = new NativeList<int>(Allocator.TempJob);

            for (int iter = 0; iter <= iterations; iter++)
            {
                bool isLast = (iter == iterations);

                // Триангуляция + Построение ячеек (с обрезкой по квадрату карты)
                DelaunayBuilder.Triangulate(sites, ref triangles, m_Settings.MapSize);
                cellVertices.Clear();
                cellCounts.Clear();
                VoronoiBuilder.BuildCells(sites, triangles, m_Settings.MapSize, ref cellVertices, ref cellCounts);

                // Двигаем сайты к центроидам (только если не последний шаг)
                if (!isLast)
                {
                    int offset = 0;
                    for (int i = 0; i < sites.Length; i++)
                    {
                        int vCount = cellCounts[i];
                        if (vCount > 0)
                        {
                            float2 centroid = float2.zero;
                            float signedArea = 0.0f;
                            for (int k = 0; k < vCount; k++)
                            {
                                float2 curr = cellVertices[offset + k];
                                float2 next = cellVertices[offset + (k + 1) % vCount];
                                float a = curr.x * next.y - next.x * curr.y;
                                signedArea += a;
                                centroid += (curr + next) * a;
                            }
                            if (math.abs(signedArea) > 1e-6f) {
                                signedArea *= 3.0f;
                                centroid /= signedArea;
                                sites[i] = math.clamp(centroid, float2.zero, m_Settings.MapSize);
                            }
                        }
                        offset += vCount;
                    }
                }
            }

            // 6. Подготовка данных для ECS
            var finalCells = new NativeList<VoronoiCell>(sites.Length, Allocator.TempJob);
            var finalEdges = new NativeList<VoronoiEdge>(triangles.Length * 3, Allocator.TempJob);

            int vertOffset = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                int vCount = cellCounts[i];
                float2 centroid = sites[i]; // После релаксации сайт и есть центроид (почти)

                finalCells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = centroid,
                    Level = level,
                    ParentRegionIndex = meta[i].ParentIndex,
                    ParentEntity = Entity.Null // Заполнится внутри Pipeline
                });
                
                // Генерируем ребра для визуализации (Gizmos)
                for (int k = 0; k < vCount; k++)
                {
                    finalEdges.Add(new VoronoiEdge
                    {
                        SiteA = i, SiteB = -1, // SiteB нам не важен для отрисовки контура
                        VertexA = cellVertices[vertOffset + k],
                        VertexB = cellVertices[vertOffset + (k + 1) % vCount],
                        Level = level
                    });
                }
                vertOffset += vCount;
            }

            // 7. СОЗДАНИЕ СУЩНОСТЕЙ
            EntityCreationPipeline.CreateEntities(
                EntityManager, level, m_LevelSettings[level], m_Settings.MapSize,
                sites, meta,
                tectonicData, climateData, biomeData, // Передаем наши новые данные!
                finalCells, finalEdges
            );

            // 8. Сохранение для следующего уровня
            m_LevelSites[level] = sites;
            m_LevelMeta[level] = meta;
            m_LevelCells[level] = new NativeArray<VoronoiCell>(finalCells.Length, Allocator.Persistent);
            m_LevelCells[level].CopyFrom(finalCells.AsArray());

            // Очистка временной памяти
            triangles.Dispose();
            cellVertices.Dispose();
            cellCounts.Dispose();
            finalCells.Dispose();
            finalEdges.Dispose();
            tectonicData.Dispose();
            climateData.Dispose();
            biomeData.Dispose();
        }

        private void CompleteGeneration()
        {
            m_IsComplete = true;
            var sEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            EntityManager.AddComponent<MapGeneratedTag>(sEntity);
            EntityManager.RemoveComponent<MapGenerationInProgress>(sEntity);
            
            // Очистка Persistent
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();
            for(int i=0; i<m_LevelCells.Length; i++)
            {
                if (m_LevelCells[i].IsCreated) m_LevelCells[i].Dispose();
                if (m_LevelSites[i].IsCreated) m_LevelSites[i].Dispose();
                if (m_LevelMeta[i].IsCreated) m_LevelMeta[i].Dispose();
            }
            
            Debug.Log("[MapGen] Generation Complete!");
            Enabled = false;
        }
    }
}