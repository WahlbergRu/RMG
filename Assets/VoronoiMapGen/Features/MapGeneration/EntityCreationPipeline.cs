using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Features
{
    public static class EntityCreationPipeline
    {
        public static void CreateEntities(
            EntityManager em,
            MapLevelData data,
            LevelSettings settings,
            float2 mapSize,
            NativeList<VoronoiEdge> edges
        )
        {
            int count = data.Length;
            int level = data.LevelIndex;

            // ------------------------------------------------------------
            // 1. ПОДГОТОВКА (Геометрия)
            // ------------------------------------------------------------
            NativeParallelMultiHashMap<int, float2> polyMap = new NativeParallelMultiHashMap<int, float2>(edges.Length * 2, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                VoronoiEdge edge = edges[i];
                if (math.lengthsq(edge.VertexA) < 0.001f) continue;
                polyMap.Add(edge.SiteA, edge.VertexA);
                polyMap.Add(edge.SiteA, edge.VertexB);
                if (edge.SiteB != -1)
                {
                    polyMap.Add(edge.SiteB, edge.VertexA);
                    polyMap.Add(edge.SiteB, edge.VertexB);
                }
            }

            // Карта родителей (Index -> Entity)
            NativeParallelHashMap<int, Entity> parentIndexToEntity = default;
            if (level > 0)
            {
                parentIndexToEntity = new NativeParallelHashMap<int, Entity>(count * 2, Allocator.Temp);
                EntityQuery q = em.CreateEntityQuery(typeof(VoronoiSite));
                NativeArray<Entity> ents = q.ToEntityArray(Allocator.Temp);
                NativeArray<VoronoiSite> sites = q.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

                for (int i = 0; i < ents.Length; i++)
                    if (sites[i].Level == level - 1)
                        parentIndexToEntity.TryAdd(sites[i].Index, ents[i]);

                ents.Dispose();
                sites.Dispose();
            }

            // ------------------------------------------------------------
            // 2. BATCH CREATION (МАССОВОЕ СОЗДАНИЕ ЯЧЕЕК)
            // ------------------------------------------------------------

            EntityArchetype cellArchetype = em.CreateArchetype(
                typeof(VoronoiCell), typeof(VoronoiSite), typeof(DetailLevelData),
                typeof(LocalTransform), typeof(LocalToWorld),
                typeof(CellPolygonVertex), typeof(CellTriIndex),
                typeof(TectonicPlateData), typeof(ClimateData), typeof(BiomeData),
                typeof(CellBiome), typeof(HydrologyData), typeof(CellNeighbor)
            );

            // Создаем ВСЕ сущности разом (1 Structural Change вместо 50,000)
            NativeArray<Entity> createdEntities = em.CreateEntity(cellArchetype, count, Allocator.Temp);

            // Кэш для ребер, чтобы они знали свои Entity
            // Используем NativeArray, так как индексы совпадают с data.Cells
            NativeArray<Entity> entityLookup = new NativeArray<Entity>(count, Allocator.Temp);
            createdEntities.CopyTo(entityLookup);

            // ------------------------------------------------------------
            // 3. ЗАПОЛНЕНИЕ ДАННЫМИ (В цикле)
            // ------------------------------------------------------------
            // Теперь просто пробегаем и устанавливаем значения.
            // Примечание: Для экстремальной оптимизации это можно вынести в IJobParallelFor
            // (используя EntityCommandBuffer.Parallel), но даже в MainThread это будет очень быстро.

            for (int i = 0; i < count; i++)
            {
                Entity e = createdEntities[i];
                VoronoiCell cell = data.Cells[i];
                VoronoiSite meta = data.Meta[i];

                // Связь с родителем
                if (parentIndexToEntity.IsCreated && parentIndexToEntity.TryGetValue(meta.ParentIndex, out Entity pEnt))
                    cell.ParentEntity = pEnt;

                // Основные компоненты
                em.SetComponentData(e, cell);
                em.SetComponentData(e, meta);
                em.SetComponentData(e, new DetailLevelData
                {
                    Level = (DetailLevel)level,
                    LODThreshold = settings.LODThreshold,
                    RenderThreshold = settings.RenderThreshold,
                    ParentIndex = meta.ParentIndex
                });

                // Данные симуляции
                em.SetComponentData(e, data.Tectonics[i]);
                em.SetComponentData(e, data.Climate[i]);
                em.SetComponentData(e, data.Hydrology[i]);
                em.SetComponentData(e, data.Biomes[i]);

                // CellBiome (для рендера)
                em.SetComponentData(e, new CellBiome
                {
                    Type = data.Biomes[i].Type,
                    Elevation = data.Tectonics[i].BaseHeight,
                    Temperature = data.Climate[i].Temperature,
                    Moisture = data.Climate[i].Moisture
                });

                // Transform
                em.SetComponentData(e, LocalTransform.FromPosition(meta.Position.x, 0, meta.Position.y));

                // Геометрия (Buffer)
                DynamicBuffer<CellPolygonVertex> vb = em.GetBuffer<CellPolygonVertex>(e);
                DynamicBuffer<CellTriIndex> tb = em.GetBuffer<CellTriIndex>(e);
                CellGeometryBuilder.BuildPolygonForCell(vb, tb, cell, polyMap, mapSize);
            }

            // ------------------------------------------------------------
            // 4. СОЗДАНИЕ РЕБЕР (ТОЖЕ BATCH)
            // ------------------------------------------------------------
            if (edges.Length > 0) CreateEdgeEntitiesBatch(em, level, edges, entityLookup);

            // Очистка
            createdEntities.Dispose();
            entityLookup.Dispose();
            polyMap.Dispose();
            if (parentIndexToEntity.IsCreated) parentIndexToEntity.Dispose();
        }

        private static void CreateEdgeEntitiesBatch(
            EntityManager em,
            int level,
            NativeList<VoronoiEdge> edges,
            NativeArray<Entity> cellLookup)
        {
            // Фильтруем ребра, у которых есть длина
            NativeList<VoronoiEdge> validEdges = new NativeList<VoronoiEdge>(edges.Length, Allocator.Temp);

            for (int i = 0; i < edges.Length; i++)
            {
                VoronoiEdge edge = edges[i];
                if (math.lengthsq(edge.VertexA) > 0.001f)
                    // Проверяем валидность индексов сразу
                    if (edge.SiteA >= 0 && edge.SiteA < cellLookup.Length &&
                        (edge.SiteB == -1 || (edge.SiteB >= 0 && edge.SiteB < cellLookup.Length)))
                        validEdges.Add(edge);
            }

            if (validEdges.Length == 0)
            {
                validEdges.Dispose();
                return;
            }

            // Пакетное создание сущностей ребер
            EntityArchetype edgeArchetype = em.CreateArchetype(
                typeof(VoronoiEdge),
                typeof(DetailLevelData),
                typeof(LocalToWorld)
            );

            // Если уровень высокий - это дороги, добавляем тег
            if (level >= 4)
                edgeArchetype = em.CreateArchetype(typeof(VoronoiEdge), typeof(DetailLevelData), typeof(LocalToWorld),
                    typeof(RoadEntityTag));
            else
                edgeArchetype = em.CreateArchetype(typeof(VoronoiEdge), typeof(DetailLevelData), typeof(LocalToWorld),
                    typeof(BorderEntityTag));

            NativeArray<Entity> edgeEntities = em.CreateEntity(edgeArchetype, validEdges.Length, Allocator.Temp);

            // Заполнение данных
            for (int i = 0; i < validEdges.Length; i++)
            {
                VoronoiEdge edge = validEdges[i];
                Entity e = edgeEntities[i];

                edge.CellA = cellLookup[edge.SiteA];
                edge.CellB = edge.SiteB >= 0 ? cellLookup[edge.SiteB] : Entity.Null;

                em.SetComponentData(e, edge);
                em.SetComponentData(e, new DetailLevelData { Level = (DetailLevel)level });
            }

            edgeEntities.Dispose();
            validEdges.Dispose();
        }
    }
}