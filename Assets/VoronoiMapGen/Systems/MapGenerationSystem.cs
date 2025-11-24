using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using Debug = UnityEngine.Debug;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        // --- Data ---
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // Persistent data storage for all levels
        private NativeArray<VoronoiCell>[] m_LevelCells;
        private NativeArray<float2>[] m_LevelSites;
        private NativeArray<VoronoiSite>[] m_LevelSiteMetadata;

        // --- State ---
        private int m_CurrentLevel = 0;
        private int m_CurrentStage = 0; // 0: Levels, 1: Heights, 2: Biomes, 3: Report
        private bool m_IsInitialized = false;
        private bool m_IsComplete = false;

        // --- Timers ---
        private Stopwatch m_OverallSW;
        private Stopwatch m_LevelSW;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
            m_OverallSW = new Stopwatch();
            m_LevelSW = new Stopwatch();
        }

        protected override void OnUpdate()
        {
            if (m_IsComplete) return;

            // 1. Инициализация
            if (!m_IsInitialized)
            {
                Initialize();
                return;
            }

            // 2. Обновление прогресса (UI)
            UpdateProgressEntity();

            // 3. Основной State Machine
            switch (m_CurrentStage)
            {
                case 0: // Генерация уровней Вороного
                    if (m_CurrentLevel < m_LevelSettings.Length)
                    {
                        ProcessSingleLevel(m_CurrentLevel);
                        m_CurrentLevel++;
                    }
                    else
                    {
                        Debug.Log($"[Stage 0] All levels generated.");
                        m_CurrentStage++;
                    }
                    break;

                case 1: // Генерация высот
                    Debug.Log("[Stage 1] Generating Heights...");
                    HeightGenerationPipeline.GenerateHeights(EntityManager, m_Settings, m_LevelSettings);
                    m_CurrentStage++;
                    break;

                case 2: // Генерация биомов
                    Debug.Log("[Stage 2] Generating Biomes...");
                    BiomeGenerationPipeline.GenerateBiomes(EntityManager, m_Settings);
                    m_CurrentStage++;
                    break;

                case 3: // Отчет и завершение
                    Debug.Log("[Stage 3] Generating Report...");
                    MapReportGenerator.Report(EntityManager, m_Settings, m_LevelSettings);
                    CompleteGeneration();
                    break;
            }
        }

        private void Initialize()
        {
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            
            // Если карта уже сгенерирована, отключаем систему
            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
            {
                Enabled = false;
                return;
            }

            m_Settings = SystemAPI.GetSingleton<MapSettings>();
            var buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

            // Аллокация массивов для хранения данных всех уровней
            int count = m_LevelSettings.Length;
            m_LevelCells = new NativeArray<VoronoiCell>[count];
            m_LevelSites = new NativeArray<float2>[count];
            m_LevelSiteMetadata = new NativeArray<VoronoiSite>[count];

            // Маркер процесса генерации
            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            
            // Сущность прогресса
            var progressEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(progressEntity, new MapGenerationProgress
            {
                CurrentProgress = 0f,
                CurrentLevel = 0,
                TotalLevels = count,
                IsGenerating = true,
                StatusMessage = "Initializing..."
            });

            m_OverallSW.Restart();
            m_IsInitialized = true;
            Debug.Log($"[MapGen] Initialization complete. Levels: {count}");
        }

        /// <summary>
        /// Полный цикл генерации одного уровня (Sites -> Delaunay -> Voronoi -> Entities -> Storage)
        /// Выполняется синхронно за один кадр, чтобы не усложнять логику владения памятью.
        /// </summary>
private void ProcessSingleLevel(int levelIndex)
        {
            m_LevelSW.Restart();
            Debug.Log($"--- Processing Level {levelIndex} ---");

            // 1. Подготовка и Генерация Сайтов
            GetParentData(levelIndex, out var parentCells, out var parentSites, out var parentMeta);

            (var sites, var siteMeta) = SiteGenerator.Generate(
                m_Settings, m_LevelSettings, m_LevelSettings[levelIndex],
                levelIndex, parentCells, parentSites, parentMeta
            );

            // Получаем кол-во итераций из настроек
            int iterations = m_LevelSettings[levelIndex].RelaxationIterations;
            // Гарантируем хотя бы 1 проход построения (даже если итераций 0)
            int totalPasses = math.max(1, iterations + 1);

            // Временные списки (будем переиспользовать или создавать заново)
            NativeList<DelaunayTriangle> triangles = default;
            NativeList<int3> edges = default;
            NativeList<VoronoiCell> voronoiCells = default;
            NativeList<VoronoiEdge> voronoiEdges = default;

            for (int pass = 0; pass < totalPasses; pass++)
            {
                bool isLastPass = (pass == totalPasses - 1);
                
                // Очистка от прошлого прохода
                if (triangles.IsCreated) triangles.Dispose();
                if (edges.IsCreated) edges.Dispose();
                if (voronoiCells.IsCreated) voronoiCells.Dispose();
                if (voronoiEdges.IsCreated) voronoiEdges.Dispose();

                // A. Триангуляция
                triangles = new NativeList<DelaunayTriangle>(Allocator.TempJob);
                edges = new NativeList<int3>(Allocator.TempJob);

                new DelaunayTriangulationJob
                {
                    Sites = sites,
                    SiteMetadata = siteMeta,
                    Level = levelIndex,
                    Triangles = triangles,
                    Edges = edges
                }.Schedule(default).Complete();

                // B. Вороной
                voronoiCells = new NativeList<VoronoiCell>(Allocator.TempJob);
                voronoiEdges = new NativeList<VoronoiEdge>(Allocator.TempJob);

                new VoronoiConstructionJob
                {
                    Triangles = triangles.AsArray(),
                    Sites = sites,
                    Level = levelIndex,
                    Cells = voronoiCells,
                    Edges = voronoiEdges
                }.Schedule(default).Complete();

                // C. Если это НЕ последний проход -> Релаксация Ллойда
                if (!isLastPass)
                {
                    new LloydRelaxationJob
                    {
                        Cells = voronoiCells.AsArray(),
                        SiteMetadata = siteMeta,
                        MapSize = m_Settings.MapSize,
                        Sites = sites // Обновляет позиции прямо в массиве sites
                    }.Schedule(default).Complete();
                    
                    // Сайты сдвинулись, идем на следующий круг перестраивать сетку
                }
            }

            // Сохраняем финальные сайты (они могли сдвинуться)
            m_LevelSites[levelIndex] = sites;
            m_LevelSiteMetadata[levelIndex] = siteMeta;

            // D. Создание сущностей (только для финальной сетки)
            EntityCreationPipeline.CreateEntities(
                EntityManager,
                levelIndex,
                m_LevelSettings[levelIndex],
                sites,
                siteMeta,
                voronoiCells,
                voronoiEdges
            );

            // E. Сохранение ячеек
            m_LevelCells[levelIndex] = new NativeArray<VoronoiCell>(voronoiCells.Length, Allocator.Persistent);
            NativeArray<VoronoiCell>.Copy(voronoiCells.AsArray(), m_LevelCells[levelIndex]);

            // F. Очистка
            triangles.Dispose();
            edges.Dispose();
            voronoiCells.Dispose();
            voronoiEdges.Dispose();
            
            if (parentCells.Length == 0 && levelIndex == 0) parentCells.Dispose();
            if (parentSites.Length == 0 && levelIndex == 0) parentSites.Dispose();
            if (parentMeta.Length == 0 && levelIndex == 0) parentMeta.Dispose();

            m_LevelSW.Stop();
            Debug.Log($"[Level {levelIndex}] Complete with {iterations} relaxation steps. Sites: {sites.Length}");
        }

        private void GetParentData(int currentLevel, out NativeArray<VoronoiCell> pCells, out NativeArray<float2> pSites, out NativeArray<VoronoiSite> pMeta)
        {
            // Для уровня 0 родителей нет
            if (currentLevel == 0)
            {
                pCells = new NativeArray<VoronoiCell>(0, Allocator.TempJob);
                pSites = new NativeArray<float2>(0, Allocator.TempJob);
                pMeta = new NativeArray<VoronoiSite>(0, Allocator.TempJob);
                return;
            }

            int pIdx = currentLevel - 1;
            
            // Безопасное получение данных предыдущего уровня
            pCells = (m_LevelCells[pIdx].IsCreated) ? m_LevelCells[pIdx] : new NativeArray<VoronoiCell>(0, Allocator.TempJob);
            pSites = (m_LevelSites[pIdx].IsCreated) ? m_LevelSites[pIdx] : new NativeArray<float2>(0, Allocator.TempJob);
            pMeta = (m_LevelSiteMetadata[pIdx].IsCreated) ? m_LevelSiteMetadata[pIdx] : new NativeArray<VoronoiSite>(0, Allocator.TempJob);
        }

        private void UpdateProgressEntity()
        {
            var query = GetEntityQuery(typeof(MapGenerationProgress));
            if (query.CalculateEntityCount() == 0) return;

            var entity = query.GetSingletonEntity();
            var progress = EntityManager.GetComponentData<MapGenerationProgress>(entity);

            float val = (float)m_CurrentLevel / m_LevelSettings.Length;
            progress.CurrentProgress = math.clamp(val, 0f, 1f);
            progress.CurrentLevel = m_CurrentLevel;
            progress.StatusMessage = $"Generating Level {m_CurrentLevel}...";
            
            EntityManager.SetComponentData(entity, progress);
        }

        private void CompleteGeneration()
        {
            m_IsComplete = true;
            m_OverallSW.Stop();
            Debug.Log($"[MapGen] TOTAL TIME: {m_OverallSW.ElapsedMilliseconds} ms");

            // Маркируем готовность
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            EntityManager.AddComponent<MapGeneratedTag>(settingsEntity);
            EntityManager.RemoveComponent<MapGenerationInProgress>(settingsEntity);

            // Обновляем прогресс бар
            var query = GetEntityQuery(typeof(MapGenerationProgress));
            if (query.CalculateEntityCount() > 0)
            {
                var entity = query.GetSingletonEntity();
                var p = EntityManager.GetComponentData<MapGenerationProgress>(entity);
                p.CurrentProgress = 1f;
                p.StatusMessage = "Done!";
                p.IsGenerating = false;
                EntityManager.SetComponentData(entity, p);
            }

            Cleanup();
            Enabled = false; // Останавливаем апдейты системы
        }

        private void Cleanup()
        {
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();

            DisposeArrayOfArrays(m_LevelCells);
            DisposeArrayOfArrays(m_LevelSites);
            DisposeArrayOfArrays(m_LevelSiteMetadata);

            m_LevelCells = null;
            m_LevelSites = null;
            m_LevelSiteMetadata = null;
        }

        private void DisposeArrayOfArrays<T>(NativeArray<T>[] arrays) where T : struct
        {
            if (arrays == null) return;
            foreach (var arr in arrays)
            {
                if (arr.IsCreated) arr.Dispose();
            }
        }

        protected override void OnDestroy()
        {
            Cleanup();
        }
    }
}