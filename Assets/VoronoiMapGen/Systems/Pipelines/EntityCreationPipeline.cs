using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
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
            // Простая защита от несовпадения длин массивов
            int count = math.min(sites.Length, cells.Length);
            if (sites.Length != cells.Length)
            {
                Debug.LogWarning($"Level {level}: Mismatch Sites({sites.Length}) vs Cells({cells.Length}). Truncating to {count}.");
            }

            // 1. Подготовка данных для полигонов (Геометрия)
            // Key: SiteIndex, Value: VertexPosition. 
            // Используем MultiHashMap, чтобы для одной ячейки собрать все её вершины из ребер.
            var polyMap = new NativeMultiHashMap<int, float2>(edges.Length * 2, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                // Добавляем вершины ребра к обоим сайтам, которые оно разделяет
                polyMap.Add(edge.SiteA, edge.VertexA);
                polyMap.Add(edge.SiteA, edge.VertexB);
                polyMap.Add(edge.SiteB, edge.VertexA);
                polyMap.Add(edge.SiteB, edge.VertexB);
            }

            // 2. Создание сущностей Ячеек (Cells)
            // Возвращаем карту [SiteIndex -> Entity], чтобы быстро связать ребра
            NativeArray<Entity> siteToCellEntityMap = CreateCellEntities(em, level, levelSettings, cells, siteMetadata, polyMap, count);

            // 3. Создание сущностей Сайтов (Sites) - чисто информативно/для дебага
            // Можно отключить, если сайтов слишком много
            CreateSiteEntities(em, level, levelSettings, sites, siteMetadata, count);

            // 4. Создание сущностей Ребер (Edges)
            CreateEdgeEntities(em, level, levelSettings, edges, siteToCellEntityMap);

            // Очистка
            polyMap.Dispose();
            siteToCellEntityMap.Dispose();
            
            // Завершаем джобы, вызванные EntityManager-ом, чтобы данные были готовы
            em.CompleteAllTrackedJobs();
        }

        private static NativeArray<Entity> CreateCellEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeList<VoronoiCell> cells,
            in NativeArray<VoronoiSite> siteMeta,
            NativeMultiHashMap<int, float2> polyMap,
            int count)
        {
            // Создаем массив маппинга: Индекс сайта -> Сущность ячейки
            // Предполагаем, что SiteIndex не превышает count (обычно так и есть, SiteIndex идет от 0 до N)
            // Если SiteIndex'ы рваные, нужен NativeHashMap, но для Вороного обычно Array быстрее.
            int maxIndex = 0;
            for(int i=0; i<count; i++) maxIndex = math.max(maxIndex, cells[i].SiteIndex);
            var lookupMap = new NativeArray<Entity>(maxIndex + 1, Allocator.Temp);

            // Архетип для ячейки со всем необходимым для рендеринга
            var cellArchetype = em.CreateArchetype(
                typeof(VoronoiCell),
                typeof(VoronoiSite),
                typeof(DetailLevelData),
                typeof(LocalTransform),
                typeof(LocalToWorld),
                typeof(CellBiome),         // Нужно для цвета
                typeof(CellPolygonVertex), // Буфер вершин (для меша)
                typeof(CellTriIndex),      // Буфер индексов (для меша)
                typeof(GeometryBuiltTag)   // Флаг готовности
            );

            var entities = new NativeArray<Entity>(count, Allocator.Temp);
            em.CreateEntity(cellArchetype, entities);

            for (int i = 0; i < count; i++)
            {
                var cell = cells[i];
                var meta = siteMeta[cell.SiteIndex];
                var e = entities[i];

                // Заполняем компоненты
                em.SetComponentData(e, cell);
                em.SetComponentData(e, meta);
                em.SetComponentData(e, new DetailLevelData
                {
                    Level = (DetailLevel)level,
                    LODThreshold = levelSettings.LODThreshold,
                    RenderThreshold = levelSettings.RenderThreshold
                });
                em.SetComponentData(e, LocalTransform.FromPosition(meta.Position.x, 0, meta.Position.y));
                
                // Заглушка биома (пока нет генератора биомов)
                em.SetComponentData(e, new CellBiome { Type = BiomeType.Grassland });

                // === ГЕНЕРАЦИЯ ПОЛИГОНА ===
                BuildPolygonForCell(em, e, cell, polyMap);

                // Сохраняем в lookup map для ребер
                if (cell.SiteIndex < lookupMap.Length)
                {
                    lookupMap[cell.SiteIndex] = e;
                }
            }

            entities.Dispose();
            return lookupMap;
        }

        private static void BuildPolygonForCell(EntityManager em, Entity e, VoronoiCell cell, NativeMultiHashMap<int, float2> polyMap)
        {
            if (polyMap.TryGetFirstValue(cell.SiteIndex, out float2 v, out var it))
            {
                // 1. Собираем уникальные вершины
                var uniqueVerts = new NativeList<float2>(16, Allocator.Temp);
                do
                {
                    bool exists = false;
                    for (int k = 0; k < uniqueVerts.Length; k++)
                    {
                        if (math.distance(uniqueVerts[k], v) < 0.01f) 
                        { 
                            exists = true; 
                            break; 
                        }
                    }
                    if (!exists) uniqueVerts.Add(v);
                } 
                while (polyMap.TryGetNextValue(out v, ref it));

                // 2. Сортируем CCW (Против часовой стрелки)
                SortVerticesCCW(uniqueVerts, cell.Centroid);

                // 3. Заполняем буферы
                var vertBuffer = em.GetBuffer<CellPolygonVertex>(e);
                var triBuffer = em.GetBuffer<CellTriIndex>(e);

                for (int k = 0; k < uniqueVerts.Length; k++)
                {
                    vertBuffer.Add(new CellPolygonVertex { Value = new float3(uniqueVerts[k].x, 0, uniqueVerts[k].y) });
                }

                // Триангуляция (Triangle Fan)
                if (uniqueVerts.Length >= 3)
                {
                    for (int k = 1; k < uniqueVerts.Length - 1; k++)
                    {
                        triBuffer.Add(new CellTriIndex { Value = 0 });
                        triBuffer.Add(new CellTriIndex { Value = k });
                        triBuffer.Add(new CellTriIndex { Value = k + 1 });
                    }
                }
                uniqueVerts.Dispose();
            }
        }

        private static void SortVerticesCCW(NativeList<float2> verts, float2 center)
        {
            // Сортировка вставками (для малых массивов < 20 элементов быстрее QuickSort)
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

            var entities = new NativeArray<Entity>(edges.Length, Allocator.Temp);
            em.CreateEntity(edgeArchetype, entities);

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                var e = entities[i];

                // Быстрый поиск соседей через массив (O(1))
                Entity cellA = (edge.SiteA >= 0 && edge.SiteA < siteToCellMap.Length) ? siteToCellMap[edge.SiteA] : Entity.Null;
                Entity cellB = (edge.SiteB >= 0 && edge.SiteB < siteToCellMap.Length) ? siteToCellMap[edge.SiteB] : Entity.Null;

                edge.CellA = cellA;
                edge.CellB = cellB;

                em.SetComponentData(e, edge);
                em.SetComponentData(e, new DetailLevelData
                {
                    Level = (DetailLevel)level,
                    LODThreshold = levelSettings.LODThreshold,
                    RenderThreshold = levelSettings.RenderThreshold
                });
                
                // Если нужны теги для дорог
                if (level >= 4) em.AddComponent<RoadEntityTag>(e);
                else em.AddComponent<BorderEntityTag>(e);
            }
            entities.Dispose();
        }

        private static void CreateSiteEntities(
            EntityManager em,
            int level,
            LevelSettings levelSettings,
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            int count)
        {
            // Опционально: создаем точки для дебага
            // Если точек тысячи, лучше не создавать лишние сущности, если они не нужны для геймплея
            // Оставляю как было у тебя, но с ограничением по count
            var arr = new NativeArray<Entity>(count, Allocator.Temp);
            em.CreateEntity(em.CreateArchetype(typeof(VoronoiSite), typeof(VoronoiSitePosition), typeof(DetailLevelData)), arr);
            
            for (int i = 0; i < count; i++)
            {
                em.SetComponentData(arr[i], siteMetadata[i]);
                em.SetComponentData(arr[i], new VoronoiSitePosition { Value = sites[i] });
                em.SetComponentData(arr[i], new DetailLevelData { Level = (DetailLevel)level });
            }
            arr.Dispose();
        }
    }
}