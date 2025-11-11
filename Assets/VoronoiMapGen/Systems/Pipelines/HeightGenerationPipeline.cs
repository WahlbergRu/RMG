using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using TerrainData = VoronoiMapGen.Components.TerrainData;

namespace VoronoiMapGen.Systems
{
    public static class HeightGenerationPipeline
    {
        public static void GenerateHeights(EntityManager em, MapSettings settings, NativeArray<LevelSettings> levelSettings)
        {
            NativeArray<VoronoiSite> l0Sites = default;
            NativeArray<TerrainData> l0Heights = default;
            NativeArray<TectonicData> l0Tectonic = default;
            NativeArray<RelaxationData> l0Relaxation = default;
            NativeArray<VoronoiEdge> l0Edges = default;
            
            NativeArray<VoronoiSite> l1Sites = default;
            NativeArray<TerrainData> l1Heights = default;
            
            NativeArray<VoronoiSite> l2Sites = default;
            NativeArray<TerrainData> l2Heights = default;
            
            NativeArray<VoronoiSite> l3Sites = default;
            NativeArray<TerrainData> l3Heights = default;
            
            NativeArray<VoronoiSite> l4Sites = default;
            NativeArray<FinalHeightData> l4FinalHeights = default;
            NativeArray<TerrainData> l4Heights = default;
            NativeArray<DetailLevelData> l4LevelData = default;

            try
            {
                // L0: тектоника (без родительских данных)
                l0Sites = GetLevelSites(em, 0);
                l0Edges = GetLevelEdges(em, 0);
                l0Heights = new NativeArray<TerrainData>(l0Sites.Length, Allocator.TempJob);
                l0Tectonic = new NativeArray<TectonicData>(l0Sites.Length, Allocator.TempJob);
                l0Relaxation = new NativeArray<RelaxationData>(l0Sites.Length, Allocator.TempJob);
                
                L0TectonicHeightJob l0Job = new L0TectonicHeightJob
                {
                    Sites = l0Sites,
                    Edges = l0Edges,
                    MapScale = settings.MapSize.x,
                    Heights = l0Heights,
                    TectonicData = l0Tectonic,
                    RelaxationData = l0Relaxation
                };
                l0Job.Schedule(l0Sites.Length, default(JobHandle)).Complete();
                
                // L1: рельеф на основе L0
                l1Sites = GetLevelSites(em, 1);
                l1Heights = new NativeArray<TerrainData>(l1Sites.Length, Allocator.TempJob);
                
                L1ToL3HeightRefinementJob l1Job = new L1ToL3HeightRefinementJob
                {
                    Sites = l1Sites,
                    ParentHeights = l0Heights,
                    ParentLevel = 0,
                    CurrentLevel = 1,
                    Heights = l1Heights
                };
                l1Job.Schedule(l1Sites.Length, default(JobHandle)).Complete();
                
                // L2: рельеф на основе L1
                l2Sites = GetLevelSites(em, 2);
                l2Heights = new NativeArray<TerrainData>(l2Sites.Length, Allocator.TempJob);
                
                L1ToL3HeightRefinementJob l2Job = new L1ToL3HeightRefinementJob
                {
                    Sites = l2Sites,
                    ParentHeights = l1Heights,
                    ParentLevel = 1,
                    CurrentLevel = 2,
                    Heights = l2Heights
                };
                l2Job.Schedule(l2Sites.Length, default(JobHandle)).Complete();
                
                // L3: рельеф на основе L2
                l3Sites = GetLevelSites(em, 3);
                l3Heights = new NativeArray<TerrainData>(l3Sites.Length, Allocator.TempJob);
                
                L1ToL3HeightRefinementJob l3Job = new L1ToL3HeightRefinementJob
                {
                    Sites = l3Sites,
                    ParentHeights = l2Heights,
                    ParentLevel = 2,
                    CurrentLevel = 3,
                    Heights = l3Heights
                };
                l3Job.Schedule(l3Sites.Length, default(JobHandle)).Complete();
                
                // L4: финальная высота на основе L3
                l4Sites = GetLevelSites(em, 4);
                l4LevelData = GetLevelDetailData(em, 4);
                l4FinalHeights = new NativeArray<FinalHeightData>(l4Sites.Length, Allocator.TempJob);
                l4Heights = new NativeArray<TerrainData>(l4Sites.Length, Allocator.TempJob);
                
                L4FinalHeightJob l4Job = new L4FinalHeightJob
                {
                    Sites = l4Sites,
                    ParentHeights = l3Heights,
                    LevelData = l4LevelData,
                    FinalHeights = l4FinalHeights,
                    Heights = l4Heights
                };
                l4Job.Schedule(l4Sites.Length, default(JobHandle)).Complete();
                
                // Сохраняем результаты в ECS
                ApplyHeightsToEntities(em, 0, l0Heights, default);
                ApplyHeightsToEntities(em, 1, l1Heights, default);
                ApplyHeightsToEntities(em, 2, l2Heights, default);
                ApplyHeightsToEntities(em, 3, l3Heights, default);
                ApplyHeightsToEntities(em, 4, l4Heights, l4FinalHeights);
            }
            finally
            {
                // Гарантированное освобождение всех ресурсов
                if (l0Sites.IsCreated) l0Sites.Dispose();
                if (l0Edges.IsCreated) l0Edges.Dispose();
                if (l0Heights.IsCreated) l0Heights.Dispose();
                if (l0Tectonic.IsCreated) l0Tectonic.Dispose();
                if (l0Relaxation.IsCreated) l0Relaxation.Dispose();
                
                if (l1Sites.IsCreated) l1Sites.Dispose();
                if (l1Heights.IsCreated) l1Heights.Dispose();
                
                if (l2Sites.IsCreated) l2Sites.Dispose();
                if (l2Heights.IsCreated) l2Heights.Dispose();
                
                if (l3Sites.IsCreated) l3Sites.Dispose();
                if (l3Heights.IsCreated) l3Heights.Dispose();
                
                if (l4Sites.IsCreated) l4Sites.Dispose();
                if (l4FinalHeights.IsCreated) l4FinalHeights.Dispose();
                if (l4Heights.IsCreated) l4Heights.Dispose();
                if (l4LevelData.IsCreated) l4LevelData.Dispose();
            }
        }
        
        private static NativeArray<VoronoiSite> GetLevelSites(EntityManager em, int level)
        {
            using EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiSite>(),
                ComponentType.ReadOnly<DetailLevelData>()
            );
            
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            NativeArray<VoronoiSite> sites = new NativeArray<VoronoiSite>(entities.Length, Allocator.TempJob);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                DetailLevelData levelData = em.GetComponentData<DetailLevelData>(entity);
                if ((int)levelData.Level == level)
                {
                    sites[count] = em.GetComponentData<VoronoiSite>(entity);
                    count++;
                }
            }

            // Обрезаем массив до реального количества
            NativeArray<VoronoiSite> result = new NativeArray<VoronoiSite>(count, Allocator.Persistent);
            if (count > 0)
            {
                NativeArray<VoronoiSite>.Copy(sites, 0, result, 0, count);
            }
            sites.Dispose();
            return result;
        }

        private static NativeArray<VoronoiEdge> GetLevelEdges(EntityManager em, int level)
        {
            using EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiEdge>(),
                ComponentType.ReadOnly<DetailLevelData>()
            );
            
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            NativeArray<VoronoiEdge> edges = new NativeArray<VoronoiEdge>(entities.Length, Allocator.TempJob);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                DetailLevelData levelData = em.GetComponentData<DetailLevelData>(entity);
                if ((int)levelData.Level == level)
                {
                    edges[count] = em.GetComponentData<VoronoiEdge>(entity);
                    count++;
                }
            }

            NativeArray<VoronoiEdge> result = new NativeArray<VoronoiEdge>(count, Allocator.Persistent);
            if (count > 0)
            {
                NativeArray<VoronoiEdge>.Copy(edges, 0, result, 0, count);
            }
            edges.Dispose();
            return result;
        }

        private static NativeArray<DetailLevelData> GetLevelDetailData(EntityManager em, int level)
        {
            using EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<DetailLevelData>()
            );
            
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            NativeArray<DetailLevelData> levelData = new NativeArray<DetailLevelData>(entities.Length, Allocator.TempJob);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                DetailLevelData data = em.GetComponentData<DetailLevelData>(entity);
                if ((int)data.Level == level)
                {
                    levelData[count] = data;
                    count++;
                }
            }

            NativeArray<DetailLevelData> result = new NativeArray<DetailLevelData>(count, Allocator.Persistent);
            if (count > 0)
            {
                NativeArray<DetailLevelData>.Copy(levelData, 0, result, 0, count);
            }
            levelData.Dispose();
            return result;
        }

        private static void ApplyHeightsToEntities(EntityManager em, int level, 
            NativeArray<TerrainData> heights, NativeArray<FinalHeightData> finalHeights)
        {
            using EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiSite>(),
                ComponentType.ReadOnly<DetailLevelData>()
            );
            
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);

            int heightIndex = 0;
            for (int i = 0; i < entities.Length && heightIndex < heights.Length; i++)
            {
                Entity entity = entities[i];
                DetailLevelData levelData = em.GetComponentData<DetailLevelData>(entity);

                if ((int)levelData.Level == level)
                {
                    if (heightIndex < heights.Length)
                    {
                        em.AddComponentData(entity, heights[heightIndex]);
                    }
                    if (level == 4 && finalHeights.IsCreated && heightIndex < finalHeights.Length)
                    {
                        em.AddComponentData(entity, finalHeights[heightIndex]);
                    }
                    heightIndex++;
                }
            }
        }
    }
}