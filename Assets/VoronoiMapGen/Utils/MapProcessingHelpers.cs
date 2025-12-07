using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems.Utils
{
    /// <summary>
    /// Вспомогательные алгоритмы для MapGenerationSystem,
    /// чтобы разгрузить основной класс от циклов перекладки данных.
    /// </summary>
    public static class MapProcessingHelpers
    {
        // 1. Фильтрация "сырых" сайтов (удаление призраков с Value < -0.5)
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> meta) FilterValidSites(
            NativeArray<float2> rawSites, NativeArray<VoronoiSite> rawMeta, Allocator allocator)
        {
            int validCount = 0;
            for (int i = 0; i < rawSites.Length; i++)
            {
                if (rawMeta[i].Value > -0.5f) validCount++;
            }

            var sites = new NativeArray<float2>(validCount, allocator);
            var meta = new NativeArray<VoronoiSite>(validCount, allocator);

            int idx = 0;
            for (int i = 0; i < rawSites.Length; i++)
            {
                if (rawMeta[i].Value > -0.5f)
                {
                    sites[idx] = rawSites[i];
                    meta[idx] = rawMeta[i];
                    
                    // Обновляем индекс внутри структуры
                    var m = meta[idx]; 
                    m.Index = idx; 
                    meta[idx] = m;
                    
                    idx++;
                }
            }
            return (sites, meta);
        }

        // 2. Конвертация Треугольников в Ребра
        public static NativeList<VoronoiEdge> ExtractEdgesFromDelaunay(
            NativeList<TriangleIndices> triangles, Allocator allocator)
        {
            var edges = new NativeList<VoronoiEdge>(triangles.Length * 3, allocator);
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                edges.Add(new VoronoiEdge { SiteA = t.A, SiteB = t.B });
                edges.Add(new VoronoiEdge { SiteA = t.B, SiteB = t.C });
                edges.Add(new VoronoiEdge { SiteA = t.C, SiteB = t.A });
            }
            return edges;
        }

        // 3. Умный расчет лимита дистанции (Статистический метод)
        public static float CalculateAdaptiveGraphLimit(
            NativeList<TriangleIndices> triangles, 
            NativeArray<float2> sites, 
            NativeArray<TectonicPlateData> tectonics, // <-- NEW
            int level)
        {
            float totalDist = 0;
            int sampleCount = 0;
            int step = math.max(1, triangles.Length / 1000); 
                
            for(int i=0; i < triangles.Length; i += step)
            {
                var t = triangles[i];
                // Игнорируем Океан! Считаем статистику только по суше
                if (tectonics[t.A].IsOcean || tectonics[t.B].IsOcean || tectonics[t.C].IsOcean) 
                    continue;

                float d = math.distance(sites[t.A], sites[t.B]);
                totalDist += d;
                sampleCount++;
            }
            
            float realAvgDist = (sampleCount > 0) ? (totalDist / sampleCount) : 50f;
            float multiplier = (level == 0) ? 3.0f : 1.8f; // Уменьшил множитель для строгости
            
            // Debug.Log($"Adaptive Limit L{level}: Avg={realAvgDist} Limit={realAvgDist*multiplier}");
            return (realAvgDist * multiplier) * (realAvgDist * multiplier);
        }

        // 4. Сборка финальных структур для Pipeline и Кэша
        public static void AssembleFinalGeometry(
            int level,
            NativeArray<float2> sites,
            NativeArray<VoronoiSite> meta,
            NativeList<TriangleIndices> triangles, // Для логических связей
            NativeList<float2> cellVerts,          // Для геометрии ячейки
            NativeList<int> cellCounts,            // Кол-во вертексов в ячейке
            ref NativeList<VoronoiCell> outCells,
            ref NativeList<VoronoiEdge> outEdges)
        {
            // 4.1. Собираем ячейки и их периметр
            int vertOffset = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                int vCount = cellCounts[i];
                
                outCells.Add(new VoronoiCell 
                { 
                    SiteIndex = i, 
                    Centroid = sites[i], 
                    Level = level, 
                    ParentRegionIndex = meta[i].ParentIndex, 
                    ParentEntity = Entity.Null 
                });
                
                // Периметр полигона (визуальные ребра)
                for (int k = 0; k < vCount; k++) 
                {
                    outEdges.Add(new VoronoiEdge { 
                        SiteA = i, SiteB = -1, 
                        VertexA = cellVerts[vertOffset + k], 
                        VertexB = cellVerts[vertOffset + (k + 1) % vCount], 
                        Level = level 
                    });
                }
                vertOffset += vCount;
            }

            // 4.2. Добавляем логические связи (для дорог)
            for(int i=0; i<triangles.Length; i++) 
            {
                var t = triangles[i];
                outEdges.Add(new VoronoiEdge { SiteA = t.A, SiteB = t.B, Level = level });
                outEdges.Add(new VoronoiEdge { SiteA = t.B, SiteB = t.C, Level = level });
                outEdges.Add(new VoronoiEdge { SiteA = t.C, SiteB = t.A, Level = level });
            }
        }
    }
}