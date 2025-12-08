using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen._Core.Utils;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration
{
    [BurstCompile]
    public struct DelaunayTriangulationJob : IJob
    {
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMetadata;
        [ReadOnly] public int Level;

        // MapSize больше не нужен, мы вычислим его из точек!

        public NativeList<DelaunayTriangle> Triangles;
        public NativeList<int3> Edges; // Вспомогательный список

        public void Execute()
        {
            if (Sites.Length < 3) return;

            // 1. Собираем индексы точек текущего уровня
            var levelIndices = new NativeList<int>(Allocator.Temp);
            for (var i = 0; i < Sites.Length; i++)
                if (SiteMetadata[i].Level == Level)
                    levelIndices.Add(i);

            if (levelIndices.Length < 3)
            {
                levelIndices.Dispose();
                return;
            }

            // 2. Вычисляем реальные границы всех точек (включая призраков)
            var min = Sites[levelIndices[0]];
            var max = Sites[levelIndices[0]];

            for (var i = 1; i < levelIndices.Length; i++)
            {
                var p = Sites[levelIndices[i]];
                min = math.min(min, p);
                max = math.max(max, p);
            }

            // 3. Создаем Супер-Треугольник вокруг этих границ
            var superTriangle = CreateSuperTriangle(min, max);

            var extendedSites = new NativeList<float2>(levelIndices.Length + 3, Allocator.Temp);
            for (var i = 0; i < levelIndices.Length; i++) extendedSites.Add(Sites[levelIndices[i]]);

            var superIndexStart = extendedSites.Length;
            extendedSites.Add(superTriangle[0]);
            extendedSites.Add(superTriangle[1]);
            extendedSites.Add(superTriangle[2]);

            var superIndices = new int3(superIndexStart, superIndexStart + 1, superIndexStart + 2);

            Triangles.Add(CreateTriangle(superIndices.x, superIndices.y, superIndices.z, extendedSites));

            // 4. Триангуляция
            for (var i = 0; i < levelIndices.Length; i++) AddPoint(i, extendedSites);

            RemoveSuperTriangleTriangles(superIndices);

            // Восстанавливаем глобальные индексы
            RemapTrianglesToGlobalIndices(levelIndices);

            extendedSites.Dispose();
            levelIndices.Dispose();
        }

        private void RemapTrianglesToGlobalIndices(NativeList<int> globalIndices)
        {
            for (var i = 0; i < Triangles.Length; i++)
            {
                var t = Triangles[i];
                Triangles[i] = new DelaunayTriangle
                {
                    A = globalIndices[t.A],
                    B = globalIndices[t.B],
                    C = globalIndices[t.C],
                    CircumCenter = t.CircumCenter,
                    CircumRadius = t.CircumRadius
                };
            }
        }

        private float2x3 CreateSuperTriangle(float2 min, float2 max)
        {
            var center = (min + max) * 0.5f;
            var size = max - min;
            var maxDim = math.max(size.x, size.y);
            var expansion = 10.0f; // Большой запас, чтобы точно накрыть призраков

            var p1 = center + new float2(-math.sqrt(3) * maxDim * expansion, -maxDim * expansion);
            var p2 = center + new float2(math.sqrt(3) * maxDim * expansion, -maxDim * expansion);
            var p3 = center + new float2(0, 2 * maxDim * expansion);

            return new float2x3(p1, p2, p3);
        }

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeList<float2> sites)
        {
            if (NativeCollectionsExtensions.CalculateCircumCircle(sites[a], sites[b], sites[c], out var center,
                    out var radius))
                return new DelaunayTriangle { A = a, B = b, C = c, CircumCenter = center, CircumRadius = radius };
            return new DelaunayTriangle();
        }

        private void AddPoint(int pointIndex, NativeList<float2> sites)
        {
            var badTriangles = new NativeList<int>(128, Allocator.Temp);
            var polygon = new NativeList<int2>(128, Allocator.Temp);

            for (var i = 0; i < Triangles.Length; i++)
            {
                var triangle = Triangles[i];
                if (NativeCollectionsExtensions.IsPointInCircle(sites[pointIndex], triangle.CircumCenter,
                        triangle.CircumRadius)) badTriangles.Add(i);
            }

            for (var i = 0; i < badTriangles.Length; i++)
            {
                var triangleIndex = badTriangles[i];
                var triangle = Triangles[triangleIndex];
                CheckAndAddEdge(triangle.A, triangle.B, badTriangles, polygon);
                CheckAndAddEdge(triangle.B, triangle.C, badTriangles, polygon);
                CheckAndAddEdge(triangle.C, triangle.A, badTriangles, polygon);
            }

            for (var i = badTriangles.Length - 1; i >= 0; i--) Triangles.RemoveAtSwapBack(badTriangles[i]);

            for (var i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[i];
                Triangles.Add(CreateTriangle(edge.x, edge.y, pointIndex, sites));
            }

            polygon.Dispose();
            badTriangles.Dispose();
        }

        private void CheckAndAddEdge(int a, int b, NativeList<int> badTriangles, NativeList<int2> polygon)
        {
            var isShared = false;
            for (var i = 0; i < badTriangles.Length; i++)
            {
                var triangleIndex = badTriangles[i];
                var triangle = Triangles[triangleIndex];
                if ((triangle.A == a && triangle.B == b) || (triangle.A == b && triangle.B == a) ||
                    (triangle.B == a && triangle.C == b) || (triangle.B == b && triangle.C == a) ||
                    (triangle.C == a && triangle.A == b) || (triangle.C == b && triangle.A == a))
                {
                    for (var j = 0; j < polygon.Length; j++)
                    {
                        var existingEdge = polygon[j];
                        if ((existingEdge.x == a && existingEdge.y == b) ||
                            (existingEdge.x == b && existingEdge.y == a))
                        {
                            polygon.RemoveAtSwapBack(j);
                            isShared = true;
                            break;
                        }
                    }

                    if (isShared) break;
                }
            }

            if (!isShared) polygon.Add(new int2(a, b));
        }

        private void RemoveSuperTriangleTriangles(int3 superIndices)
        {
            for (var i = Triangles.Length - 1; i >= 0; i--)
            {
                var triangle = Triangles[i];
                if (triangle.A >= superIndices.x || triangle.B >= superIndices.x || triangle.C >= superIndices.x)
                    Triangles.RemoveAtSwapBack(i);
            }
        }
    }
}