using Unity.Collections;
using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class PolygonTriangulation
    {
        /// <summary>
        /// Триангулирует многоугольник против часовой стрелки
        /// </summary>
        public static void TriangulatePolygon(NativeArray<float2> polygon, NativeList<int> indices)
        {
            indices.Clear();
            
            int n = polygon.Length;
            if (n < 3) return;
            
            // Нормализуем порядок вершин (против часовой стрелки)
            if (IsClockwise(polygon))
            {
                ReversePolygon(polygon);
            }
            
            // Для выпуклых многоугольников используем простую триангуляцию
            if (IsConvexPolygon(polygon))
            {
                for (int i = 1; i < n - 1; i++)
                {
                    indices.Add(0);
                    indices.Add(i);
                    indices.Add(i + 1);
                }
                return;
            }
            
            // Для невыпуклых используем алгоритм ear clipping
            EarClippingTriangulation(polygon, indices);
        }
        
        private static bool IsClockwise(NativeArray<float2> polygon)
        {
            float area = 0;
            int n = polygon.Length;
            
            for (int i = 0; i < n; i++)
            {
                float2 current = polygon[i];
                float2 next = polygon[(i + 1) % n];
                area += (next.x - current.x) * (next.y + current.y);
            }
            
            return area > 0;
        }
        
        private static void ReversePolygon(NativeArray<float2> polygon)
        {
            int n = polygon.Length;
            for (int i = 0; i < n / 2; i++)
            {
                float2 temp = polygon[i];
                polygon[i] = polygon[n - 1 - i];
                polygon[n - 1 - i] = temp;
            }
        }
        
        private static bool IsConvexPolygon(NativeArray<float2> polygon)
        {
            int n = polygon.Length;
            bool sign = false;
            
            for (int i = 0; i < n; i++)
            {
                float2 a = polygon[(i + n - 1) % n];
                float2 b = polygon[i];
                float2 c = polygon[(i + 1) % n];
                
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                
                if (i == 0)
                {
                    sign = cross > 0;
                }
                else if ((cross > 0) != sign)
                {
                    return false;
                }
            }
            
            return true;
        }
        
        private static void EarClippingTriangulation(NativeArray<float2> polygon, NativeList<int> indices)
        {
            int n = polygon.Length;
            var polygonIndices = new NativeList<int>(n, Allocator.Temp);
            
            // Инициализируем индексы многоугольника
            for (int k = 0; k < n; k++)
            {
                polygonIndices.Add(k);
            }
            
            int count = n;
            int i = 0;
            
            while (count > 3)
            {
                int prevIndex = (i + count - 1) % count;
                int currIndex = i;
                int nextIndex = (i + 1) % count;
                
                int prev = polygonIndices[prevIndex];
                int curr = polygonIndices[currIndex];
                int next = polygonIndices[nextIndex];
                
                // Проверяем, является ли текущая вершина "ушком"
                if (IsEar(prev, curr, next, polygon, polygonIndices))
                {
                    // Добавляем треугольник
                    indices.Add(prev);
                    indices.Add(curr);
                    indices.Add(next);
                    
                    // Удаляем вершину из многоугольника
                    polygonIndices.RemoveAt(i);
                    count--;
                    
                    // Сбрасываем счетчик, чтобы проверить новые вершины
                    i = 0;
                }
                else
                {
                    i = (i + 1) % count;
                }
            }
            
            // Добавляем последний треугольник
            indices.Add(polygonIndices[0]);
            indices.Add(polygonIndices[1]);
            indices.Add(polygonIndices[2]);
            
            polygonIndices.Dispose();
        }
        
        private static bool IsEar(int prev, int curr, int next, NativeArray<float2> polygon, NativeList<int> polygonIndices)
        {
            float2 a = polygon[prev];
            float2 b = polygon[curr];
            float2 c = polygon[next];
            
            // Проверяем, что угол выпуклый
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (cross <= 0)
                return false;
            
            // Проверяем, что внутри треугольника нет других вершин
            for (int i = 0; i < polygonIndices.Length; i++)
            {
                int index = polygonIndices[i];
                if (index != prev && index != curr && index != next)
                {
                    if (PointInTriangle(polygon[index], a, b, c))
                        return false;
                }
            }
            
            return true;
        }
        
        private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            float area = 0.5f * (-b.y * c.x + a.y * (-b.x + c.x) + a.x * (b.y - c.y) + b.x * c.y);
            
            float s = 1 / (2 * area) * (a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y);
            float t = 1 / (2 * area) * (a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y);
            
            return s >= 0 && t >= 0 && (s + t) <= 1;
        }
    }
}