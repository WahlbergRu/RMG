using System;
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
                requests.Dispose();
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

            // --- 1) Создаем сущности для сайтов ---
            var siteEntities = CollectionHelper.CreateNativeArray<Entity>(request.SiteCount, Allocator.Temp);
            for (int i = 0; i < request.SiteCount; i++)
            {
                siteEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<VoronoiSite>(siteEntities[i]);
            }

            // --- 2) Генерируем сайты (Job) ---
            var sites = new NativeArray<float2>(request.SiteCount, Allocator.TempJob);
            var siteJob = new SiteGenerationJob
            {
                Sites   = sites,
                Seed    = seed,
                MapSize = request.MapSize
            };

            var swSites = Stopwatch.StartNew();
            siteJob.Schedule(request.SiteCount, 64).Complete();
            swSites.Stop();
            Debug.Log($"SiteGenerationJob completed in {swSites.ElapsedMilliseconds} ms");

            // Заполняем компоненты сайтов
            for (int i = 0; i < siteEntities.Length; i++)
            {
                EntityManager.SetComponentData(siteEntities[i], new VoronoiSite
                {
                    Position = sites[i],
                    Index    = i
                });
            }

            // --- 3) Триангуляция Делоне ---
            var triangles = new NativeList<DelaunayTriangle>(request.SiteCount * 2, Allocator.TempJob);
            var triangulationJob = new DelaunayTriangulationJob
            {
                Sites     = sites,
                Triangles = triangles,
                Edges     = new NativeList<int3>(request.SiteCount * 3, Allocator.TempJob)
            };

            var swTri = Stopwatch.StartNew();
            triangulationJob.Run(); // синхронно, чтобы честно замерить
            swTri.Stop();
            Debug.Log($"DelaunayTriangulationJob completed in {swTri.ElapsedMilliseconds} ms");
            Debug.Log($"Generated {triangles.Length} triangles");

            // --- 4) Построение диаграммы Вороного ---
            var edges = new NativeList<VoronoiEdge>(request.SiteCount * 3, Allocator.TempJob);
            var cells = new NativeList<VoronoiCell>(request.SiteCount, Allocator.TempJob);

            var voronoiJob = new VoronoiConstructionJob
            {
                Triangles = triangles.AsArray(),
                Sites     = sites,
                Edges     = edges,
                Cells     = cells
            };

            var swVor = Stopwatch.StartNew();
            voronoiJob.Run();
            swVor.Stop();
            Debug.Log($"VoronoiConstructionJob completed in {swVor.ElapsedMilliseconds} ms");
            Debug.Log($"Generated {edges.Length} Voronoi edges");

            // --- 5) Создаем сущности для ячеек, записываем данные + высоты ---
            var cellEntities = CollectionHelper.CreateNativeArray<Entity>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++)
            {
                cellEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<VoronoiCell>(cellEntities[i]);
            }

            var swCellHeights = Stopwatch.StartNew();
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                EntityManager.SetComponentData(cellEntities[i], cell);

                // // Высота по центроиду
                // float height = SampleHeight(cell.Centroid, seed);
                // EntityManager.AddComponentData(cellEntities[i], new CellHeight { Value = height });
            }
            swCellHeights.Stop();

            // --- 6) Создаем сущности для рёбер, записываем данные + высоты ---
            var edgeEntities = CollectionHelper.CreateNativeArray<Entity>(edges.Length, Allocator.Temp);
            var swEdgeHeights = Stopwatch.StartNew();
            for (int i = 0; i < edges.Length; i++)
            {
                edgeEntities[i] = EntityManager.CreateEntity();
                EntityManager.AddComponent<VoronoiEdge>(edgeEntities[i]);
                EntityManager.SetComponentData(edgeEntities[i], edges[i]);
            }
            swEdgeHeights.Stop();

            // --- 7) Генерация биомов (Job) ---
            var biomes = new NativeArray<CellBiome>(request.SiteCount, Allocator.TempJob);
            var biomeJob = new BiomeAssignmentJob
            {
                Cells     = cells.AsArray(),
                Sites     = sites,
                Biomes    = biomes,
                MapCenter = request.MapSize * 0.5f,
                MapRadius = math.length(request.MapSize) * 0.5f
            };

            var swBiome = Stopwatch.StartNew();
            biomeJob.Schedule(request.SiteCount, 64).Complete();
            swBiome.Stop();
            Debug.Log($"BiomeAssignmentJob completed in {swBiome.ElapsedMilliseconds} ms");

            // Проставляем биомы ячейкам
            for (int i = 0; i < cellEntities.Length && i < biomes.Length; i++)
            {
                EntityManager.AddComponentData(cellEntities[i], biomes[i]);
            }

            // --- 8) Пометка о завершении генерации ---
            var mapEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(mapEntity, new MapGeneratedTag());

            // Обновляем запрос
            var requestEntity = _generationQuery.GetSingletonEntity();
            EntityManager.SetComponentData(requestEntity, new MapGenerationRequest
            {
                Seed        = seed,
                SiteCount   = request.SiteCount,
                MapSize     = request.MapSize,
                IsGenerated = true
            });

            // --- 9) Статистика и отчёт ---
            Debug.Log("Map Statistics:");
            Debug.Log($"  Sites: {sites.Length}");
            Debug.Log($"  Triangles: {triangles.Length}");
            Debug.Log($"  Voronoi Edges: {edges.Length}");
            Debug.Log($"  Voronoi Cells: {cells.Length}");

            string report = $"=== MAP GENERATION REPORT ===\n";
            report += $"Seed: {seed}\n";
            report += $"Sites: {sites.Length}\n";
            report += $"Triangles: {triangles.Length}\n";
            report += $"Voronoi Edges: {edges.Length}\n";
            report += $"Voronoi Cells: {cells.Length}\n";
            report += $"Map Size: {request.MapSize.x} x {request.MapSize.y}\n";
            report += $"Timings (ms): " +
                      $"SiteGen={swSites.ElapsedMilliseconds}, " +
                      $"Triangulation={swTri.ElapsedMilliseconds}, " +
                      $"VoronoiBuild={swVor.ElapsedMilliseconds}, " +
                      $"CellHeight={swCellHeights.ElapsedMilliseconds}, " +
                      $"EdgeHeight={swEdgeHeights.ElapsedMilliseconds}, " +
                      $"BiomeAssign={swBiome.ElapsedMilliseconds}\n";

            // Простая статистика биомов
            var biomeCounts = new NativeArray<int>((int)BiomeType.Snow + 1, Allocator.Temp);
            for (int i = 0; i < biomes.Length; i++)
            {
                int biomeIndex = (int)biomes[i].Type;
                if ((uint)biomeIndex < (uint)biomeCounts.Length)
                    biomeCounts[biomeIndex] = biomeCounts[biomeIndex] + 1;
            }

            string[] biomeNames = { "Ocean", "Coast", "Ice", "Desert", "Grassland", "Forest", "Mountain", "Snow" };
            for (int i = 0; i < biomeCounts.Length && i < biomeNames.Length; i++)
            {
                if (biomeCounts[i] > 0)
                    report += $"{biomeNames[i]}: {biomeCounts[i]}\n";
            }
            biomeCounts.Dispose();

            Debug.Log(report);

            // --- 10) Освобождаем ресурсы ---
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

        // ---- Helpers ----
    }
}
