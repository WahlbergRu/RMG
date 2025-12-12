using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Civilization.Components; // Для SettlementData
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

            // Карта родителей (Index -> Entity) для связывания
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
            // 2. АРХЕТИП СУЩНОСТИ ЯЧЕЙКИ
            // ------------------------------------------------------------
            // Здесь мы добавляем ВСЕ компоненты, включая новые (Civilization & Zoning)
            EntityArchetype cellArchetype = em.CreateArchetype(
                // База
                typeof(VoronoiCell), typeof(VoronoiSite), typeof(DetailLevelData),
                typeof(LocalTransform), typeof(LocalToWorld),
                typeof(CellPolygonVertex), typeof(CellTriIndex),
                // Природа
                typeof(TectonicPlateData), typeof(ClimateData), typeof(BiomeData),
                typeof(CellBiome), typeof(HydrologyData), typeof(CellNeighbor),
                
                // --- ЦИВИЛИЗАЦИЯ И НАСЕЛЕНИЕ (L2+) ---
                typeof(DemographicsData),
                typeof(SettlementData),
                typeof(CalcDemographicsTag), // Тэг для пересчета демографии
                
                // --- ЗОНИРОВАНИЕ РАЙОНОВ (L3+) ---
                typeof(DistrictData) 
            );

            // Создаем ВСЕ сущности разом
            NativeArray<Entity> createdEntities = em.CreateEntity(cellArchetype, count, Allocator.Temp);

            // Кэш для ребер
            NativeArray<Entity> entityLookup = new NativeArray<Entity>(count, Allocator.Temp);
            createdEntities.CopyTo(entityLookup);

            // ------------------------------------------------------------
            // 3. ЗАПОЛНЕНИЕ ДАННЫМИ
            // ------------------------------------------------------------
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

                // --- ЗАПОЛНЕНИЕ НОВЫХ ДАННЫХ ---

                // 1. Поселения (Settlements)
                if (data.Settlements.IsCreated)
                {
                    em.SetComponentData(e, data.Settlements[i]);
                }
                else
                {
                    // Если данных нет (например на L0), ставим дефолт
                    em.SetComponentData(e, new SettlementData 
                    { 
                        Type = SettlementType.Wilderness, 
                        MetropolisIndex = -1 
                    });
                }

                // 2. Районы (Districts)
                if (data.Districts.IsCreated)
                {
                    em.SetComponentData(e, data.Districts[i]);
                }
                else
                {
                    // Если данных нет (например на L2 или в лесу), это просто "Парк" без застройки
                    em.SetComponentData(e, new DistrictData 
                    { 
                        Type = DistrictType.Park, 
                        BuildingDensity = 0 
                    });
                }

                // 3. Демография (пустая при создании, заполнится системой симуляции)
                em.SetComponentData(e, new DemographicsData());

                // ------------------------------

                // CellBiome (для рендера цвета)
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
            // 4. СОЗДАНИЕ РЕБЕР
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
            NativeList<VoronoiEdge> validEdges = new NativeList<VoronoiEdge>(edges.Length, Allocator.Temp);

            for (int i = 0; i < edges.Length; i++)
            {
                VoronoiEdge edge = edges[i];
                if (math.lengthsq(edge.VertexA) > 0.001f)
                    if (edge.SiteA >= 0 && edge.SiteA < cellLookup.Length &&
                        (edge.SiteB == -1 || (edge.SiteB >= 0 && edge.SiteB < cellLookup.Length)))
                        validEdges.Add(edge);
            }

            if (validEdges.Length == 0) { validEdges.Dispose(); return; }

            // Если уровень L3 и выше - это уже могут быть городские дороги
            // Но логику дорог пока оставляем простой: Border или Road tag
            EntityArchetype edgeArchetype;
            if (level >= 4)
                edgeArchetype = em.CreateArchetype(typeof(VoronoiEdge), typeof(DetailLevelData), typeof(LocalToWorld), typeof(RoadEntityTag));
            else
                edgeArchetype = em.CreateArchetype(typeof(VoronoiEdge), typeof(DetailLevelData), typeof(LocalToWorld), typeof(BorderEntityTag));

            NativeArray<Entity> edgeEntities = em.CreateEntity(edgeArchetype, validEdges.Length, Allocator.Temp);

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