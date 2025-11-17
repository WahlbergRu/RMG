using System;
using System.Collections.Generic;
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
        private EntityQuery _settingsQuery;
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;

        // Store cells for ALL levels - Persistent
        private NativeArray<VoronoiCell>[] m_LevelCells;
        // Store sites for ALL levels - Persistent
        private NativeArray<float2>[] m_LevelSites;
        // Store site metadata for ALL levels - Persistent (NEW)
        private NativeArray<VoronoiSite>[] m_LevelSiteMetadata;

        private int m_CurrentLevel = 0;
        private int m_CurrentStage = 0;
        private bool m_GenerationStarted = false;
        private bool m_GenerationComplete = false;

        private const int m_TotalStages = 4;
        private LevelWork m_CurrentWork; // Only process one level at a time

        // Timers
        private Stopwatch m_OverallSW;
        private Stopwatch m_StageSW;
        private Stopwatch m_LevelSW;
        private Stopwatch m_JobSW;

        protected override void OnCreate()
        {
            _settingsQuery = GetEntityQuery(ComponentType.ReadOnly<MapSettings>());
            RequireForUpdate(_settingsQuery);

            m_OverallSW = new Stopwatch();
            m_StageSW = new Stopwatch();
            m_LevelSW = new Stopwatch();
            m_JobSW = new Stopwatch();
        }

        protected override void OnUpdate()
        {
            if (m_GenerationComplete)
                return;

            if (!m_GenerationStarted)
            {
                Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
                if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
                {
                    Enabled = false;
                    return;
                }

                m_Settings = SystemAPI.GetSingleton<MapSettings>();
                DynamicBuffer<LevelSettings> buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
                m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

                // Initialize array to store cells for all levels
                m_LevelCells = new NativeArray<VoronoiCell>[m_LevelSettings.Length];
                // Initialize array to store sites for all levels
                m_LevelSites = new NativeArray<float2>[m_LevelSettings.Length];
                // Initialize array to store site metadata for all levels (NEW)
                m_LevelSiteMetadata = new NativeArray<VoronoiSite>[m_LevelSettings.Length]; // <<< НОВОЕ

                EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);

                Entity progressEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(progressEntity, new MapGenerationProgress
                {
                    CurrentProgress = 0f,
                    CurrentLevel = 0,
                    TotalLevels = m_LevelSettings.Length,
                    IsGenerating = true,
                    StatusMessage = "Initializing..."
                });

                m_CurrentLevel = 0;
                m_CurrentStage = 0;
                m_GenerationStarted = true;

                m_OverallSW.Restart();
                m_StageSW.Restart();
            }

            // ---------- PROGRESS ----------
            EntityQuery progressQuery = GetEntityQuery(typeof(MapGenerationProgress));
            if (progressQuery.CalculateEntityCount() > 0)
            {
                Entity progressEntity = progressQuery.GetSingletonEntity();
                MapGenerationProgress progress = EntityManager.GetComponentData<MapGenerationProgress>(progressEntity);

                float progressValue = (float)m_CurrentLevel / m_LevelSettings.Length;
                progress.CurrentProgress = math.clamp(progressValue, 0f, 1f);
                progress.CurrentLevel = m_CurrentLevel;
                progress.StatusMessage = $"Generating level {m_CurrentLevel}/{m_LevelSettings.Length}";
                EntityManager.SetComponentData(progressEntity, progress);
            }

            // ---------- PROCESS CURRENT LEVEL ----------
            if (m_CurrentStage == 0)
            {
                Debug.Log($"[OnUpdate] CurrentLevel: {m_CurrentLevel}, CurrentWork.Stage: {m_CurrentWork.Stage}, m_LevelSettings.Length: {m_LevelSettings.Length}");

                if (m_CurrentLevel < m_LevelSettings.Length)
                {
                    if (m_CurrentWork.Stage == LevelWorkStage.None)
                    {
                        Debug.Log($"[OnUpdate] Calling StartLevelWork({m_CurrentLevel})");
                        StartLevelWork(m_CurrentLevel);
                    }
                    else
                    {
                        Debug.Log($"[OnUpdate] Processing current work for level {m_CurrentWork.LevelIndex}, stage {m_CurrentWork.Stage}");
                        // Process the current work
                        switch (m_CurrentWork.Stage)
                        {
                            case LevelWorkStage.DelaunayScheduled:
                                if (!m_CurrentWork.JobHandle.IsCompleted)
                                    break;

                                m_CurrentWork.JobHandle.Complete();
                                m_CurrentWork.Stage = LevelWorkStage.DelaunayComplete;

                                Debug.Log($"[Level {m_CurrentWork.LevelIndex}] Delaunay complete. Scheduling Voronoi...");
                                ScheduleVoronoi(ref m_CurrentWork);
                                break;

                            case LevelWorkStage.VoronoiScheduled:
                                if (!m_CurrentWork.JobHandle.IsCompleted)
                                    break;

                                m_CurrentWork.JobHandle.Complete();
                                m_CurrentWork.Stage = LevelWorkStage.VoronoiComplete;

                                Debug.Log($"[Level {m_CurrentWork.LevelIndex}] Voronoi complete. Creating entities...");
                                RunEntityCreation(ref m_CurrentWork);

                                // Store cells for this level
                                if (m_CurrentWork.VoronoiCells.Length > 0)
                                {
                                    if (m_LevelCells[m_CurrentWork.LevelIndex].IsCreated)
                                    {
                                        Debug.Log($"[OnUpdate] Disposing m_LevelCells[{m_CurrentWork.LevelIndex}]");
                                        m_LevelCells[m_CurrentWork.LevelIndex].Dispose(); // m_LevelCells[0].Dispose()
                                    }

                                    Debug.Log($"[OnUpdate] Creating new m_LevelCells[{m_CurrentWork.LevelIndex}]");
                                    m_LevelCells[m_CurrentWork.LevelIndex] = new NativeArray<VoronoiCell>(m_CurrentWork.VoronoiCells.Length, Allocator.Persistent);
                                    NativeArray<VoronoiCell>.Copy(m_CurrentWork.VoronoiCells.AsArray(), m_LevelCells[m_CurrentWork.LevelIndex]);
                                    Debug.Log($"[Level {m_CurrentWork.LevelIndex}] Stored {m_CurrentWork.VoronoiCells.Length} cells for this level");
                                }

                                // Dispose current work and move to next level
                                Debug.Log($"[OnUpdate] Disposing m_CurrentWork for level {m_CurrentWork.LevelIndex}");
                                m_CurrentWork.Dispose(); // <<< Освобождает Triangles, Edges, VoronoiCells, VoronoiEdges. Sites, SiteMetadata НЕ освобождается.
                                Debug.Log($"[OnUpdate] m_CurrentWork disposed for level {m_CurrentWork.LevelIndex}");

                                m_CurrentWork = new LevelWork(); // Reset to default
                                Debug.Log($"[OnUpdate] m_CurrentWork reset. Current m_CurrentWork.Stage: {m_CurrentWork.Stage}");

                                m_CurrentLevel++; // <<< m_CurrentLevel теперь 1
                                Debug.Log($"Moving to level {m_CurrentLevel}");
                                break; // FIXED: Added missing break statement
                            

                            case LevelWorkStage.VoronoiComplete: // <<< Вот этот случай ДОЛЖЕН сработать для L0
                                Debug.Log($"[OnUpdate] Processing VoronoiComplete for level {m_CurrentWork.LevelIndex}");
                                Debug.Log($"[OnUpdate] Running Entity Creation for level {m_CurrentWork.LevelIndex}...");
                                RunEntityCreation(ref m_CurrentWork);
                                Debug.Log($"[OnUpdate] Entity Creation for level {m_CurrentWork.LevelIndex} completed.");

                                // Store cells for this level
                                Debug.Log($"[OnUpdate] Checking VoronoiCells length for level {m_CurrentWork.LevelIndex}: {m_CurrentWork.VoronoiCells.Length}");
                                if (m_CurrentWork.VoronoiCells.Length > 0)
                                {
                                    Debug.Log($"[OnUpdate] m_CurrentWork.VoronoiCells.Length > 0 for level {m_CurrentWork.LevelIndex}. Checking if m_LevelCells[{m_CurrentWork.LevelIndex}] is created: {m_LevelCells[m_CurrentWork.LevelIndex].IsCreated}");
                                    if (m_LevelCells[m_CurrentWork.LevelIndex].IsCreated)
                                    {
                                        Debug.Log($"[OnUpdate] Disposing m_LevelCells[{m_CurrentWork.LevelIndex}]");
                                        m_LevelCells[m_CurrentWork.LevelIndex].Dispose(); // m_LevelCells[0].Dispose()
                                    }

                                    Debug.Log($"[OnUpdate] Creating new m_LevelCells[{m_CurrentWork.LevelIndex}] with length {m_CurrentWork.VoronoiCells.Length}");
                                    m_LevelCells[m_CurrentWork.LevelIndex] = new NativeArray<VoronoiCell>(m_CurrentWork.VoronoiCells.Length, Allocator.Persistent);
                                    Debug.Log($"[OnUpdate] Copying VoronoiCells data for level {m_CurrentWork.LevelIndex} (length {m_CurrentWork.VoronoiCells.Length})");
                                    NativeArray<VoronoiCell>.Copy(m_CurrentWork.VoronoiCells.AsArray(), m_LevelCells[m_CurrentWork.LevelIndex]);
                                    Debug.Log($"[Level {m_CurrentWork.LevelIndex}] Stored {m_CurrentWork.VoronoiCells.Length} cells for this level");
                                }
                                else
                                {
                                    Debug.Log($"[OnUpdate] m_CurrentWork.VoronoiCells.Length is 0 for level {m_CurrentWork.LevelIndex}, skipping storage to m_LevelCells.");
                                }

                                // Dispose current work and move to next level
                                Debug.Log($"[OnUpdate] About to dispose m_CurrentWork for level {m_CurrentWork.LevelIndex}");
                                m_CurrentWork.Dispose(); // <<< Освобождает Triangles, Edges, VoronoiCells, VoronoiEdges. Sites, SiteMetadata НЕ освобождается.
                                Debug.Log($"[OnUpdate] m_CurrentWork disposed for level {m_CurrentWork.LevelIndex}");

                                Debug.Log($"[OnUpdate] Resetting m_CurrentWork...");
                                m_CurrentWork = new LevelWork(); // Reset to default
                                Debug.Log($"[OnUpdate] m_CurrentWork reset. Current m_CurrentWork.Stage: {m_CurrentWork.Stage}");

                                m_CurrentLevel++; // <<< m_CurrentLevel теперь 1
                                Debug.Log($"Moving to level {m_CurrentLevel}");
                                break; // FIXED: Added missing break statement
                        }
                    }
                }
                else
                {
                    // All levels completed
                    m_StageSW.Stop();
                    Debug.Log($"[Stage 0] Level generation completed in {m_StageSW.ElapsedMilliseconds} ms");
                    m_CurrentStage = 1;
                    m_StageSW.Restart();
                }
            }
            // ---------- NEXT STAGES ----------
            if (m_CurrentStage > 0)
            {
                switch (m_CurrentStage)
                {
                    case 1:
                        Debug.Log("[Stage 1] Height generation...");
                        HeightGenerationPipeline.GenerateHeights(EntityManager, m_Settings, m_LevelSettings);
                        m_CurrentStage = 2;
                        break;

                    case 2:
                        Debug.Log("[Stage 2] Biome generation...");
                        BiomeGenerationPipeline.GenerateBiomes(EntityManager, m_Settings);
                        m_CurrentStage = 3;
                        break;

                    case 3:
                        Debug.Log("[Stage 3] Report...");
                        MapReportGenerator.Report(EntityManager, m_Settings, m_LevelSettings);
                        m_CurrentStage = 4;
                        break;
                }
            }

            // ---------- FINAL ----------
            if (m_CurrentStage >= m_TotalStages)
            {
                m_GenerationComplete = true;
                m_OverallSW.Stop();

                Debug.Log($"[Overall] Map generation complete in {m_OverallSW.ElapsedMilliseconds} ms");

                Entity progressEntity = progressQuery.GetSingletonEntity();
                MapGenerationProgress progress = EntityManager.GetComponentData<MapGenerationProgress>(progressEntity);
                progress.CurrentProgress = 1f;
                progress.StatusMessage = "Complete!";
                progress.IsGenerating = false;
                EntityManager.SetComponentData(progressEntity, progress);

                Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
                EntityManager.AddComponent<MapGeneratedTag>(settingsEntity);

                Cleanup();
                Enabled = false;
            }
        }

        private void StartLevelWork(int levelIndex)
        {
            Debug.Log($"[Level {levelIndex}] Starting...");
            m_LevelSW.Restart();

            // --- Site Generation ---
            m_JobSW.Restart();

            NativeArray<VoronoiCell> parentCells = default;
            NativeArray<float2> parentSites = default;
            NativeArray<VoronoiSite> parentSiteMetadata = default; // <<< НОВОЕ

            // Получаем родительские данные из m_LevelCells, m_LevelSites и m_LevelSiteMetadata
            if (levelIndex > 0)
            {
                // Look for parent cells, sites, and site metadata from the immediate previous level
                if (levelIndex - 1 < m_LevelCells.Length && m_LevelCells[levelIndex - 1].IsCreated && m_LevelCells[levelIndex - 1].Length > 0)
                {
                    parentCells = m_LevelCells[levelIndex - 1]; // <<< Persistent массив
                    Debug.Log($"[Level {levelIndex}] Found {parentCells.Length} parent cells from level {levelIndex - 1}");
                }
                else
                {
                    Debug.LogWarning($"[Level {levelIndex}] No parent cells found for level {levelIndex - 1}. Using empty array.");
                    parentCells = new NativeArray<VoronoiCell>(0, Allocator.Persistent); // <<< Используем Persistent
                }

                if (levelIndex - 1 < m_LevelSites.Length && m_LevelSites[levelIndex - 1].IsCreated && m_LevelSites[levelIndex - 1].Length > 0)
                {
                    parentSites = m_LevelSites[levelIndex - 1]; // <<< Persistent массив
                    Debug.Log($"[Level {levelIndex}] Found {parentSites.Length} parent sites from level {levelIndex - 1}");
                }
                else
                {
                    Debug.LogWarning($"[Level {levelIndex}] No parent sites found for level {levelIndex - 1}. Using empty array.");
                    parentSites = new NativeArray<float2>(0, Allocator.Persistent); // <<< Используем Persistent
                }

                // <<< НОВОЕ: Получаем parentSiteMetadata >>>
                if (levelIndex - 1 < m_LevelSiteMetadata.Length && m_LevelSiteMetadata[levelIndex - 1].IsCreated && m_LevelSiteMetadata[levelIndex - 1].Length > 0)
                {
                    parentSiteMetadata = m_LevelSiteMetadata[levelIndex - 1]; // <<< Persistent массив
                    Debug.Log($"[Level {levelIndex}] Found {parentSiteMetadata.Length} parent site metadata from level {levelIndex - 1}");
                }
                else
                {
                    Debug.LogWarning($"[Level {levelIndex}] No parent site metadata found for level {levelIndex - 1}. Using empty array.");
                    parentSiteMetadata = new NativeArray<VoronoiSite>(0, Allocator.Persistent); // <<< Используем Persistent
                }
            }
            else
            {
                Debug.Log($"[Level {levelIndex}] Level 0 - no parent cells, sites, or site metadata needed");
                parentCells = new NativeArray<VoronoiCell>(0, Allocator.Persistent); // <<< Используем Persistent
                parentSites = new NativeArray<float2>(0, Allocator.Persistent);     // <<< Используем Persistent
                parentSiteMetadata = new NativeArray<VoronoiSite>(0, Allocator.Persistent); // <<< Используем Persistent (НОВОЕ)
            }

            (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMeta) = SiteGenerator.Generate(
                m_Settings,
                m_LevelSettings,
                m_LevelSettings[levelIndex],
                levelIndex,
                parentCells, // <<< Передаём Persistent массив
                parentSites,  // <<< Передаём Persistent массив
                parentSiteMetadata // <<< Передаём Persistent массив (НОВОЕ)
            );
            m_JobSW.Stop();
            Debug.Log($"[Level {levelIndex}] Site generation {m_JobSW.ElapsedMilliseconds} ms ({sites.Length} sites)");

            // sites и siteMeta теперь Persistent (владение передано из SiteGenerator)

            // --- Сохраняем sites текущего уровня (владение передаётся MapGenerationSystem) ---
            if (m_LevelSites[levelIndex].IsCreated)
            {
                m_LevelSites[levelIndex].Dispose(); // Освобождаем старый, если был (на всякий случай)
            }
            m_LevelSites[levelIndex] = sites; // <<< sites теперь принадлежит m_LevelSites[levelIndex]

            // --- Сохраняем siteMetadata текущего уровня (владение передаётся MapGenerationSystem) (NEW) ---
            if (m_LevelSiteMetadata[levelIndex].IsCreated) // <<< НОВОЕ
            {                                              // <<< НОВОЕ
                m_LevelSiteMetadata[levelIndex].Dispose(); // Освобождаем старый, если был (на всякий случай) // <<< НОВОЕ
            }                                              // <<< НОВОЕ
            m_LevelSiteMetadata[levelIndex] = siteMeta;    // <<< siteMeta теперь принадлежит m_LevelSiteMetadata[levelIndex] // <<< НОВОЕ

            // Initialize Delaunay data structures (Persistent для последующего использования)
            NativeList<DelaunayTriangle> triangles = new NativeList<DelaunayTriangle>(Allocator.Persistent);
            NativeList<int3> edges = new NativeList<int3>(Allocator.Persistent);

            // Schedule Delaunay job
            DelaunayTriangulationJob delaunayJob = new DelaunayTriangulationJob
            {
                Sites = sites, // Передаём Persistent массив sites
                SiteMetadata = siteMeta, // Передаём Persistent массив siteMeta
                Level = levelIndex,
                Triangles = triangles,
                Edges = edges
            };

            JobHandle handle = delaunayJob.Schedule(default);
            handle.Complete(); // <<< Выполняем синхронно

            // Schedule Voronoi job (если он тоже синхронный)
            NativeList<VoronoiCell> voronoiCells = new NativeList<VoronoiCell>(Allocator.Persistent);
            NativeList<VoronoiEdge> voronoiEdges = new NativeList<VoronoiEdge>(Allocator.Persistent);

            VoronoiConstructionJob voronoiJob = new VoronoiConstructionJob
            {
                Triangles = triangles.AsArray(),
                Sites = sites, // Передаём Persistent массив sites
                SiteMetadata = siteMeta, // Передаём Persistent массив siteMeta
                Level = levelIndex,
                Cells = voronoiCells,
                Edges = voronoiEdges
            };

            JobHandle voronoiHandle = voronoiJob.Schedule(default);
            voronoiHandle.Complete(); // <<< Выполняем синхронно

            // Create work item (владение НЕ передаётся для Sites и SiteMetadata!)
            m_CurrentWork = new LevelWork
            {
                LevelIndex = levelIndex,
                // Sites и SiteMetadata НЕ передаются в LevelWork
                // Они принадлежат MapGenerationSystem (m_LevelSites, m_LevelSiteMetadata)
                // и будут использованы в RunEntityCreation напрямую из MapGenerationSystem
                // Sites = sites, // <<< УБРАТЬ!
                // SiteMetadata = siteMeta, // <<< УБРАТЬ!
                Triangles = triangles, // <<< Владение передаётся LevelWork
                Edges = edges,         // <<< Владение передаётся LevelWork
                VoronoiCells = voronoiCells, // <<< Владение передаётся LevelWork
                VoronoiEdges = voronoiEdges, // <<< Владение передаётся LevelWork
                JobHandle = default,
                Stage = LevelWorkStage.VoronoiComplete
            };

            Debug.Log($"[Level {levelIndex}] Level generation steps (SiteGen, Delaunay, Voronoi) completed synchronously. Sites: {sites.Length}, Cells: {voronoiCells.Length}, Edges: {voronoiEdges.Length}");

            // Освобождаем временные массивы, которые больше не нужны - УДАЛЕНО
            // triangles.Dispose(); // УДАЛЕНО
            // edges.Dispose();     // УДАЛЕНО
        }

        private void ScheduleVoronoi(ref LevelWork work)
        {
            // Initialize Voronoi data structures (если не было сделано ранее)
            // work.VoronoiCells = new NativeList<VoronoiCell>(Allocator.Persistent);
            // work.VoronoiEdges = new NativeList<VoronoiEdge>(Allocator.Persistent);

            // Schedule Voronoi job
            VoronoiConstructionJob voronoiJob = new VoronoiConstructionJob
            {
                Triangles = work.Triangles.AsArray(),
                Sites = m_LevelSites[work.LevelIndex], // <<< Берём из MapGenerationSystem
                SiteMetadata = m_LevelSiteMetadata[work.LevelIndex], // <<< Берём из MapGenerationSystem (НОВОЕ)
                Level = work.LevelIndex,
                Cells = work.VoronoiCells,
                Edges = work.VoronoiEdges
            };

            work.JobHandle = voronoiJob.Schedule(default);
            work.Stage = LevelWorkStage.VoronoiScheduled;
        }

        private void RunEntityCreation(ref LevelWork work)
        {
            // Create entities for this level
            // Берём Sites и SiteMetadata из MapGenerationSystem, а не из LevelWork
            EntityCreationPipeline.CreateEntities(
                EntityManager,
                work.LevelIndex,
                m_LevelSettings[work.LevelIndex],
                m_LevelSites[work.LevelIndex], // <<< Берём из MapGenerationSystem
                m_LevelSiteMetadata[work.LevelIndex], // <<< Берём из MapGenerationSystem (НОВОЕ)
                work.VoronoiCells,
                work.VoronoiEdges
            );

            Debug.Log($"[Level {work.LevelIndex}] Entity creation complete. Sites: {m_LevelSites[work.LevelIndex].Length}, Cells: {work.VoronoiCells.Length}, Edges: {work.VoronoiEdges.Length}");
        }

        private void Cleanup()
        {
            Debug.Log("MapGenerationSystem.Cleanup() called.");

            // Dispose current work if exists
            if (m_CurrentWork.Stage != LevelWorkStage.None)
            {
                if (!m_CurrentWork.JobHandle.IsCompleted)
                {
                    m_CurrentWork.JobHandle.Complete();
                }
                m_CurrentWork.Dispose();
            }

            // Dispose level settings
            if (m_LevelSettings.IsCreated)
            {
                m_LevelSettings.Dispose();
            }

            // Dispose ALL level cell arrays
            if (m_LevelCells != null)
            {
                for (int i = 0; i < m_LevelCells.Length; i++)
                {
                    if (m_LevelCells[i].IsCreated)
                    {
                        m_LevelCells[i].Dispose();
                    }
                }
                m_LevelCells = null;
            }

            // Dispose ALL level site arrays
            if (m_LevelSites != null)
            {
                for (int i = 0; i < m_LevelSites.Length; i++)
                {
                    if (m_LevelSites[i].IsCreated)
                    {
                        m_LevelSites[i].Dispose();
                    }
                }
                m_LevelSites = null;
            }

            // Dispose ALL level site metadata arrays (NEW)
            if (m_LevelSiteMetadata != null) // <<< НОВОЕ
            {                                // <<< НОВОЕ
                for (int i = 0; i < m_LevelSiteMetadata.Length; i++) // <<< НОВОЕ
                {                                                     // <<< НОВОЕ
                    if (m_LevelSiteMetadata[i].IsCreated)             // <<< НОВОЕ
                        m_LevelSiteMetadata[i].Dispose();             // <<< НОВОЕ
                }                                                     // <<< НОВОЕ
                m_LevelSiteMetadata = null;                           // <<< НОВОЕ
            }                                                         // <<< НОВОЕ

            // Stop timers
            if (m_OverallSW != null) m_OverallSW.Stop();
            if (m_StageSW != null) m_StageSW.Stop();
            if (m_LevelSW != null) m_LevelSW.Stop();
            if (m_JobSW != null) m_JobSW.Stop();

            Debug.Log("MapGenerationSystem.Cleanup() completed.");
        }

        protected override void OnDestroy()
        {
            Cleanup();
        }

        private enum LevelWorkStage
        {
            None,
            DelaunayScheduled,
            DelaunayComplete,
            VoronoiScheduled,
            VoronoiComplete
        }

        private struct LevelWork : IDisposable
        {
            public int LevelIndex;
            // public NativeArray<float2> Sites; // <<< УБРАТЬ, если берёте из MapGenerationSystem
            // public NativeArray<VoronoiSite> SiteMetadata; // <<< УБРАТЬ, если берёте из MapGenerationSystem
            public NativeList<DelaunayTriangle> Triangles;
            public NativeList<int3> Edges;
            public NativeList<VoronoiCell> VoronoiCells;
            public NativeList<VoronoiEdge> VoronoiEdges;
            public JobHandle JobHandle;
            public LevelWorkStage Stage;

            public void Dispose()
            {
                // НЕ освобождаем Sites и SiteMetadata, если они принадлежат MapGenerationSystem
                // if (Sites.IsCreated) Sites.Dispose(); // <<< УБРАТЬ
                // if (SiteMetadata.IsCreated) SiteMetadata.Dispose(); // <<< УБРАТЬ

                // Освобождаем только то, что принадлежит LevelWork (NativeList)
                if (Triangles.IsCreated) Triangles.Dispose();
                if (Edges.IsCreated) Edges.Dispose();
                if (VoronoiCells.IsCreated) VoronoiCells.Dispose();
                if (VoronoiEdges.IsCreated) VoronoiEdges.Dispose();

                if (JobHandle != default && !JobHandle.IsCompleted)
                {
                    JobHandle.Complete();
                }
            }
        }
    }
}