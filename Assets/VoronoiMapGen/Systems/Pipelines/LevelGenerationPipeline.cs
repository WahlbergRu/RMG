using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;

namespace VoronoiMapGen.Systems
{
    public static class LevelGenerationPipeline
    {
        public static void Generate(
            EntityManager entityManager,
            int levelIndex,
            MapSettings mapSettings,
            LevelSettings levelSettings,
            NativeArray<VoronoiCell> parentCells,
            NativeArray<float2> parentSites,
            NativeArray<VoronoiSite> parentMeta,
            out NativeArray<float2> resultSites,
            out NativeArray<VoronoiSite> resultMeta,
            out NativeArray<VoronoiCell> resultCells)
        {
            // 1. Генерация точек (Sites)
            (var sites, var siteMeta) = SiteGenerator.Generate(
                mapSettings, 
                default, // Тут можно передать полный массив настроек если нужно, или заглушку
                levelSettings,
                levelIndex, 
                parentCells, 
                parentSites, 
                parentMeta
            );

            // 2. Цикл Релаксации (Lloyd)
            int iterations = levelSettings.RelaxationIterations;
            int totalPasses = math.max(1, iterations + 1);

            NativeList<DelaunayTriangle> triangles = default;
            NativeList<int3> delaunayEdges = default; // Временный список для джобы
            NativeList<VoronoiCell> voronoiCells = default;
            NativeList<VoronoiEdge> voronoiEdges = default;

            for (int pass = 0; pass < totalPasses; pass++)
            {
                bool isLastPass = (pass == totalPasses - 1);
                
                // Очистка памяти от предыдущего прохода
                if (triangles.IsCreated) triangles.Dispose();
                if (delaunayEdges.IsCreated) delaunayEdges.Dispose();
                if (voronoiCells.IsCreated) voronoiCells.Dispose();
                if (voronoiEdges.IsCreated) voronoiEdges.Dispose();

                // А. Триангуляция
                triangles = new NativeList<DelaunayTriangle>(Allocator.TempJob);
                delaunayEdges = new NativeList<int3>(Allocator.TempJob);

                new DelaunayTriangulationJob
                {
                    Sites = sites,
                    SiteMetadata = siteMeta,
                    Level = levelIndex,
                    Triangles = triangles,
                    Edges = delaunayEdges
                }.Schedule(default).Complete();

                // Б. Построение Вороного
                voronoiCells = new NativeList<VoronoiCell>(Allocator.TempJob);
                voronoiEdges = new NativeList<VoronoiEdge>(Allocator.TempJob);

                new VoronoiConstructionJob
                {
                    Triangles = triangles.AsArray(),
                    Sites = sites,
                    Level = levelIndex,
                    Cells = voronoiCells,
                    Edges = voronoiEdges
                }.Schedule(default).Complete();

                // В. Релаксация (двигаем точки к центру ячеек)
                if (!isLastPass)
                {
                    new LloydRelaxationJob
                    {
                        Cells = voronoiCells.AsArray(),
                        SiteMetadata = siteMeta,
                        MapSize = mapSettings.MapSize,
                        Sites = sites 
                    }.Schedule(default).Complete();
                }
            }

            // 3. Создание сущностей (Entities)
            // === ИСПРАВЛЕНИЕ ОШИБКИ CS7036 ===
            // Добавлен аргумент mapSettings.MapSize для обрезки полигонов
            EntityCreationPipeline.CreateEntities(
                entityManager,
                levelIndex,
                levelSettings,
                mapSettings.MapSize, // <--- ВОТ ЭТОГО НЕ ХВАТАЛО
                sites,
                siteMeta,
                voronoiCells,
                voronoiEdges
            );
            // ==================================

            // 4. Подготовка результатов для возврата (чтобы сохранить для след. уровня)
            resultSites = sites; // Передаем владение массивом наружу
            resultMeta = siteMeta; // Передаем владение
            
            // Копируем ячейки в Persistent массив для возврата
            resultCells = new NativeArray<VoronoiCell>(voronoiCells.Length, Allocator.Persistent);
            NativeArray<VoronoiCell>.Copy(voronoiCells.AsArray(), resultCells);

            // 5. Очистка временных данных
            triangles.Dispose();
            delaunayEdges.Dispose();
            voronoiCells.Dispose();
            voronoiEdges.Dispose();
        }
    }
}