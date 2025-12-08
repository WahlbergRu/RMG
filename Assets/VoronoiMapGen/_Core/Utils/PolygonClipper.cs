using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class PolygonClipper
    {
        // Обрезает полигон по прямоугольнику (0,0) -> (mapSize.x, mapSize.y)
        public static void ClipToRect(ref NativeList<float2> poly, float2 mapSize)
        {
            if (poly.Length < 3) return;

            ClipAxis(ref poly, new float2(1, 0), 0); // Left
            ClipAxis(ref poly, new float2(-1, 0), -mapSize.x); // Right
            ClipAxis(ref poly, new float2(0, 1), 0); // Bottom
            ClipAxis(ref poly, new float2(0, -1), -mapSize.y); // Top
        }

        private static void ClipAxis(ref NativeList<float2> poly, float2 n, float d)
        {
            if (poly.Length == 0) return;
            NativeList<float2> output = new NativeList<float2>(poly.Length + 4, Allocator.Temp);

            for (int i = 0; i < poly.Length; i++)
            {
                float2 curr = poly[i];
                float2 prev = poly[(i + poly.Length - 1) % poly.Length];

                bool currIn = math.dot(curr, n) >= d;
                bool prevIn = math.dot(prev, n) >= d;

                if (currIn)
                {
                    if (!prevIn) output.Add(Intersect(prev, curr, n, d));
                    output.Add(curr);
                }
                else if (prevIn)
                {
                    output.Add(Intersect(prev, curr, n, d));
                }
            }

            poly.Clear();
            poly.AddRange(output.AsArray());
            output.Dispose();
        }

        private static float2 Intersect(float2 a, float2 b, float2 n, float d)
        {
            float t = (d - math.dot(a, n)) / math.dot(b - a, n);
            return a + t * (b - a);
        }
    }
}