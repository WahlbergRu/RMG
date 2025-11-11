using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Пайплайн генерации уровней карты:
    /// сайты → триангуляция Делоне → диаграмма Вороного → ECS сущности.
    /// </summary>
    public static class LevelGenerationPipeline
    {
        public static void GenerateLevels(
            EntityManager em,
            MapSettings mapSettings,
            NativeArray<LevelSettings> levels)
        {
            NativeArray<VoronoiCell> parentCells = default;

            for (int level = 0; level < levels.Length; level++)
            {
                LevelSettings levelSettings = levels[level];
                Debug.Log($"[Level {level}] Generating level with SiteCount={levelSettings.SiteCount}");

                // === 1. Генерация сайтов ===
                NativeArray<float2> sites;
                NativeArray<VoronoiSite> siteMetadata;
                (sites, siteMetadata) = SiteGenerator.Generate(mapSettings, levels, levelSettings, level, parentCells);
                Debug.Log($"[Level {level}] Sites generated: {sites.Length}");

                // === 2. Триангуляция Делоне ===
                NativeList<DelaunayTriangle> triangles = new NativeList<DelaunayTriangle>(Allocator.TempJob);
                NativeList<int3> edges = new NativeList<int3>(Allocator.TempJob);

                DelaunayTriangulationJob delaunayJob = new DelaunayTriangulationJob
                {
                    Sites = sites,
                    SiteMetadata = siteMetadata,
                    Level = level,
                    Triangles = triangles,
                    Edges = edges
                };

                JobHandle delaunayHandle = delaunayJob.Schedule(default);
                delaunayHandle.Complete();
                Debug.Log($"[Level {level}] Triangles: {triangles.Length}, Edges: {edges.Length}");

                // === 3. Построение диаграммы Вороного ===
                NativeList<VoronoiCell> voronoiCells = new NativeList<VoronoiCell>(Allocator.TempJob);
                NativeList<VoronoiEdge> voronoiEdges = new NativeList<VoronoiEdge>(Allocator.TempJob);

                VoronoiConstructionJob voronoiJob = new VoronoiConstructionJob
                {
                    Triangles = triangles.AsArray(),
                    Sites = sites,
                    SiteMetadata = siteMetadata,
                    Level = level,
                    Cells = voronoiCells,
                    Edges = voronoiEdges
                };

                JobHandle voronoiHandle = voronoiJob.Schedule(default);
                voronoiHandle.Complete();
                Debug.Log($"[Level {level}] Voronoi cells: {voronoiCells.Length}, edges: {voronoiEdges.Length}");
                
                // === 4. Создание ECS сущностей ===
                EntityCreationPipeline.CreateEntities(
                    em,
                    level,
                    levelSettings,
                    sites,
                    siteMetadata,
                    voronoiCells,
                    voronoiEdges);
                Debug.Log($"[Level {level}] ECS entities created");

                // === 5. Копируем данные для parentCells следующего уровня ===
                if (parentCells.IsCreated)
                {
                    parentCells.Dispose();
                }

                parentCells = new NativeArray<VoronoiCell>(voronoiCells.Length, Allocator.TempJob);
                for (int i = 0; i < voronoiCells.Length; i++)
                {
                    parentCells[i] = voronoiCells[i];
                }

                // === 6. Освобождение временных буферов ===
                sites.Dispose();
                siteMetadata.Dispose();
                triangles.Dispose();
                edges.Dispose();
                voronoiEdges.Dispose();
                voronoiCells.Dispose();

                Debug.Log($"[Level {level}] Temporary buffers disposed");
            }

            // Освобождаем parentCells после завершения всех уровней
            if (parentCells.IsCreated)
            {
                parentCells.Dispose();
            }
        }
    }
}