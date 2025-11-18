using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        [ReadOnly] public float2 MapSize;

        [NativeDisableContainerSafetyRestriction] public NativeList<DelaunayTriangle> Triangles;
        [NativeDisableContainerSafetyRestriction] public NativeList<int3> Edges;

        public void Execute()
        {
            if (Sites.Length < 3)
                return;

            // Собираем индексы точек текущего уровня
            NativeList<int> levelIndices = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < Sites.Length; i++)
            {
                if (SiteMetadata[i].Level == Level)
                {
                    levelIndices.Add(i);
                }
            }

            if (levelIndices.Length < 3)
            {
                levelIndices.Dispose();
                return;
            }

            // Создаем расширенный массив точек: обычные точки + 3 вершины супер-треугольника
            int totalPoints = levelIndices.Length + 3;
            NativeArray<float2> extendedSites = new NativeArray<float2>(totalPoints, Allocator.Temp);
            
            // Копируем обычные точки
            for (int i = 0; i < levelIndices.Length; i++)
            {
                extendedSites[i] = Sites[levelIndices[i]];
            }

            // Вычисляем границы для супер-треугольника
            float2 minBound = new float2(float.MaxValue, float.MaxValue);
            float2 maxBound = new float2(float.MinValue, float.MinValue);
            
            for (int i = 0; i < levelIndices.Length; i++)
            {
                float2 pos = extendedSites[i];
                minBound = math.min(minBound, pos);
                maxBound = math.max(maxBound, pos);
            }
            
            // Добавляем отступы для супер-треугольника
            float padding = math.max(maxBound.x - minBound.x, maxBound.y - minBound.y) * 0.5f;
            minBound -= new float2(padding, padding);
            maxBound += new float2(padding, padding);
            
            // Создаем супер-треугольник
            float2 center = (minBound + maxBound) * 0.5f;
            float maxDim = math.max(maxBound.x - minBound.x, maxBound.y - minBound.y) * 1.5f;
            
            // Вершины супер-треугольника (индексы levelIndices.Length, levelIndices.Length+1, levelIndices.Length+2)
            extendedSites[levelIndices.Length] = center + new float2(-maxDim, -maxDim * 0.5f);     // p1
            extendedSites[levelIndices.Length + 1] = center + new float2(maxDim, -maxDim * 0.5f);  // p2
            extendedSites[levelIndices.Length + 2] = center + new float2(0, maxDim);              // p3

            // Инициализируем триангуляцию с супер-треугольником
            Triangles.Clear();
            Triangles.Add(CreateTriangle(
                levelIndices.Length,        // индекс p1
                levelIndices.Length + 1,    // индекс p2
                levelIndices.Length + 2,    // индекс p3
                extendedSites
            ));

            // Вставляем все точки по одной
            for (int i = 0; i < levelIndices.Length; i++)
            {
                InsertPoint(i, extendedSites, levelIndices.Length);
            }

            // Удаляем треугольники, содержащие вершины супер-треугольника
            RemoveSuperTriangleTriangles(levelIndices.Length);

            // Извлекаем рёбра
            ExtractEdgesFromTriangles();

            // Очищаем память
            extendedSites.Dispose();
            levelIndices.Dispose();
        }

        private void InsertPoint(int pointIndex, NativeArray<float2> sites, int superTriangleStartIndex)
        {
            NativeList<int> badTriangles = new NativeList<int>(Allocator.Temp);
            
            // Находим все треугольники, чья описанная окружность содержит точку
            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle tri = Triangles[i];
                if (tri.CircumRadius > 0f && IsPointInCircumCircle(sites[pointIndex], tri))
                {
                    badTriangles.Add(i);
                }
            }

            if (badTriangles.Length == 0)
            {
                badTriangles.Dispose();
                return;
            }

            // Считаем рёбра плохих треугольников
            NativeHashMap<int2, int> edgeCount = new NativeHashMap<int2, int>(badTriangles.Length * 3, Allocator.Temp);
            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triIdx = badTriangles[i];
                DelaunayTriangle tri = Triangles[triIdx];
                AddEdgeToCount(tri.A, tri.B, ref edgeCount);
                AddEdgeToCount(tri.B, tri.C, ref edgeCount);
                AddEdgeToCount(tri.C, tri.A, ref edgeCount);
            }

            // Удаляем плохие треугольники
            for (int i = badTriangles.Length - 1; i >= 0; i--)
            {
                Triangles.RemoveAtSwapBack(badTriangles[i]);
            }

            // Создаем новые треугольники из полигон.дырки и новой точки
            using (NativeArray<int2> polygonEdges = edgeCount.GetKeyArray(Allocator.Temp))
            {
                for (int i = 0; i < polygonEdges.Length; i++)
                {
                    int2 edge = polygonEdges[i];
                    if (edgeCount[edge] == 1) // Граница дырки
                    {
                        if (edge.x != pointIndex && edge.y != pointIndex)
                        {
                            DelaunayTriangle newTri = CreateTriangle(edge.x, edge.y, pointIndex, sites);
                            if (newTri.CircumRadius > 0.001f) // Фильтр вырожденных треугольников
                            {
                                Triangles.Add(newTri);
                            }
                        }
                    }
                }
            }

            edgeCount.Dispose();
            badTriangles.Dispose();
        }

        private bool IsPointInCircumCircle(float2 point, DelaunayTriangle tri)
        {
            // Используем оптимизированный метод без sqrt
            return math.distance(tri.CircumCenter, point) < tri.CircumRadius - 0.001f;
        }

        private void AddEdgeToCount(int a, int b, ref NativeHashMap<int2, int> map)
        {
            if (a == b) return;
            int2 edge = new int2(math.min(a, b), math.max(a, b));
            
            if (map.TryGetValue(edge, out int count))
            {
                map[edge] = count + 1;
            }
            else
            {
                map.Add(edge, 1);
            }
        }

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeArray<float2> sites)
        {
            float2 pA = sites[a];
            float2 pB = sites[b];
            float2 pC = sites[c];
            
            if (CalculateCircumCircle(pA, pB, pC, out float2 center, out float radius))
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
            
            // Возвращаем невалидный треугольник
            return new DelaunayTriangle { CircumRadius = -1f };
        }

        private bool CalculateCircumCircle(float2 a, float2 b, float2 c, out float2 center, out float radius)
        {
            center = float2.zero;
            radius = 0f;

            // Проверка на вырожденный треугольник
            float area = math.abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            if (area < 0.001f)
                return false;

            // Вычисление центра описанной окружности
            float d = 2f * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
            if (math.abs(d) < 0.001f)
                return false;

            center.x = ((a.x * a.x + a.y * a.y) * (b.y - c.y) + 
                        (b.x * b.x + b.y * b.y) * (c.y - a.y) + 
                        (c.x * c.x + c.y * c.y) * (a.y - b.y)) / d;
            
            center.y = ((a.x * a.x + a.y * a.y) * (c.x - b.x) + 
                        (b.x * b.x + b.y * b.y) * (a.x - c.x) + 
                        (c.x * c.x + c.y * c.y) * (b.x - a.x)) / d;

            radius = math.distance(center, a);
            return true;
        }

        private void RemoveSuperTriangleTriangles(int superTriangleStartIndex)
        {
            // Удаляем треугольники, содержащие вершины супер-треугольника
            for (int i = Triangles.Length - 1; i >= 0; i--)
            {
                DelaunayTriangle tri = Triangles[i];
                if (tri.A >= superTriangleStartIndex || 
                    tri.B >= superTriangleStartIndex || 
                    tri.C >= superTriangleStartIndex)
                {
                    Triangles.RemoveAtSwapBack(i);
                }
            }
        }

        private void ExtractEdgesFromTriangles()
        {
            Edges.Clear();
            if (Triangles.Length == 0) return;

            NativeHashMap<int2, int> edgeMap = new NativeHashMap<int2, int>(Triangles.Length * 3, Allocator.Temp);
            
            // Считаем все ребра
            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle tri = Triangles[i];
                AddEdgeToCount(tri.A, tri.B, ref edgeMap);
                AddEdgeToCount(tri.B, tri.C, ref edgeMap);
                AddEdgeToCount(tri.C, tri.A, ref edgeMap);
            }

            // Ищем граничные ребра (встречаются только один раз)
            using (NativeArray<int2> keys = edgeMap.GetKeyArray(Allocator.Temp))
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    int2 edge = keys[i];
                    if (edgeMap[edge] == 1)
                    {
                        Edges.Add(new int3(edge.x, edge.y, 0));
                    }
                }
            }

            edgeMap.Dispose();
        }
    }
}