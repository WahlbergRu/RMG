using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class NativeCollectionsExtensions
    {
        public static bool Contains<T>(this NativeList<T> list, T value) 
            where T : unmanaged, System.IEquatable<T>
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].Equals(value))
                    return true;
            }
            return false;
        }

        public static void AddUnique<T>(this NativeList<T> list, T value)
            where T : unmanaged, System.IEquatable<T>
        {
            if (!list.Contains(value))
                list.Add(value);
        }

        public static bool IsPointInCircle(float2 point, float2 center, float radius)
        {
            return math.lengthsq(point - center) <= radius * radius;
        }

        public static bool CalculateCircumCircle(float2 p1, float2 p2, float2 p3, out float2 center, out float radius)
        {
            center = float2.zero;
            radius = 0f;

            float D = 2 * (p1.x * (p2.y - p3.y) + p2.x * (p3.y - p1.y) + p3.x * (p1.y - p2.y));
            if (math.abs(D) < 1e-10f)
                return false;

            float ux = ((p1.x * p1.x + p1.y * p1.y) * (p2.y - p3.y) +
                       (p2.x * p2.x + p2.y * p2.y) * (p3.y - p1.y) +
                       (p3.x * p3.x + p3.y * p3.y) * (p1.y - p2.y)) / D;

            float uy = ((p1.x * p1.x + p1.y * p1.y) * (p3.x - p2.x) +
                       (p2.x * p2.x + p2.y * p2.y) * (p1.x - p3.x) +
                       (p3.x * p3.x + p3.y * p3.y) * (p2.x - p1.x)) / D;

            center = new float2(ux, uy);
            radius = math.distance(center, p1);

            return true;
        }
    }
}