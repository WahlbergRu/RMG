using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems
{
    public static class EntityCreationPipeline
    {
        public static void CreateEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            float2 mapSize,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            in NativeList<VoronoiCell> cells,
            in NativeList<VoronoiEdge> edges)
        {
            int count = math.min(sites.Length, cells.Length);
            
            var polyMap = new NativeParallelMultiHashMap<int, float2>(edges.Length * 2, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                polyMap.Add(edge.SiteA, edge.VertexA);
                polyMap.Add(edge.SiteA, edge.VertexB);
                polyMap.Add(edge.SiteB, edge.VertexA);
                polyMap.Add(edge.SiteB, edge.VertexB);
            }

            NativeArray<Entity> siteToCellEntityMap = CreateCellEntities(em, level, levelSettings, mapSize, cells, siteMetadata, polyMap, count);
            
            // Ребра создаем опционально (можно отключить для скорости)
            CreateEdgeEntities(em, level, levelSettings, edges, siteToCellEntityMap);

            polyMap.Dispose();
            siteToCellEntityMap.Dispose();
            
            em.CompleteAllTrackedJobs();
        }

        private static NativeArray<Entity> CreateCellEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            float2 mapSize,
            in NativeList<VoronoiCell> cells,
            in NativeArray<VoronoiSite> siteMeta,
            NativeParallelMultiHashMap<int, float2> polyMap,
            int count)
        {
            int maxIndex = 0;
            for(int i=0; i<count; i++) maxIndex = math.max(maxIndex, cells[i].SiteIndex);
            var lookupMap = new NativeArray<Entity>(maxIndex + 1, Allocator.Temp);

            var cellArchetype = em.CreateArchetype(
                typeof(VoronoiCell),
                typeof(VoronoiSite),
                typeof(DetailLevelData),
                typeof(LocalTransform),
                typeof(LocalToWorld),
                typeof(CellBiome),         
                typeof(CellPolygonVertex), 
                typeof(CellTriIndex)
            );

            for (int i = 0; i < count; i++)
            {
                var cell = cells[i];
                var meta = siteMeta[cell.SiteIndex];

                // === ИСПРАВЛЕНИЕ: БОЛЬШЕ НЕ ПРОПУСКАЕМ ПРИЗРАКОВ ===
                // Раньше мы делали: if (meta.Value < -0.5f) continue;
                // Теперь мы разрешаем их создание, чтобы они заполнили пустоты по краям.
                // Обрезка (Clipper) сделает их ровными.
                // ====================================================

                var e = em.CreateEntity(cellArchetype);

                em.SetComponentData(e, cell);
                em.SetComponentData(e, meta);
                em.SetComponentData(e, new DetailLevelData
                {
                    Level = (DetailLevel)level,
                    LODThreshold = levelSettings.LODThreshold,
                    RenderThreshold = levelSettings.RenderThreshold
                });
                em.SetComponentData(e, LocalTransform.FromPosition(meta.Position.x, 0, meta.Position.y));
                em.SetComponentData(e, new CellBiome { Type = BiomeType.Grassland });

                BuildPolygonForCell(em, e, cell, polyMap, mapSize);

                if (cell.SiteIndex < lookupMap.Length) lookupMap[cell.SiteIndex] = e;
            }

            return lookupMap;
        }

        private static void BuildPolygonForCell(
            EntityManager em, 
            Entity e, 
            VoronoiCell cell, 
            NativeParallelMultiHashMap<int, float2> polyMap,
            float2 mapSize)
        {
            if (polyMap.TryGetFirstValue(cell.SiteIndex, out float2 v, out var it))
            {
                var uniqueVerts = new NativeList<float2>(16, Allocator.Temp);
                do
                {
                    bool exists = false;
                    for (int k = 0; k < uniqueVerts.Length; k++)
                    {
                        if (math.distance(uniqueVerts[k], v) < 0.01f) { exists = true; break; }
                    }
                    if (!exists) uniqueVerts.Add(v);
                } 
                while (polyMap.TryGetNextValue(out v, ref it));

                // === ОБРЕЗКА (Это делает карту квадратной) ===
                PolygonClipper.ClipPolygonToMapBounds(ref uniqueVerts, mapSize);
                // =============================================

                SortVerticesCCW(uniqueVerts, cell.Centroid);

                var vertBuffer = em.GetBuffer<CellPolygonVertex>(e);
                var triBuffer = em.GetBuffer<CellTriIndex>(e);

                for (int k = 0; k < uniqueVerts.Length; k++)
                {
                    vertBuffer.Add(new CellPolygonVertex { Value = new float3(uniqueVerts[k].x, 0, uniqueVerts[k].y) });
                }

                if (uniqueVerts.Length >= 3)
                {
                    for (int k = 1; k < uniqueVerts.Length - 1; k++)
                    {
                        triBuffer.Add(new CellTriIndex { Value = 0 });
                        triBuffer.Add(new CellTriIndex { Value = k + 1 });
                        triBuffer.Add(new CellTriIndex { Value = k });
                    }
                }
                uniqueVerts.Dispose();
            }
        }

        private static void SortVerticesCCW(NativeList<float2> verts, float2 center)
        {
            for (int i = 0; i < verts.Length - 1; i++)
            {
                for (int j = i + 1; j < verts.Length; j++)
                {
                    float angleA = math.atan2(verts[i].y - center.y, verts[i].x - center.x);
                    float angleB = math.atan2(verts[j].y - center.y, verts[j].x - center.x);
                    if (angleA > angleB)
                    {
                        var temp = verts[i];
                        verts[i] = verts[j];
                        verts[j] = temp;
                    }
                }
            }
        }

        private static void CreateEdgeEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeList<VoronoiEdge> edges,
            NativeArray<Entity> siteToCellMap)
        {
            var edgeArchetype = em.CreateArchetype(
                typeof(VoronoiEdge),
                typeof(DetailLevelData),
                typeof(LocalToWorld)
            );

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                Entity cellA = (edge.SiteA >= 0 && edge.SiteA < siteToCellMap.Length) ? siteToCellMap[edge.SiteA] : Entity.Null;
                Entity cellB = (edge.SiteB >= 0 && edge.SiteB < siteToCellMap.Length) ? siteToCellMap[edge.SiteB] : Entity.Null;

                if (cellA == Entity.Null && cellB == Entity.Null) continue;

                var e = em.CreateEntity(edgeArchetype);
                edge.CellA = cellA;
                edge.CellB = cellB;

                em.SetComponentData(e, edge);
                em.SetComponentData(e, new DetailLevelData { Level = (DetailLevel)level });
                
                if (level >= 4) em.AddComponent<RoadEntityTag>(e);
                else em.AddComponent<BorderEntityTag>(e);
            }
        }
    }
}