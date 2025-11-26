using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class PolygonUtils
    {
        // === 1. SORTING ===
        public struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;
            public ClockwiseComparer(float2 center) => _center = center;
            public int Compare(float2 a, float2 b)
            {
                float angA = math.atan2(a.y - _center.y, a.x - _center.x);
                float angB = math.atan2(b.y - _center.y, b.x - _center.x);
                return angA.CompareTo(angB);
            }
        }

        // === 2. CLIPPING (Map Bounds) ===
        public static void ClipToBounds(ref NativeList<float2> polygon, float2 mapSize)
        {
            if (polygon.Length < 3) return;
            ClipEdge(ref polygon, new float2(1, 0), 0);           // Left
            ClipEdge(ref polygon, new float2(-1, 0), -mapSize.x); // Right
            ClipEdge(ref polygon, new float2(0, 1), 0);           // Bottom
            ClipEdge(ref polygon, new float2(0, -1), -mapSize.y); // Top
        }

        // === 3. CLIPPING (Polygon vs Polygon) ===
        public static void ClipToPolygon(ref NativeList<float2> subject, NativeArray<float3> clipper)
        {
            if (subject.Length < 3 || clipper.Length < 3) return;

            var output = new NativeList<float2>(subject.Length + 4, Allocator.Temp);
            output.AddRange(subject.AsArray());
            var input = new NativeList<float2>(subject.Length + 4, Allocator.Temp);

            int len = clipper.Length;
            for (int i = 0; i < len; i++)
            {
                float2 a = new float2(clipper[i].x, clipper[i].z);
                float2 b = new float2(clipper[(i + 1) % len].x, clipper[(i + 1) % len].z);
                float2 edge = b - a;
                float2 normal = new float2(-edge.y, edge.x);

                if (math.lengthsq(edge) < 1e-6f) continue;

                input.Clear();
                input.AddRange(output.AsArray());
                output.Clear();

                if (input.Length == 0) break;

                float2 S = input[input.Length - 1];
                for (int j = 0; j < input.Length; j++)
                {
                    float2 E = input[j];
                    if (IsInside(E, a, normal))
                    {
                        if (!IsInside(S, a, normal)) output.Add(Intersection(S, E, a, normal));
                        output.Add(E);
                    }
                    else if (IsInside(S, a, normal))
                    {
                        output.Add(Intersection(S, E, a, normal));
                    }
                    S = E;
                }
            }
            
            subject.Clear();
            subject.AddRange(output.AsArray());
            output.Dispose();
            input.Dispose();
        }

        // === 4. INSET (Shrink) ===
        public static void ApplyInset(ref NativeList<float2> poly, float2 center, float amount)
        {
            if (amount <= 0.01f) return;
            for (int i = 0; i < poly.Length; i++)
            {
                float2 dir = poly[i] - center;
                float dist = math.length(dir);
                if (dist > amount)
                {
                    poly[i] = center + (dir / dist) * (dist - amount);
                }
            }
        }

        // === 5. SMOOTHING (Chaikin) ===
        public static void ApplySmoothing(ref NativeList<float2> poly, int iterations)
        {
            if (iterations <= 0 || poly.Length < 3) return;
            var temp = new NativeList<float2>(poly.Length * 2, Allocator.Temp);

            for (int iter = 0; iter < iterations; iter++)
            {
                temp.Clear();
                int count = poly.Length;
                for (int i = 0; i < count; i++)
                {
                    float2 p0 = poly[i];
                    float2 p1 = poly[(i + 1) % count];
                    temp.Add(math.lerp(p0, p1, 0.25f));
                    temp.Add(math.lerp(p0, p1, 0.75f));
                }
                poly.Clear();
                poly.AddRange(temp.AsArray());
            }
            temp.Dispose();
        }

        // --- Helpers ---
        private static void ClipEdge(ref NativeList<float2> poly, float2 n, float d)
        {
            var newPoly = new NativeList<float2>(poly.Length + 4, Allocator.Temp);
            for (int i = 0; i < poly.Length; i++)
            {
                float2 curr = poly[i];
                float2 prev = poly[(i + poly.Length - 1) % poly.Length];
                bool currIn = math.dot(curr, n) >= d;
                bool prevIn = math.dot(prev, n) >= d;

                if (currIn) {
                    if (!prevIn) newPoly.Add(Intersection(prev, curr, n, d));
                    newPoly.Add(curr);
                } else if (prevIn) {
                    newPoly.Add(Intersection(prev, curr, n, d));
                }
            }
            poly.Clear();
            poly.AddRange(newPoly.AsArray());
            newPoly.Dispose();
        }

        private static float2 Intersection(float2 a, float2 b, float2 n, float d)
        {
            float t = (d - math.dot(a, n)) / (math.dot(b - a, n));
            return a + t * (b - a);
        }
        
        private static float2 Intersection(float2 a, float2 b, float2 origin, float2 normal)
        {
            float t = math.dot(origin - a, normal) / math.dot(b - a, normal);
            return a + t * (b - a);
        }

        private static bool IsInside(float2 p, float2 origin, float2 normal) => math.dot(p - origin, normal) >= 0;
    }
}