using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    public static class EntityCreationPipeline
    {
        public static void CreateEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            in NativeList<VoronoiCell> cells,
            in NativeList<VoronoiEdge> edges)
        {
            if (sites.Length != cells.Length)
            {
                Debug.LogWarning($"Level {level}: Sites count ({sites.Length}) != Cells count ({cells.Length}). Truncating to {math.min(sites.Length, cells.Length)}");
                TruncateAndCreateEntities(em, level, levelSettings, sites, siteMetadata, cells, edges);
            }
            else
            {
                CreateLevelEntitiesInternal(em, level, levelSettings, sites, siteMetadata, cells, edges);
            }
        }

        private static void TruncateAndCreateEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            in NativeList<VoronoiCell> cells,
            in NativeList<VoronoiEdge> edges)
        {
            var minCount = math.min(sites.Length, cells.Length);

            var truncatedSites = new NativeArray<float2>(minCount, Allocator.Temp);
            var truncatedSiteMetadata = new NativeArray<VoronoiSite>(minCount, Allocator.Temp);
            var truncatedCells = new NativeList<VoronoiCell>(minCount, Allocator.Temp);

            try
            {
                for (int i = 0; i < minCount; i++)
                {
                    truncatedSites[i] = sites[i];
                    truncatedSiteMetadata[i] = siteMetadata[i];
                    truncatedCells.Add(cells[i]);
                }

                CreateLevelEntitiesInternal(em, level, levelSettings, truncatedSites, truncatedSiteMetadata, truncatedCells, edges);
            }
            finally
            {
                truncatedSites.Dispose();
                truncatedSiteMetadata.Dispose();
                truncatedCells.Dispose();
            }
        }

        private static void CreateLevelEntitiesInternal(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            in NativeList<VoronoiCell> cells,
            in NativeList<VoronoiEdge> edges)
        {
            using var siteEntities = CreateSiteEntities(em, level, levelSettings, sites, siteMetadata);
            using var cellEntities = CreateCellEntities(em, level, levelSettings, cells);

            CreateEdgeEntities(em, level, levelSettings, edges, cellEntities);

            // завершаем все треки задач системы чтобы изменения были видимы сразу
            em.CompleteAllTrackedJobs();
        }

        private static NativeArray<Entity> CreateSiteEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata)
        {
            var siteEntities = new NativeArray<Entity>(sites.Length, Allocator.Temp);

            for (int i = 0; i < sites.Length; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, siteMetadata[i]);
                em.AddComponentData(e, new VoronoiSitePosition { Value = sites[i] });
                AddLevelData(em, e, level, levelSettings);
                siteEntities[i] = e;
            }

            return siteEntities;
        }

        private static NativeArray<Entity> CreateCellEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeList<VoronoiCell> cells)
        {
            var cellEntities = new NativeArray<Entity>(cells.Length, Allocator.Temp);

            for (int i = 0; i < cells.Length; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, cells[i]);
                AddLevelData(em, e, level, levelSettings);
                cellEntities[i] = e;
            }

            return cellEntities;
        }

        private static void CreateEdgeEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeList<VoronoiEdge> edges,
            in NativeArray<Entity> cellEntities)
        {
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                var edgeEntity = em.CreateEntity();

                var (cellA, cellB) = FindCellsForEdge(em, edge, cellEntities);
                if (cellA != Entity.Null && cellB != Entity.Null)
                {
                    edge.CellA = cellA;
                    edge.CellB = cellB;
                }

                em.AddComponentData(edgeEntity, edge);
                AddLevelData(em, edgeEntity, level, levelSettings);
            }
        }

        private static (Entity, Entity) FindCellsForEdge(EntityManager em, VoronoiEdge edge, in NativeArray<Entity> cellEntities)
        {
            Entity cellA = Entity.Null;
            Entity cellB = Entity.Null;

            for (int j = 0; j < cellEntities.Length; j++)
            {
                var cEntity = cellEntities[j];
                var cell = em.GetComponentData<VoronoiCell>(cEntity);
                if (cell.SiteIndex == edge.SiteA) cellA = cEntity;
                if (cell.SiteIndex == edge.SiteB) cellB = cEntity;
            }

            return (cellA, cellB);
        }

        private static void AddLevelData(EntityManager em, Entity entity, int level, LevelSettings levelSettings)
        {
            em.AddComponentData(entity, new DetailLevelData
            {
                Level = (DetailLevel)level,
                LODThreshold = levelSettings.LODThreshold,
                RenderThreshold = levelSettings.RenderThreshold
            });
        }
    }
}
