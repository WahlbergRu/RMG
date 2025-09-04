using Unity.Collections;
using Unity.Entities;
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
        /// <summary>
        /// Запускаем генерацию всех уровней карты.
        /// </summary>
        public static void GenerateLevels(EntityManager em, MapSettings mapSettings,  NativeArray<LevelSettings> levels) {
            for (int level = 0; level < levels.Length; level++)
            {
                var levelSettings = levels[level];
                Debug.Log(levelSettings.SiteCount);

                // === 1. Генерация сайтов ===
                (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) = SiteGenerator.Generate(
                    mapSettings,
                    levels, // передаём весь массив настроек
                    levelSettings,
                    level,
                    default // parentCells пока не используем
                );

                // === 2. Триангуляция Делоне ===
                var triangles = new NativeList<DelaunayTriangle>(Allocator.TempJob);
                var edges = new NativeList<int3>(Allocator.TempJob);

                var delaunayJob = new DelaunayTriangulationJob
                {
                    Sites = sites,
                    SiteMetadata = siteMetadata,
                    Level = level,
                    Triangles = triangles,
                    Edges = edges
                };
                delaunayJob.Execute();

                // === 3. Построение диаграммы Вороного ===
                var voronoiEdges = new NativeList<VoronoiEdge>(Allocator.TempJob);
                var voronoiCells = new NativeList<VoronoiCell>(Allocator.TempJob);

                var voronoiJob = new VoronoiConstructionJob
                {
                    Triangles = triangles.AsArray(),
                    Sites = sites,
                    SiteMetadata = siteMetadata,
                    Level = level,
                    Edges = voronoiEdges,
                    Cells = voronoiCells
                };
                voronoiJob.Execute();

                // === 4. Создание ECS сущностей ===
                EntityCreationPipeline.CreateEntities(
                    em,
                    level,
                    levelSettings,          // передаём именно struct
                    sites,
                    siteMetadata,
                    voronoiCells,
                    voronoiEdges
                );

                // === 5. Dispose временных буферов ===
                sites.Dispose();
                siteMetadata.Dispose();
                triangles.Dispose();
                edges.Dispose();
                voronoiEdges.Dispose();
                voronoiCells.Dispose();
            }
        }

    }
}
