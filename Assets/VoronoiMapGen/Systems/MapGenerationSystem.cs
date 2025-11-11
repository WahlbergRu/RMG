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
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private EntityQuery _settingsQuery;
        private MapSettings m_Settings;
        private NativeArray<LevelSettings> m_LevelSettings;
        
        // Store cells for ALL levels
        private NativeArray<VoronoiCell>[] m_LevelCells;

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
                if (m_CurrentLevel < m_LevelSettings.Length)
                {
                    if (m_CurrentWork.Stage == LevelWorkStage.None)
                    {
                        StartLevelWork(m_CurrentLevel);
                    }
                    else
                    {
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
                                        m_LevelCells[m_CurrentWork.LevelIndex].Dispose();

                                    m_LevelCells[m_CurrentWork.LevelIndex] = new NativeArray<VoronoiCell>(m_CurrentWork.VoronoiCells.Length, Allocator.Persistent);
                                    NativeArray<VoronoiCell>.Copy(m_CurrentWork.VoronoiCells.AsArray(), m_LevelCells[m_CurrentWork.LevelIndex]);
                                    Debug.Log($"[Level {m_CurrentWork.LevelIndex}] Stored {m_CurrentWork.VoronoiCells.Length} cells for this level");
                                }

                                // Dispose current work and move to next level
                                m_CurrentWork.Dispose();
                                m_CurrentWork = new LevelWork(); // Reset to default
                                m_CurrentLevel++;
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
            bool parentCellsAllocated = false;

            try
            {
                if (levelIndex > 0)
                {
                    // Look for cells from the immediate previous level
                    if (levelIndex - 1 < m_LevelCells.Length && m_LevelCells[levelIndex - 1].IsCreated && m_LevelCells[levelIndex - 1].Length > 0)
                    {
                        parentCells = m_LevelCells[levelIndex - 1];
                        Debug.Log($"[Level {levelIndex}] Found {parentCells.Length} parent cells from level {levelIndex - 1}");
                    }
                    else
                    {
                        Debug.LogWarning($"[Level {levelIndex}] No parent cells found for level {levelIndex - 1}. Using empty array.");
                        parentCells = new NativeArray<VoronoiCell>(0, Allocator.Temp);
                        parentCellsAllocated = true;
                    }
                }
                else
                {
                    Debug.Log($"[Level {levelIndex}] Level 0 - no parent cells needed");
                    parentCells = new NativeArray<VoronoiCell>(0, Allocator.Temp);
                    parentCellsAllocated = true;
                }
                
                var (sites, siteMeta) = SiteGenerator.Generate(
                    m_Settings, 
                    m_LevelSettings, 
                    m_LevelSettings[levelIndex], 
                    levelIndex, 
                    parentCells
                );
                m_JobSW.Stop();
                Debug.Log($"[Level {levelIndex}] Site generation {m_JobSW.ElapsedMilliseconds} ms ({sites.Length} sites)");

                // Create persistent arrays for sites and metadata
                NativeArray<float2> persistentSites = new NativeArray<float2>(sites.Length, Allocator.Persistent);
                sites.CopyTo(persistentSites);
                sites.Dispose();

                NativeArray<VoronoiSite> persistentMeta = new NativeArray<VoronoiSite>(siteMeta.Length, Allocator.Persistent);
                siteMeta.CopyTo(persistentMeta);
                siteMeta.Dispose();

                // Initialize Delaunay data structures
                NativeList<DelaunayTriangle> triangles = new NativeList<DelaunayTriangle>(Allocator.Persistent);
                NativeList<int3> edges = new NativeList<int3>(Allocator.Persistent);

                // Schedule Delaunay job
                DelaunayTriangulationJob delaunayJob = new DelaunayTriangulationJob
                {
                    Sites = persistentSites,
                    SiteMetadata = persistentMeta,
                    Level = levelIndex,
                    Triangles = triangles,
                    Edges = edges
                };

                JobHandle handle = delaunayJob.Schedule(default);

                // Create work item
                m_CurrentWork = new LevelWork
                {
                    LevelIndex = levelIndex,
                    Sites = persistentSites,
                    SiteMetadata = persistentMeta,
                    Triangles = triangles,
                    Edges = edges,
                    JobHandle = handle,
                    Stage = LevelWorkStage.DelaunayScheduled
                };

                Debug.Log($"[Level {levelIndex}] Delaunay scheduled ({persistentSites.Length} sites)");
            }
            finally
            {
                // Only dispose if we allocated a new array
                if (parentCellsAllocated && parentCells.IsCreated)
                {
                    parentCells.Dispose();
                }
            }
        }

        private void ScheduleVoronoi(ref LevelWork work)
        {
            // Initialize Voronoi data structures
            work.VoronoiCells = new NativeList<VoronoiCell>(Allocator.Persistent);
            work.VoronoiEdges = new NativeList<VoronoiEdge>(Allocator.Persistent);

            // Schedule Voronoi job
            VoronoiConstructionJob voronoiJob = new VoronoiConstructionJob
            {
                Triangles = work.Triangles.AsArray(),
                Sites = work.Sites,
                SiteMetadata = work.SiteMetadata,
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
            EntityCreationPipeline.CreateEntities(
                EntityManager,
                work.LevelIndex,
                m_LevelSettings[work.LevelIndex],
                work.Sites,
                work.SiteMetadata,
                work.VoronoiCells,
                work.VoronoiEdges
            );

            Debug.Log($"[Level {work.LevelIndex}] Entity creation complete. Sites: {work.Sites.Length}, Cells: {work.VoronoiCells.Length}, Edges: {work.VoronoiEdges.Length}");
        }

        private void Cleanup()
        {
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

            // Stop timers
            if (m_OverallSW != null) m_OverallSW.Stop();
            if (m_StageSW != null) m_StageSW.Stop();
            if (m_LevelSW != null) m_LevelSW.Stop();
            if (m_JobSW != null) m_JobSW.Stop();
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
            public NativeArray<float2> Sites;
            public NativeArray<VoronoiSite> SiteMetadata;
            public NativeList<DelaunayTriangle> Triangles;
            public NativeList<int3> Edges;
            public NativeList<VoronoiCell> VoronoiCells;
            public NativeList<VoronoiEdge> VoronoiEdges;
            public JobHandle JobHandle;
            public LevelWorkStage Stage;

            public void Dispose()
            {
                if (Sites.IsCreated) Sites.Dispose();
                if (SiteMetadata.IsCreated) SiteMetadata.Dispose();
                if (Triangles.IsCreated) Triangles.Dispose();
                if (Edges.IsCreated) Edges.Dispose();
                if (VoronoiCells.IsCreated) VoronoiCells.Dispose();
                if (VoronoiEdges.IsCreated) VoronoiEdges.Dispose();
                
                // FIXED: JobHandle doesn't have IsCreated property - use default comparison instead
                if (JobHandle != default && !JobHandle.IsCompleted)
                {
                    JobHandle.Complete();
                }
            }
        }
    }
}