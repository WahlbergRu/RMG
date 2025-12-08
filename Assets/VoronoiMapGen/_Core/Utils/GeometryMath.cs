using Unity.Mathematics;

namespace VoronoiMapGen.Utils
{
    public static class GeometryMath
    {
        // Проверка: лежит ли точка P внутри описанной окружности треугольника ABC?
        public static bool IsPointInCircumCircle(float2 p, float2 a, float2 b, float2 c)
        {
            // Упрощенная проверка без вычисления радиуса (через детерминант)
            // Но для стабильности используем классический метод
            float2 center;
            float rSq;
            if (!GetCircumcenter(a, b, c, out center, out rSq)) return false;
            return math.distancesq(p, center) < rSq;
        }

        // Находит центр описанной окружности и квадрат радиуса
        public static bool GetCircumcenter(float2 a, float2 b, float2 c, out float2 center, out float rSq)
        {
            float D = 2 * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));

            if (math.abs(D) < 1e-5f) // Коллинеарные точки (вырожденный треугольник)
            {
                center = float2.zero;
                rSq = 0;
                return false;
            }

            float ux = ((a.x * a.x + a.y * a.y) * (b.y - c.y) +
                        (b.x * b.x + b.y * b.y) * (c.y - a.y) +
                        (c.x * c.x + c.y * c.y) * (a.y - b.y)) / D;

            float uy = ((a.x * a.x + a.y * a.y) * (c.x - b.x) +
                        (b.x * b.x + b.y * b.y) * (a.x - c.x) +
                        (c.x * c.x + c.y * c.y) * (b.x - a.x)) / D;

            center = new float2(ux, uy);
            rSq = math.distancesq(center, a);
            return true;
        }
    }
}