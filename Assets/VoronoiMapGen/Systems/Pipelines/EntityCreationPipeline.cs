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
            // Данные симуляции
            in NativeArray<TectonicPlateData> tectonicData,
            in NativeArray<ClimateData> climateData,
            in NativeArray<BiomeData> biomeData,
            in NativeArray<HydrologyData> hydrologyData, 
            // Геометрия
            in NativeList<VoronoiCell> cells,
            in NativeList<VoronoiEdge> edges)
        {
            int count = math.min(sites.Length, cells.Length);
            
            // 1. Подготовка полигонов
            // Фильтруем ребра, чтобы оставить только те, что имеют геометрию
            var polyMap = new NativeParallelMultiHashMap<int, float2>(edges.Length * 2, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                if (math.lengthsq(edge.VertexA) < 0.001f && math.lengthsq(edge.VertexB) < 0.001f) continue;

                polyMap.Add(edge.SiteA, edge.VertexA);
                polyMap.Add(edge.SiteA, edge.VertexB);
                
                if (edge.SiteB != -1) 
                {
                    polyMap.Add(edge.SiteB, edge.VertexA);
                    polyMap.Add(edge.SiteB, edge.VertexB);
                }
            }

            // 2. Карта Родителей (для иерархии)
            NativeParallelHashMap<int, Entity> parentIndexToEntity = default;
            if (level > 0)
            {
                parentIndexToEntity = new NativeParallelHashMap<int, Entity>(count * 2, Allocator.Temp);
                var query = em.CreateEntityQuery(typeof(VoronoiSite), typeof(VoronoiCell));
                var existingEntities = query.ToEntityArray(Allocator.Temp);
                var existingSites = query.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

                for (int k = 0; k < existingEntities.Length; k++)
                {
                    if (existingSites[k].Level == level - 1)
                    {
                        parentIndexToEntity.TryAdd(existingSites[k].Index, existingEntities[k]);
                    }
                }
                existingEntities.Dispose();
                existingSites.Dispose();
            }

            // 3. СОЗДАНИЕ СУЩНОСТЕЙ ЯЧЕЕК
            NativeArray<Entity> siteToCellEntityMap = CreateCellEntities(
                em, level, levelSettings, mapSize, cells, siteMetadata, 
                tectonicData, climateData, biomeData, hydrologyData,
                polyMap, parentIndexToEntity, count
            );
            
            // 4. СОЗДАНИЕ СУЩНОСТЕЙ РЕБЕР
            CreateEdgeEntities(em, level, edges, siteToCellEntityMap);

            polyMap.Dispose();
            if (level > 0 && parentIndexToEntity.IsCreated) parentIndexToEntity.Dispose();
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
            in NativeArray<TectonicPlateData> tectonicData,
            in NativeArray<ClimateData> climateData,
            in NativeArray<BiomeData> biomeData,
            in NativeArray<HydrologyData> hydrologyData,
            NativeParallelMultiHashMap<int, float2> polyMap,
            NativeParallelHashMap<int, Entity> parentMap,
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
                typeof(CellPolygonVertex), 
                typeof(CellTriIndex),
                // Все компоненты данных
                typeof(TectonicPlateData),
                typeof(ClimateData),
                typeof(BiomeData),
                typeof(CellBiome),     // <-- ВАЖНО: Используется для рендера и высоты
                typeof(HydrologyData), 
                typeof(CellNeighbor)   
            );

            for (int i = 0; i < count; i++)
            {
                var cell = cells[i];
                int sIdx = cell.SiteIndex;
                if (sIdx >= siteMeta.Length) continue; 
                
                var meta = siteMeta[sIdx];

                Entity parentEnt = Entity.Null;
                if (level > 0 && parentMap.IsCreated)
                {
                    parentMap.TryGetValue(meta.ParentIndex, out parentEnt);
                }
                cell.ParentEntity = parentEnt;

                var e = em.CreateEntity(cellArchetype);

                // Базовые компоненты
                em.SetComponentData(e, cell);
                em.SetComponentData(e, meta);
                em.SetComponentData(e, new DetailLevelData
                {
                    Level = (DetailLevel)level,
                    LODThreshold = levelSettings.LODThreshold,
                    RenderThreshold = levelSettings.RenderThreshold,
                    ParentIndex = meta.ParentIndex
                });
                
                // Перенос данных симуляции
                // Проверяем индексы, так как массивы могут быть разной длины при ошибках, но здесь должны совпадать
                if (sIdx < tectonicData.Length) em.SetComponentData(e, tectonicData[sIdx]);
                if (sIdx < climateData.Length) em.SetComponentData(e, climateData[sIdx]);
                if (sIdx < biomeData.Length) em.SetComponentData(e, biomeData[sIdx]);
                if (sIdx < hydrologyData.Length) em.SetComponentData(e, hydrologyData[sIdx]);

                // *** КРИТИЧЕСКИЙ МОМЕНТ ***
                // Заполняем CellBiome, который читают системы генерации меша!
                if (sIdx < biomeData.Length && sIdx < climateData.Length && sIdx < tectonicData.Length)
                {
                    var bData = biomeData[sIdx];
                    var cData = climateData[sIdx];
                    var tData = tectonicData[sIdx];
                    
                    em.SetComponentData(e, new CellBiome
                    {
                        Type = bData.Type,
                        Temperature = cData.Temperature,
                        Moisture = cData.Moisture,
                        Elevation = tData.BaseHeight // <-- Вот высота, которая терялась!
                    });
                }
                else
                {
                    // Fallback, если данных нет (чтобы не было нулей)
                    em.SetComponentData(e, new CellBiome { Type = BiomeType.Ocean, Elevation = -0.5f });
                }

                // Геометрия (Начальная позиция 0,0,0, так как меш строится в локальных координатах)
                // Но LocalTransform должен соответствовать центроиду для правильного пивота
                em.SetComponentData(e, LocalTransform.FromPosition(meta.Position.x, 0, meta.Position.y));
                
                // Строим полигон (плоский, 3D построится в VoronoiGeometryBuildSystem)
                BuildPolygonForCell(em, e, cell, polyMap, mapSize);

                if (sIdx < lookupMap.Length) lookupMap[sIdx] = e;
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
            var uniqueVerts = new NativeList<float2>(16, Allocator.Temp);
            
            if (polyMap.TryGetFirstValue(cell.SiteIndex, out float2 v, out var it))
            {
                do
                {
                    bool exists = false;
                    for (int k = 0; k < uniqueVerts.Length; k++)
                    {
                        if (math.distancesq(uniqueVerts[k], v) < 0.0001f) { exists = true; break; }
                    }
                    if (!exists) uniqueVerts.Add(v);
                } 
                while (polyMap.TryGetNextValue(out v, ref it));
            }

            PolygonClipper.ClipToRect(ref uniqueVerts, mapSize);
            SortVerticesCCW(uniqueVerts, cell.Centroid);

            var vertBuffer = em.GetBuffer<CellPolygonVertex>(e);
            var triBuffer = em.GetBuffer<CellTriIndex>(e);

            vertBuffer.Clear();
            triBuffer.Clear();

            for (int k = 0; k < uniqueVerts.Length; k++)
            {
                // Записываем плоские данные (Y=0), высота добавится позже
                vertBuffer.Add(new CellPolygonVertex { Value = new float3(uniqueVerts[k].x, 0, uniqueVerts[k].y) });
            }
            
            // Простая триангуляция для начального состояния
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
                if (math.lengthsq(edge.VertexA) < 0.001f && math.lengthsq(edge.VertexB) < 0.001f) continue;

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