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

        // Вход / выход
        public NativeList<DelaunayTriangle> Triangles;
        public NativeList<int3> Edges;

        public void Execute()
        {
            if (Sites.Length < 3)
                return;

            // собираем точки уровня
            NativeList<float2> levelSites = new NativeList<float2>(Sites.Length, Allocator.Temp);
            NativeList<int> levelIndices = new NativeList<int>(Sites.Length, Allocator.Temp);

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

            float2x2 bounds = CalculateBounds(levelSites.AsArray());
            float2x3 superTriangle = CreateSuperTriangle(bounds.c0, bounds.c1);

            // extendedSites: levelSites + 3 вершины супер-треугольника
            NativeList<float2> extendedSites = new NativeList<float2>(levelSites.Length + 3, Allocator.Temp);
            extendedSites.AddRange(levelSites.AsArray());
            extendedSites.Add(superTriangle.c0);
            extendedSites.Add(superTriangle.c1);
            extendedSites.Add(superTriangle.c2);

            int superA = levelSites.Length;
            int superB = levelSites.Length + 1;
            int superC = levelSites.Length + 2;

            Triangles.Clear();
            Edges.Clear();

            // заранее добавляем супер-треугольник
            DelaunayTriangle st = CreateTriangle(superA, superB, superC, extendedSites.AsArray());
            Triangles.Add(st);

            // Для каждого уровня вставляем точку (индексы 0..levelSites.Length-1)
            for (int i = 0; i < levelSites.Length; i++)
            {
                AddPoint(i, extendedSites);
            }

            // удаляем треугольники со сторонами супер-треугольника и переводим индексы в глобальные (levelIndices)
            int3 superIndices = new int3(superA, superB, superC);
            RemoveSuperTriangleTriangles(superIndices, levelIndices);

            // извлекаем рёбра
            ExtractEdgesFromTriangles();

            extendedSites.Dispose();
            levelSites.Dispose();
            levelIndices.Dispose();
        }

        /// <summary>
        /// Извлекает рёбра: считаем как ключ нормализованный int2 (min, max).
        /// Если ребро встречается ровно 1 раз -> внешнее ребро.
        /// </summary>
        private void ExtractEdgesFromTriangles()
        {
            // estimate capacity: triangles * 3
            NativeHashMap<int2, int> edgeCount = new NativeHashMap<int2, int>(Triangles.Length * 3, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle t = Triangles[i];
                AddEdgeCount(t.A, t.B, ref edgeCount);
                AddEdgeCount(t.B, t.C, ref edgeCount);
                AddEdgeCount(t.C, t.A, ref edgeCount);
            }

            Edges.Clear();
            NativeArray<int2> keys = edgeCount.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                int2 key = keys[i];
                if (edgeCount.TryGetValue(key, out int count) && count == 1)
                {
                    Edges.Add(new int3(key.x, key.y, 0));
                }
            }
            keys.Dispose();

            edgeCount.Dispose();
        }

        private void AddEdgeCount(int a, int b, ref NativeHashMap<int2, int> map)
        {
            if (a == b) return;
            int minIndex = math.min(a, b);
            int maxIndex = math.max(a, b);
            int2 key = new int2(minIndex, maxIndex);
            if (map.TryGetValue(key, out int val))
            {
                map[key] = val + 1;
            }
            else
            {
                map.TryAdd(key, 1);
            }
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

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeArray<float2> sites)
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

            // возвращаем треугольник с нулевым радиусом — он будет отброшен
            return new DelaunayTriangle { A = a, B = b, C = c, CircumRadius = -1f };
        }

        /// <summary>
        /// Вставка точки: вычисляем "плохие" треугольники (их индексы), считаем все их ребра (edgeCount).
        /// Рёбра, встретившиеся ровно 1 раз — образуют полигон. Удаляем плохие треугольники путём пересборки списка.
        /// </summary>
        private void AddPoint(int pointIndex, NativeList<float2> sites)
        {
            // bad triangles set
            NativeList<int> badSet = new NativeList<int>(128, Allocator.Temp);
            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle tri = Triangles[i];
                if (tri.CircumRadius > 0f && Utils.NativeCollectionsExtensions.IsPointInCircle(sites[pointIndex], tri.CircumCenter, tri.CircumRadius))
                {
                    badSet.Add(i);
                }
            }

            if (badSet.Length == 0)
            {
                badSet.Dispose();
                return;
            }

            // считаем рёбра плохих треугольников
            NativeHashMap<int2, int> edgeCount = new NativeHashMap<int2, int>(badSet.Length * 3, Allocator.Temp);
            for (int i = 0; i < badSet.Length; i++)
            {
                int triIdx = badSet[i];
                DelaunayTriangle tri = Triangles[triIdx];
                AddEdgeCount(tri.A, tri.B, ref edgeCount);
                AddEdgeCount(tri.B, tri.C, ref edgeCount);
                AddEdgeCount(tri.C, tri.A, ref edgeCount);
            }

            // пересобираем Triangles без плохих треугольников (эффективнее, чем RemoveAtSwapBack по индексам)
            NativeList<DelaunayTriangle> newTriangles = new NativeList<DelaunayTriangle>(Triangles.Length - badSet.Length + 16, Allocator.Temp);
            for (int i = 0; i < Triangles.Length; i++)
            {
                bool isBad = false;
                // проверка наличия в badSet: можно сделать hashset, но для простоты и т.к. badSet обычно меньше,
                // используем прямой поиск — если у вас часто много плохих треугольников, замените badSet на NativeHashSet.
                // Чтобы не ухудшить случай большого badSet — если badSet.Length > Triangles.Length/4 — лучше конвертировать:
                if (badSet.Length > Triangles.Length / 4)
                {
                    // конвертируем в hashset для быстрого поиска
                    NativeHashSet<int> tmpHash = new NativeHashSet<int>(badSet.Length, Allocator.Temp);
                    for (int k = 0; k < badSet.Length; k++) tmpHash.Add(badSet[k]);
                    for (int k = 0; k < Triangles.Length; k++)
                    {
                        if (!tmpHash.Contains(k))
                            newTriangles.Add(Triangles[k]);
                    }
                    tmpHash.Dispose();
                    // конец сборки
                    i = Triangles.Length; // выйти из внешнего цикла
                    break;
                }
                else
                {
                    // линейный поиск в badSet (для небольших badSet это быстрее, чем аллокация hashset)
                    for (int j = 0; j < badSet.Length; j++)
                    {
                        if (badSet[j] == i) { isBad = true; break; }
                    }
                    if (!isBad)
                        newTriangles.Add(Triangles[i]);
                }
            }

            Triangles.Clear();
            Triangles.AddRange(newTriangles.AsArray());

            // Полигоны — рёбра с count == 1
            NativeArray<int2> keys = edgeCount.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                int2 e = keys[i];
                if (edgeCount.TryGetValue(e, out int count) && count == 1)
                {
                    if (e.x == e.y || e.x == pointIndex || e.y == pointIndex) continue;

                    DelaunayTriangle newT = CreateTriangle(e.x, e.y, pointIndex, sites.AsArray());
                    if (newT.CircumRadius > 0.0001f)
                    {
                        Triangles.Add(newT);
                    }
                }
            }
            keys.Dispose();

            newTriangles.Dispose();
            edgeCount.Dispose();
            badSet.Dispose();
        }

        private void RemoveSuperTriangleTriangles(int3 superIndices, NativeList<int> levelIndices)
        {
            int globalOffset = superIndices.x;

            for (int i = Triangles.Length - 1; i >= 0; i--)
            {
                DelaunayTriangle triangle = Triangles[i];

                // если любой индекс принадлежит супер-треугольнику — удаляем
                if (triangle.A >= globalOffset || triangle.B >= globalOffset || triangle.C >= globalOffset)
                {
                    Triangles.RemoveAtSwapBack(i);
                    continue;
                }

                // переходим от локальных индексов back -> глобальные (из levelIndices)
                triangle.A = levelIndices[triangle.A];
                triangle.B = levelIndices[triangle.B];
                triangle.C = levelIndices[triangle.C];
                Triangles[i] = triangle;
            }
        }
    }
}
