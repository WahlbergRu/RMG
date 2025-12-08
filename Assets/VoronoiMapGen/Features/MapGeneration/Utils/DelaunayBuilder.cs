using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    // Структура треугольника
    public struct TriangleIndices
    {
        public int A, B, C;
        public bool IsBad;
    }

    public static class DelaunayBuilder
    {
        public static void Triangulate(
            NativeArray<float2> points,
            ref NativeList<TriangleIndices> triangles,
            float2 mapSize)
        {
            triangles.Clear();

            // 1. Супер-Треугольник
            float M = math.max(mapSize.x, mapSize.y) * 1000.0f;
            int n = points.Length;

            // Временный список точек + 3 вершины супер-треугольника
            NativeList<float2> allPoints = new NativeList<float2>(n + 3, Allocator.Temp);
            allPoints.AddRange(points);
            allPoints.Add(new float2(-M, -M)); // Index n
            allPoints.Add(new float2(2 * M, -M)); // Index n+1
            allPoints.Add(new float2(-M, 2 * M)); // Index n+2

            // Добавляем первый супер-треугольник
            triangles.Add(new TriangleIndices { A = n, B = n + 1, C = n + 2, IsBad = false });

            // 2. Вставка точек (Bowyer-Watson)
            for (int i = 0; i < n; i++)
            {
                float2 p = points[i];
                NativeList<int> badTriangles = new NativeList<int>(32, Allocator.Temp);

                // Ищем плохие треугольники
                for (int t = 0; t < triangles.Length; t++)
                {
                    TriangleIndices tri = triangles[t];
                    if (GeometryMath.IsPointInCircumCircle(p, allPoints[tri.A], allPoints[tri.B], allPoints[tri.C]))
                    {
                        badTriangles.Add(t);
                        tri.IsBad = true; // Помечаем
                        triangles[t] = tri;
                    }
                }

                // Ищем границу (полигон дыры)
                NativeList<int2> polygon = new NativeList<int2>(16, Allocator.Temp);
                for (int j = 0; j < badTriangles.Length; j++)
                {
                    TriangleIndices tri = triangles[badTriangles[j]];
                    AddEdgeIfUnique(ref polygon, tri.A, tri.B);
                    AddEdgeIfUnique(ref polygon, tri.B, tri.C);
                    AddEdgeIfUnique(ref polygon, tri.C, tri.A);
                }

                // Удаляем плохие
                CleanupBadTriangles(ref triangles);

                // Зашиваем дыру
                for (int k = 0; k < polygon.Length; k++)
                    triangles.Add(new TriangleIndices { A = polygon[k].x, B = polygon[k].y, C = i });

                badTriangles.Dispose();
                polygon.Dispose();
            }

            // 3. !!! ФИНАЛЬНАЯ ОЧИСТКА !!! 
            // Удаляем треугольники, связанные с супер-структурой (индексы >= n)
            // Без этого шага код падает с IndexOutOfRange при попытке читать массивы (Tectonics и т.д.) по этим индексам.
            RemoveSuperStructures(ref triangles, n);

            allPoints.Dispose();
        }

        private static void RemoveSuperStructures(ref NativeList<TriangleIndices> triangles, int n)
        {
            // Идем с конца, чтобы безопасно удалять
            for (int i = triangles.Length - 1; i >= 0; i--)
            {
                TriangleIndices t = triangles[i];
                // Если хоть одна вершина принадлежит супер-треугольнику
                if (t.A >= n || t.B >= n || t.C >= n) triangles.RemoveAtSwapBack(i);
            }
        }

        private static void CleanupBadTriangles(ref NativeList<TriangleIndices> triangles)
        {
            for (int i = triangles.Length - 1; i >= 0; i--)
                if (triangles[i].IsBad)
                    triangles.RemoveAtSwapBack(i);
        }

        private static void AddEdgeIfUnique(ref NativeList<int2> polygon, int a, int b
            // аргументы 'tris' и 'badTris' убраны для упрощения, здесь логика "только внешние" ребра
            // в оригинальной реализации Watson это сложнее, но для простого случая достаточно count based или списка
            // В предыдущем коде использовался простой подход: add all edges -> remove duplicates.
        )
        {
            bool isDuplicate = false;
            for (int i = 0; i < polygon.Length; i++)
                // Если ребро (a,b) или (b,a) уже есть, значит оно общее для двух удаляемых треугольников -> удаляем его.
                if ((polygon[i].x == a && polygon[i].y == b) || (polygon[i].x == b && polygon[i].y == a))
                {
                    polygon.RemoveAtSwapBack(i);
                    isDuplicate = true;
                    break;
                }

            if (!isDuplicate) polygon.Add(new int2(a, b));
        }
    }
}