using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct DelaunayTriangulationJob : IJob
    {
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMetadata;
        [ReadOnly] public int Level;
        
        public NativeList<DelaunayTriangle> Triangles;
        public NativeList<int3> Edges;

        public void Execute()
        {
            if (Sites.Length < 3)
                return;

            // Фильтруем точки текущего уровня
            var levelSites = new NativeList<float2>(Sites.Length, Allocator.Temp);
            var levelIndices = new NativeList<int>(Sites.Length, Allocator.Temp);
        
            for (int i = 0; i < Sites.Length; i++)
            {
                if (SiteMetadata[i].Level == Level)
                {
                    levelSites.Add(Sites[i]);
                    levelIndices.Add(i);
                }
            }
        
            if (levelSites.Length < 3)
            {
                levelSites.Dispose();
                levelIndices.Dispose();
                return;
            }

            var bounds = CalculateBounds(levelSites);
            var superTriangle = CreateSuperTriangle(bounds[0], bounds[1]);

            var extendedSites = new NativeList<float2>(levelSites.Length + 3, Allocator.Temp);
            extendedSites.AddRange(levelSites);
            extendedSites.Add(superTriangle[0]);
            extendedSites.Add(superTriangle[1]);
            extendedSites.Add(superTriangle[2]);

            var superIndices = new int3(levelSites.Length, levelSites.Length + 1, levelSites.Length + 2);

            Triangles.Add(CreateTriangle(superIndices.x, superIndices.y, superIndices.z, extendedSites));

            for (int i = 0; i < levelSites.Length; i++)
            {
                AddPoint(i, extendedSites);
            }

            // ✅ КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: СНАЧАЛА удаляем супертреугольник
            RemoveSuperTriangleTriangles(superIndices, levelIndices);
            
            // ✅ ПОТОМ извлекаем рёбра из ЧИСТОЙ триангуляции
            ExtractEdgesFromTriangles();

            extendedSites.Dispose();
            levelSites.Dispose();
            levelIndices.Dispose();
        }

        // НОВЫЙ МЕТОД: извлекаем рёбра из треугольников
        private void ExtractEdgesFromTriangles()
        {
            // Создаем хэш-множество для уникальных ребер
            var edgeSet = new NativeHashSet<int2>(Triangles.Length * 3, Allocator.Temp);
            
            for (int i = 0; i < Triangles.Length; i++)
            {
                var triangle = Triangles[i];
                
                // Добавляем все три ребра треугольника
                AddEdgeToSet(triangle.A, triangle.B, edgeSet);
                AddEdgeToSet(triangle.B, triangle.C, edgeSet);
                AddEdgeToSet(triangle.C, triangle.A, edgeSet);
            }
            
            // Копируем уникальные ребра в выходной буфер
            Edges.Clear();
            foreach (var edge in edgeSet)
            {
                // Используем int3 для совместимости
                Edges.Add(new int3(edge.x, edge.y, 0));
            }
            
            edgeSet.Dispose();
        }

        private void AddEdgeToSet(int a, int b, NativeHashSet<int2> edgeSet)
        {
            // Нормализуем ребро (всегда меньший индекс первым)
            int minIndex = math.min(a, b);
            int maxIndex = math.max(a, b);
            
            edgeSet.Add(new int2(minIndex, maxIndex));
        }

        private float2x3 CreateSuperTriangle(float2 min, float2 max)
        {
            float2 center = (min + max) * 0.5f;
            float2 size = max - min;
            float maxDim = math.max(size.x, size.y);

            float2 p1 = center + new float2(-2 * maxDim, -maxDim);
            float2 p2 = center + new float2(0, 2 * maxDim);
            float2 p3 = center + new float2(2 * maxDim, -maxDim);

            return new float2x3(p1, p2, p3);
        }

        private float2x2 CalculateBounds(NativeArray<float2> sites)
        {
            float2 min = sites[0];
            float2 max = sites[0];

            for (int i = 1; i < sites.Length; i++)
            {
                min = math.min(min, sites[i]);
                max = math.max(max, sites[i]);
            }

            return new float2x2(min, max);
        }

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeList<float2> sites)
        {
            if (Utils.NativeCollectionsExtensions.CalculateCircumCircle(sites[a], sites[b], sites[c], out float2 center, out float radius))
            {
                return new DelaunayTriangle
                {
                    A = a,
                    B = b,
                    C = c,
                    CircumCenter = center,
                    CircumRadius = radius
                };
            }

            return new DelaunayTriangle();
        }

        private void AddPoint(int pointIndex, NativeList<float2> sites)
        {
            var badTriangles = new NativeList<int>(128, Allocator.Temp);
            var polygon = new NativeList<int2>(128, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                var triangle = Triangles[i];
                if (Utils.NativeCollectionsExtensions.IsPointInCircle(sites[pointIndex], triangle.CircumCenter, triangle.CircumRadius))
                {
                    badTriangles.Add(i);
                }
            }

            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triangleIndex = badTriangles[i];
                var triangle = Triangles[triangleIndex];

                CheckAndAddEdge(triangle.A, triangle.B, badTriangles, polygon);
                CheckAndAddEdge(triangle.B, triangle.C, badTriangles, polygon);
                CheckAndAddEdge(triangle.C, triangle.A, badTriangles, polygon);
            }

            for (int i = badTriangles.Length - 1; i >= 0; i--)
            {
                Triangles.RemoveAtSwapBack(badTriangles[i]);
            }


            for (int i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[i];
        
                if (edge.x == edge.y || edge.x == pointIndex || edge.y == pointIndex)
                    continue;
            
                var triangle = CreateTriangle(edge.x, edge.y, pointIndex, sites);
                if (triangle.CircumRadius > 0.0001f)
                {
                    Triangles.Add(triangle);
                }
            }

            polygon.Dispose();
            badTriangles.Dispose();
        }

        private void CheckAndAddEdge(int a, int b, NativeList<int> badTriangles, NativeList<int2> polygon)
        {
            // ✅ ДОБАВЛЕНО: Пропускаем дублирующиеся индексы
            if (a == b) return;
    
            bool isShared = false;
    
            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triangleIndex = badTriangles[i];
                var triangle = Triangles[triangleIndex];
        
                // ✅ ИСПРАВЛЕНО: Проверяем, что a != b в треугольнике
                if ((triangle.A != triangle.B && ((triangle.A == a && triangle.B == b) || (triangle.A == b && triangle.B == a))) ||
                    (triangle.B != triangle.C && ((triangle.B == a && triangle.C == b) || (triangle.B == b && triangle.C == a))) ||
                    (triangle.C != triangle.A && ((triangle.C == a && triangle.A == b) || (triangle.C == b && triangle.A == a))))
                {
                    for (int j = 0; j < polygon.Length; j++)
                    {
                        var existingEdge = polygon[j];
                        if (existingEdge.x == existingEdge.y) continue; // Пропускаем дубли
                        if ((existingEdge.x == a && existingEdge.y == b) || 
                            (existingEdge.x == b && existingEdge.y == a))
                        {
                            polygon.RemoveAtSwapBack(j);
                            isShared = true;
                            break;
                        }
                    }
                    if (isShared) break;
                }
            }

            if (!isShared && a != b) // ✅ ДОБАВЛЕНО: Убедимся, что a != b
            {
                polygon.Add(new int2(a, b));
            }
            
#if UNITY_EDITOR
            var triSet = new NativeHashSet<int3>(Triangles.Length, Allocator.Temp);
            int dupCount = 0;
            for (int i = 0; i < Triangles.Length; i++)
            {
                var t = Triangles[i];
                int3 normTri = new int3(
                    math.min(math.min(t.A, t.B), t.C),
                    math.max(math.min(math.max(t.A, t.B), t.C), math.min(t.A, t.B)),
                    math.max(math.max(t.A, t.B), t.C)
                );
                if (!triSet.Add(normTri)) dupCount++;
            }
            if (dupCount > 0)
                UnityEngine.Debug.LogWarning($"[Delaunay] Found {dupCount} duplicate triangles out of {Triangles.Length}");
            triSet.Dispose();
#endif

            
        }
        
        private void RemoveSuperTriangleTriangles(int3 superIndices, NativeList<int> levelIndices)
        {
            for (int i = Triangles.Length - 1; i >= 0; i--)
            {
                var triangle = Triangles[i];
                if (triangle.A >= superIndices.x || triangle.B >= superIndices.x || triangle.C >= superIndices.x)
                {
                    Triangles.RemoveAtSwapBack(i);
                    continue;
                }
            
                // Преобразуем индексы обратно к глобальным
                triangle.A = levelIndices[triangle.A];
                triangle.B = levelIndices[triangle.B];
                triangle.C = levelIndices[triangle.C];
                Triangles[i] = triangle;
            }
        }
    }
}