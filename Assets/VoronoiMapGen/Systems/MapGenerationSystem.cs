using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private EntityQuery _generationQuery;

        protected override void OnCreate()
        {
            _generationQuery = GetEntityQuery(ComponentType.ReadOnly<MapGenerationRequest>());
        }

        protected override void OnUpdate()
        {
            var requests = _generationQuery.ToComponentDataArray<MapGenerationRequest>(Allocator.Temp);
            
            if (requests.Length == 0)
            {
                return;
            }

            var request = requests[0];
            requests.Dispose();

            if (request.IsGenerated)
                return;

            // Гарантируем ненулевой сид
            int seed = request.Seed;
            if (seed == 0) seed = 1;

            Debug.Log($"Starting map generation with seed: {seed}");

            // Создаем сущности для сайтов
            var siteEntities = CollectionHelper.CreateNativeArray<Entity>(request.SiteCount, Allocator.Temp);
            for (int i = 0; i < request.SiteCount; i++)
            {
                siteEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<Components.VoronoiSite>(siteEntities[i]);
            }

            // Генерируем сайты - используем TempJob для джобов
            var sites = new NativeArray<float2>(request.SiteCount, Allocator.TempJob);
            var siteJob = new SiteGenerationJob
            {
                Sites = sites,
                Seed = seed, // Используем гарантированный ненулевой сид
                MapSize = request.MapSize
            };
            siteJob.Schedule(request.SiteCount, 64).Complete();

            // Заполняем компоненты сайтов
            for (int i = 0; i < siteEntities.Length; i++)
            {
                EntityManager.SetComponentData(siteEntities[i], new VoronoiSite
                {
                    Position = sites[i],
                    Index = i
                });
            }

            // Триангуляция Делоне
            var triangles = new NativeList<Components.DelaunayTriangle>(request.SiteCount * 2, Allocator.TempJob);
            var triangulationJob = new DelaunayTriangulationJob
            {
                Sites = sites,
                Triangles = triangles,
                Edges = new NativeList<int3>(request.SiteCount * 3, Allocator.TempJob)
            };
            triangulationJob.Run();

            Debug.Log($"Generated {triangles.Length} triangles");

            // Построение диаграммы Вороного
            var edges = new NativeList<VoronoiEdge>(request.SiteCount * 3, Allocator.TempJob);
            var cells = new NativeList<VoronoiCell>(request.SiteCount, Allocator.TempJob);
            
            var voronoiJob = new VoronoiConstructionJob
            {
                Triangles = triangles.AsArray(),
                Sites = sites,
                Edges = edges,
                Cells = cells
            };
            voronoiJob.Run();

            Debug.Log($"Generated {edges.Length} Voronoi edges");

            // Создаем сущности для ячеек
            var cellEntities = CollectionHelper.CreateNativeArray<Entity>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++)
            {
                cellEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<Components.VoronoiCell>(cellEntities[i]);
            }

            for (int i = 0; i < cells.Length; i++)
            {
                EntityManager.SetComponentData(cellEntities[i], cells[i]);
            }

            // Создаем сущности для ребер
            var edgeEntities = CollectionHelper.CreateNativeArray<Entity>(edges.Length, Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                edgeEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<Components.VoronoiEdge>(edgeEntities[i]);
            }

            for (int i = 0; i < edges.Length; i++)
            {
                EntityManager.SetComponentData(edgeEntities[i], edges[i]);
            }

            // Генерация биомов - используем TempJob для джобов
            var biomes = new NativeArray<CellBiome>(request.SiteCount, Allocator.TempJob);
            var biomeJob = new BiomeAssignmentJob
            {
                Cells = cells.AsArray(),
                Sites = sites,
                Biomes = biomes,
                MapCenter = request.MapSize * 0.5f,
                MapRadius = math.length(request.MapSize) * 0.5f
            };
            biomeJob.Schedule(request.SiteCount, 64).Complete();

            // Добавляем компоненты биомов к ячейкам
            for (int i = 0; i < cellEntities.Length && i < biomes.Length; i++)
            {
                EntityManager.AddComponentData(cellEntities[i], biomes[i]);
            }

            // Добавляем тег завершения
            var mapEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(mapEntity, new MapGeneratedTag());

            // Обновляем запрос
            var requestEntity = _generationQuery.GetSingletonEntity();
            EntityManager.SetComponentData(requestEntity, new MapGenerationRequest
            {
                Seed = seed, // Сохраняем гарантированный сид
                SiteCount = request.SiteCount,
                MapSize = request.MapSize,
                IsGenerated = true
            });

            Debug.Log($"Map Statistics:");
            Debug.Log($"  Sites: {sites.Length}");
            Debug.Log($"  Triangles: {triangles.Length}");
            Debug.Log($"  Voronoi Edges: {edges.Length}");
            Debug.Log($"  Voronoi Cells: {cells.Length}");

            // Создаем текстовый отчет (без NativeHashMap)
            string report = $"=== MAP GENERATION REPORT ===\n";
            report += $"Seed: {seed}\n";
            report += $"Sites: {sites.Length}\n";
            report += $"Triangles: {triangles.Length}\n";
            report += $"Voronoi Edges: {edges.Length}\n";
            report += $"Voronoi Cells: {cells.Length}\n";
            report += $"Map Size: {request.MapSize.x} x {request.MapSize.y}\n";

            // Простая статистика биомов через массивы
            var biomeCounts = new NativeArray<int>((int)BiomeType.Snow + 1, Allocator.Temp);
            for (int i = 0; i < biomes.Length; i++)
            {
                var biome = biomes[i];
                int biomeIndex = (int)biome.Type;
                if (biomeIndex >= 0 && biomeIndex < biomeCounts.Length)
                {
                    biomeCounts[biomeIndex] = biomeCounts[biomeIndex] + 1;
                }
            }

            string[] biomeNames = { "Ocean", "Coast", "Ice", "Desert", "Grassland", "Forest", "Mountain", "Snow" };
            for (int i = 0; i < biomeCounts.Length && i < biomeNames.Length; i++)
            {
                if (biomeCounts[i] > 0)
                {
                    report += $"{biomeNames[i]}: {biomeCounts[i]}\n";
                }
            }
            biomeCounts.Dispose();

            Debug.Log(report);

            // Освобождаем ресурсы
            siteEntities.Dispose();
            sites.Dispose();
            triangles.Dispose();
            edges.Dispose();
            cells.Dispose();
            cellEntities.Dispose();
            edgeEntities.Dispose();
            biomes.Dispose();

            Debug.Log("Map generation completed!");
        }
    }
}