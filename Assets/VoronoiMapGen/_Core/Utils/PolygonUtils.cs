using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class PolygonUtils
    {
        // Допуск для расчетов (более строгий, так как мы теперь квантуем)
        private const float Epsilon = 1e-5f;

        // Разрешение сетки. 1000 = 3 знака после запятой.
        // Это значит, что точки будут прыгать по сетке 0.001. 
        // Это убивает float-дребезг.
        private const float GridPrecision = 10000.0f;

        // === ОБРЕЗКА (ОСНОВНОЙ МЕТОД) ===
        public static void ClipToPolygon(ref NativeList<float2> subject, NativeList<float2> clipper)
        {
            if (subject.Length < 3 || clipper.Length < 3) return;

            // 1. Квантуем родителя (клиппер), чтобы убрать микро-шум на его границах
            // Используем копию, чтобы не ломать оригинал
            var cleanClipper = new NativeList<float2>(clipper.Length, Allocator.Temp);
            for (var i = 0; i < clipper.Length; i++)
                cleanClipper.Add(Quantize(clipper[i]));

            EnsureCCW(ref cleanClipper);

            var len = cleanClipper.Length;
            for (var i = 0; i < len; i++)
            {
                if (subject.Length < 3) break;

                var a = cleanClipper[i];
                var b = cleanClipper[(i + 1) % len];

                if (math.distancesq(a, b) < 1e-6f) continue;

                // Вектор и нормаль (CCW)
                var edge = b - a;
                var normal = math.normalize(new float2(-edge.y, edge.x));
                var dist = math.dot(normal, a);

                ClipByPlane(ref subject, normal, dist);
            }

            cleanClipper.Dispose();
        }

        public static void ClipToBounds(ref NativeList<float2> polygon, float2 mapSize)
        {
            // Также квантуем границы мира
            var min = Quantize(new float2(0, 0));
            var max = Quantize(mapSize);

            ClipByPlane(ref polygon, new float2(1, 0), min.x);
            ClipByPlane(ref polygon, new float2(-1, 0), -max.x);
            ClipByPlane(ref polygon, new float2(0, 1), min.y);
            ClipByPlane(ref polygon, new float2(0, -1), -max.y);
        }

        // === ВНУТРЕННИЕ МЕХАНИЗМЫ ===

        private static void ClipByPlane(ref NativeList<float2> poly, float2 n, float d)
        {
            var output = new NativeList<float2>(poly.Length + 4, Allocator.Temp);

            // Квантуем входной полигон перед обработкой, чтобы все точки "сели" на сетку
            for (var i = 0; i < poly.Length; i++) poly[i] = Quantize(poly[i]);

            for (var i = 0; i < poly.Length; i++)
            {
                var curr = poly[i];
                var prev = poly[(i + poly.Length - 1) % poly.Length];

                var currIn = math.dot(n, curr) >= d - Epsilon;
                var prevIn = math.dot(n, prev) >= d - Epsilon;

                if (currIn)
                {
                    if (!prevIn)
                        output.Add(Quantize(Intersect(prev, curr, n, d))); // Вход -> Квантуем точку пересечения
                    output.Add(curr);
                }
                else if (prevIn)
                {
                    output.Add(Quantize(Intersect(prev, curr, n, d))); // Выход -> Квантуем точку пересечения
                }
            }

            poly.Clear();
            if (output.Length >= 3)
            {
                for (var k = 0; k < output.Length; k++)
                {
                    var p = output[k];
                    // Фильтр дубликатов (после квантования это очень надежно)
                    if (poly.Length > 0 && math.distancesq(p, poly[poly.Length - 1]) < 1e-8f) continue;
                    if (!IsNaN(p)) poly.Add(p);
                }

                // Проверка замыкания
                if (poly.Length > 2 && math.distancesq(poly[0], poly[poly.Length - 1]) < 1e-8f)
                    poly.RemoveAt(poly.Length - 1);
            }

            output.Dispose();
        }

        // --- ВАЖНЕЙШИЙ ФИКС: СЕТКА ---
        // Превращает 5.3000001 в 5.300
        private static float2 Quantize(float2 v)
        {
            return new float2(
                math.round(v.x * GridPrecision) / GridPrecision,
                math.round(v.y * GridPrecision) / GridPrecision
            );
        }

        private static void EnsureCCW(ref NativeList<float2> poly)
        {
            float area = 0;
            for (var i = 0; i < poly.Length; i++)
            {
                var curr = poly[i];
                var next = poly[(i + 1) % poly.Length];
                area += (next.x - curr.x) * (next.y + curr.y);
            }

            if (area > 0)
                for (var i = 0; i < poly.Length / 2; i++)
                {
                    var tmp = poly[i];
                    poly[i] = poly[poly.Length - 1 - i];
                    poly[poly.Length - 1 - i] = tmp;
                }
        }

        private static float2 Intersect(float2 a, float2 b, float2 n, float d)
        {
            var t = (d - math.dot(n, a)) / math.dot(n, b - a);
            // double precision math for intersection stability
            return math.lerp(a, b, t);
        }

        // === ВИЗУАЛЬНЫЕ МОДИФИКАТОРЫ ===
        public static void ApplyInset(ref NativeList<float2> poly, float2 center, float amount)
        {
            if (math.abs(amount) < 0.001f || poly.Length < 3) return;
            for (var i = 0; i < poly.Length; i++)
            {
                var dir = poly[i] - center;
                var len = math.length(dir);
                if (len > 0.001f)
                {
                    var move = amount > 0 ? math.min(len - 0.01f, amount) : amount;
                    // Здесь квантование не обязательно, это чисто визуал
                    poly[i] = center + dir / len * (len - move);
                }
            }
        }

        public static void ApplySmoothing(ref NativeList<float2> poly, int iterations)
        {
            if (iterations <= 0 || poly.Length < 3) return;
            var temp = new NativeList<float2>(poly.Length * 2, Allocator.Temp);
            for (var iter = 0; iter < iterations; iter++)
            {
                temp.Clear();
                var count = poly.Length;
                for (var i = 0; i < count; i++)
                {
                    var p0 = poly[i];
                    var p1 = poly[(i + 1) % count];
                    temp.Add(math.lerp(p0, p1, 0.25f));
                    temp.Add(math.lerp(p0, p1, 0.75f));
                }

                poly.Clear();
                poly.AddRange(temp.AsArray());
            }

            temp.Dispose();
        }

        private static bool IsNaN(float2 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y);
        }

        // === СОРТИРОВКА ===
        public struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;

            public ClockwiseComparer(float2 center)
            {
                _center = center;
            }

            public int Compare(float2 a, float2 b)
            {
                if (IsNaN(a) || IsNaN(b)) return 0;
                return math.atan2(a.y - _center.y, a.x - _center.x)
                    .CompareTo(math.atan2(b.y - _center.y, b.x - _center.x));
            }
        }
    }
}