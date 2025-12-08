using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class VoronoiBuilder
    {
        // Превращает сайты и треугольники Делоне в ячейки Вороного
        public static void BuildCells(
            NativeArray<float2> sites,
            NativeList<TriangleIndices> triangles,
            float2 mapSize,
            ref NativeList<float2> outVertices, // Плоский массив всех вершин всех ячеек
            ref NativeList<int> outCellCounts // Сколько вершин у каждой ячейки (по порядку сайтов)
        )
        {
            // 1. Вычисляем центры окружностей для всех треугольников
            // Это и есть вершины Вороного
            var superTriStart = sites.Length; // Индекс начала супер-вершин

            // Создаем временные точки для супер-треугольника, чтобы GetCircumcenter работал
            var M = math.max(mapSize.x, mapSize.y) * 100.0f;
            var allPoints = new NativeList<float2>(sites.Length + 3, Allocator.Temp);
            allPoints.AddRange(sites);
            allPoints.Add(new float2(-M, -M));
            allPoints.Add(new float2(2 * M, -M));
            allPoints.Add(new float2(-M, 2 * M));

            var circumcenters = new NativeArray<float2>(triangles.Length, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                GeometryMath.GetCircumcenter(allPoints[t.A], allPoints[t.B], allPoints[t.C], out var c, out _);
                circumcenters[i] = c;
            }

            // 2. Собираем полигоны
            // Используем MultiHashMap: Key = SiteIndex, Value = TriangleIndex
            // Это позволяет найти все треугольники, к которым принадлежит точка
            var siteToTri = new NativeParallelMultiHashMap<int, int>(triangles.Length * 3, Allocator.Temp);

            for (var i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                siteToTri.Add(t.A, i);
                siteToTri.Add(t.B, i);
                siteToTri.Add(t.C, i);
            }

            // 3. Для каждого сайта строим ячейку
            var poly = new NativeList<float2>(16, Allocator.Temp);

            for (var i = 0; i < sites.Length; i++)
            {
                poly.Clear();

                // Собираем центры соседних треугольников
                if (siteToTri.TryGetFirstValue(i, out var tIdx, out var it))
                    do
                    {
                        poly.Add(circumcenters[tIdx]);
                    } while (siteToTri.TryGetNextValue(out tIdx, ref it));

                // Сортируем вершины по часовой стрелке вокруг сайта
                if (poly.Length > 0) poly.Sort(new ClockwiseComparer(sites[i]));

                // 4. ОБРЕЗКА (Clipping)
                // Это превращает "бесконечные" ячейки (уходящие в супер-треугольник) в квадратные
                PolygonClipper.ClipToRect(ref poly, mapSize);

                // Записываем результат
                outCellCounts.Add(poly.Length);
                outVertices.AddRange(poly.AsArray());
            }

            siteToTri.Dispose();
            circumcenters.Dispose();
            allPoints.Dispose();
            poly.Dispose();
        }

        private struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 center;

            public ClockwiseComparer(float2 c)
            {
                center = c;
            }

            public int Compare(float2 a, float2 b)
            {
                return math.atan2(a.y - center.y, a.x - center.x)
                    .CompareTo(math.atan2(b.y - center.y, b.x - center.x));
            }
        }
    }
}