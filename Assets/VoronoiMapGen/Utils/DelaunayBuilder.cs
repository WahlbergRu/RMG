using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    // Структура треугольника (индексы вершин)
    public struct TriangleIndices
    {
        public int A, B, C;
        public bool IsBad; // Флаг для удаления
    }

    public static class DelaunayBuilder
    {
        public static void Triangulate(
            NativeArray<float2> points, 
            ref NativeList<TriangleIndices> triangles,
            float2 mapSize)
        {
            triangles.Clear();

            // 1. Создаем Супер-Треугольник (огромный, охватывающий всю карту)
            // Делаем его реально большим, чтобы не влиял на центр карты
            float M = math.max(mapSize.x, mapSize.y) * 100.0f;
            
            // Вершины супер-треугольника (добавляем временно в конец списка, но здесь у нас только индексы)
            // Мы будем считать, что:
            // Index N   = SuperA
            // Index N+1 = SuperB
            // Index N+2 = SuperC
            int n = points.Length;
            
            // Локальный кэш точек + супер-треугольник
            var allPoints = new NativeList<float2>(n + 3, Allocator.Temp);
            allPoints.AddRange(points);
            allPoints.Add(new float2(-M, -M));       // N
            allPoints.Add(new float2(2 * M, -M));    // N+1
            allPoints.Add(new float2(-M, 2 * M));    // N+2

            // Добавляем первый треугольник
            triangles.Add(new TriangleIndices { A = n, B = n + 1, C = n + 2, IsBad = false });

            // 2. Вставляем точки по одной
            for (int i = 0; i < n; i++)
            {
                float2 p = points[i];
                var badTriangles = new NativeList<int>(32, Allocator.Temp);

                // А. Ищем плохие треугольники (в чьи окружности попала точка)
                for (int t = 0; t < triangles.Length; t++)
                {
                    var tri = triangles[t];
                    if (GeometryMath.IsPointInCircumCircle(p, allPoints[tri.A], allPoints[tri.B], allPoints[tri.C]))
                    {
                        badTriangles.Add(t);
                        // Помечаем как плохой, но не удаляем пока, чтобы индексы не поехали
                        tri.IsBad = true; 
                        triangles[t] = tri;
                    }
                }

                // Б. Ищем границу "дыры" (полигон из уникальных ребер)
                var polygon = new NativeList<int2>(16, Allocator.Temp);
                for (int j = 0; j < badTriangles.Length; j++)
                {
                    var tIdx = badTriangles[j];
                    var tri = triangles[tIdx];
                    AddEdgeIfUnique(ref polygon, tri.A, tri.B, triangles, badTriangles);
                    AddEdgeIfUnique(ref polygon, tri.B, tri.C, triangles, badTriangles);
                    AddEdgeIfUnique(ref polygon, tri.C, tri.A, triangles, badTriangles);
                }

                // В. Удаляем плохие треугольники
                // (Идем с конца, чтобы swapback работал корректно, или просто фильтруем потом)
                // Для простоты в DOTS: перезапишем список
                CleanupTriangles(ref triangles);

                // Г. Триангулируем дыру (соединяем грани с новой точкой)
                for (int k = 0; k < polygon.Length; k++)
                {
                    triangles.Add(new TriangleIndices { A = polygon[k].x, B = polygon[k].y, C = i });
                }
                
                badTriangles.Dispose();
                polygon.Dispose();
            }
            
            allPoints.Dispose();
        }

        private static void AddEdgeIfUnique(ref NativeList<int2> polygon, int a, int b, NativeList<TriangleIndices> tris, NativeList<int> badTris)
        {
            // Ребро уникально, если оно не разделяется с другим "плохим" треугольником
            // В алгоритме Bowyer-Watson это делается проверкой соседей. 
            // Упрощенная версия: ищем, встречается ли ребро (a,b) или (b,a) в других плохих треугольниках.
            // Но проще: просто добавляем все, а потом удаляем дубликаты.
            
            // В данном случае "Unique" означает - ребро внешнее для группы удаляемых треугольников.
            // Проверяем, есть ли это ребро в других badTriangles.
            // Если ребро общее для двух badTriangles - оно удаляется (внутреннее).
            // Если ребро принадлежит badTriangle и goodTriangle (или пустоте) - оно остается.
            
            bool isShared = false;
            // ... тут логика поиска дубликатов сложна O(N^2).
            // Упростим: просто добавляем в список, если такое ребро уже есть - удаляем оба.
            
            for (int i = 0; i < polygon.Length; i++)
            {
                if ((polygon[i].x == a && polygon[i].y == b) || (polygon[i].x == b && polygon[i].y == a))
                {
                    polygon.RemoveAtSwapBack(i);
                    return; // Нашли дубликат - уничтожили оба, выходим
                }
            }
            // Не нашли - добавляем
            polygon.Add(new int2(a, b));
        }

        private static void CleanupTriangles(ref NativeList<TriangleIndices> triangles)
        {
            // Удаляем помеченные IsBad
            for (int i = triangles.Length - 1; i >= 0; i--)
            {
                if (triangles[i].IsBad) triangles.RemoveAtSwapBack(i);
            }
        }
    }
}