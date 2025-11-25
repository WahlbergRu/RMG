using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class PolygonClipper
    {
        // Обрезает полигон по прямоугольнику от (0,0) до (mapSize.x, mapSize.y)
        public static void ClipPolygonToMapBounds(ref NativeList<float2> polygon, float2 mapSize)
        {
            if (polygon.Length < 3) return;

            // 4 плоскости отсечения: Лево, Право, Низ, Верх
            ClipEdge(ref polygon, new float2(1, 0), 0);           // Left (x > 0)
            ClipEdge(ref polygon, new float2(-1, 0), -mapSize.x); // Right (x < width) -> -x > -width
            ClipEdge(ref polygon, new float2(0, 1), 0);           // Bottom (y > 0)
            ClipEdge(ref polygon, new float2(0, -1), -mapSize.y); // Top (y < height) -> -y > -height
        }

        private static void ClipEdge(ref NativeList<float2> polygon, float2 normal, float distance)
        {
            if (polygon.Length == 0) return;

            // Используем временный список для хранения вершин текущего этапа обрезки
            var newPoly = new NativeList<float2>(polygon.Length + 4, Allocator.Temp);

            for (int i = 0; i < polygon.Length; i++)
            {
                float2 current = polygon[i];
                float2 prev = polygon[(i + polygon.Length - 1) % polygon.Length];

                // Проверяем, находятся ли точки внутри допустимой области
                bool currInside = math.dot(current, normal) >= distance;
                bool prevInside = math.dot(prev, normal) >= distance;

                if (currInside)
                {
                    if (!prevInside)
                    {
                        // Входим в область -> добавляем точку пересечения
                        newPoly.Add(ComputeIntersection(prev, current, normal, distance));
                    }
                    newPoly.Add(current);
                }
                else if (prevInside)
                {
                    // Выходим из области -> добавляем точку пересечения
                    newPoly.Add(ComputeIntersection(prev, current, normal, distance));
                }
            }

            // Копируем результат обратно
            polygon.Clear();
            polygon.AddRange(newPoly);
            newPoly.Dispose();
        }

        private static float2 ComputeIntersection(float2 a, float2 b, float2 n, float d)
        {
            float t = (d - math.dot(a, n)) / (math.dot(b - a, n));
            return a + t * (b - a);
        }
    }
}