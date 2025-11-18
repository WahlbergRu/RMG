using System;
using System.Collections.Generic;
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
    public static class LevelGenerationPipeline
    {
        // <<< ДОБАВЛЕНО: Поле для хранения siteMetadata каждого уровня >>>
        private static NativeArray<VoronoiSite>[] s_LevelSiteMetadata;

        public static void GenerateLevels(
            EntityManager em,
            MapSettings mapSettings,
            NativeArray<LevelSettings> levels)
        {
            NativeArray<VoronoiCell> parentCells = default;
            NativeArray<float2> parentSites = default;
            NativeArray<VoronoiSite> parentSiteMetadata = default; // <<< НОВОЕ

            // <<< ДОБАВЛЕНО: Инициализация s_LevelSiteMetadata >>>
            s_LevelSiteMetadata = new NativeArray<VoronoiSite>[levels.Length];

            for (int level = 0; level < levels.Length; level++)
            {
                LevelSettings levelSettings = levels[level];
                Debug.Log($"[Level {level}] Generating level with SiteCount={levelSettings.SiteCount}");

                // === 1. Генерация сайтов ===
                NativeArray<float2> sites;
                NativeArray<VoronoiSite> siteMetadata; // <<< ИЗМЕНЕНО имя переменной >>>
                (sites, siteMetadata) = SiteGenerator.Generate(mapSettings, levels, levelSettings, level, parentCells, parentSites, parentSiteMetadata); // <<< ПЕРЕДАЁМ parentSiteMetadata
                Debug.Log($"[Level {level}] Sites generated: {sites.Length}");

                // === 2. Триангуляция Делоне ===
                NativeList<DelaunayTriangle> triangles = new NativeList<DelaunayTriangle>(Allocator.TempJob);
                NativeList<int3> edges = new NativeList<int3>(Allocator.TempJob);

                DelaunayTriangulationJob delaunayJob = new DelaunayTriangulationJob
                {
                    Sites = sites,
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
                    siteMetadata, // <<< ИСПОЛЬЗУЕМ siteMetadata
                    voronoiCells,
                    voronoiEdges);
                Debug.Log($"[Level {level}] ECS entities created");

                // === 5. Копируем данные для следующего уровня ===
                if (parentCells.IsCreated)
                {
                    parentCells.Dispose();
                }
                if (parentSites.IsCreated)
                {
                    parentSites.Dispose();
                }
                // <<< НОВОЕ: Освобождаем parentSiteMetadata >>>
                if (parentSiteMetadata.IsCreated) // <<< НОВОЕ
                {                                 // <<< НОВОЕ
                    parentSiteMetadata.Dispose(); // <<< НОВОЕ
                }                                 // <<< НОВОЕ

                // --- ЗАПОЛНЯЕМ parentCells, parentSites И parentSiteMetadata ДЛЯ СЛЕДУЮЩЕГО УРОВНЯ ---
                parentCells = new NativeArray<VoronoiCell>(voronoiCells.Length, Allocator.Temp);
                for (int i = 0; i < voronoiCells.Length; i++)
                {
                    parentCells[i] = voronoiCells[i];
                }

                parentSites = new NativeArray<float2>(sites.Length, Allocator.Temp);
                for (int i = 0; i < sites.Length; i++)
                {
                    parentSites[i] = sites[i]; // <<< Сохраняем позиции точек текущего уровня
                }

                // <<< НОВОЕ: Заполняем parentSiteMetadata для следующего уровня >>>
                parentSiteMetadata = new NativeArray<VoronoiSite>(siteMetadata.Length, Allocator.Temp); // <<< НОВОЕ
                for (int i = 0; i < siteMetadata.Length; i++)                                         // <<< НОВОЕ
                {                                                                                     // <<< НОВОЕ
                    parentSiteMetadata[i] = siteMetadata[i];                                          // <<< НОВОЕ
                }                                                                                     // <<< НОВОЕ

                // <<< НОВОЕ: Сохраняем siteMetadata в s_LevelSiteMetadata >>>
                if (s_LevelSiteMetadata[level].IsCreated) // <<< НОВОЕ
                {                                         // <<< НОВОЕ
                    s_LevelSiteMetadata[level].Dispose(); // Освобождаем старый, если был (на всякий случай) // <<< НОВОЕ
                }                                         // <<< НОВОЕ
                s_LevelSiteMetadata[level] = siteMetadata; // <<< НОВОЕ

                // === 6. Освобождение временных буферов ===
                sites.Dispose();
                siteMetadata.Dispose(); // <<< ОСВОБОЖДАЕМ siteMetadata
                triangles.Dispose();
                edges.Dispose();
                voronoiEdges.Dispose();
                voronoiCells.Dispose();

                Debug.Log($"[Level {level}] Temporary buffers disposed");
            }

            // <<< НОВОЕ: Освобождаем s_LevelSiteMetadata >>>
            if (s_LevelSiteMetadata != null) // <<< НОВОЕ
            {                                // <<< НОВОЕ
                for (int i = 0; i < s_LevelSiteMetadata.Length; i++) // <<< НОВОЕ
                {                                                     // <<< НОВОЕ
                    if (s_LevelSiteMetadata[i].IsCreated)             // <<< НОВОЕ
                        s_LevelSiteMetadata[i].Dispose();             // <<< НОВОЕ
                }                                                     // <<< НОВОЕ
                s_LevelSiteMetadata = null;                           // <<< НОВОЕ
            }                                                         // <<< НОВОЕ

            // Освобождаем parentCells, parentSites и parentSiteMetadata после завершения всех уровней
            if (parentCells.IsCreated)
            {
                parentCells.Dispose();
            }
            if (parentSites.IsCreated)
            {
                parentSites.Dispose();
            }
            if (parentSiteMetadata.IsCreated) // <<< НОВОЕ
            {                                 // <<< НОВОЕ
                parentSiteMetadata.Dispose(); // <<< НОВОЕ
            }                                 // <<< НОВОЕ
        }
    }
}