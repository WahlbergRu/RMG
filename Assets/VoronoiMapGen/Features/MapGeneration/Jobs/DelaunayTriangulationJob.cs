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
            NativeList<int> levelIndices = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < Sites.Length; i++)
                if (SiteMetadata[i].Level == Level)
                    levelIndices.Add(i);

            if (levelIndices.Length < 3)
            {
                levelIndices.Dispose();
                return;
            }

            // 2. Вычисляем реальные границы всех точек (включая призраков)
            float2 min = Sites[levelIndices[0]];
            float2 max = Sites[levelIndices[0]];

            for (int i = 1; i < levelIndices.Length; i++)
            {
                float2 p = Sites[levelIndices[i]];
                min = math.min(min, p);
                max = math.max(max, p);
            }

            // 3. Создаем Супер-Треугольник вокруг этих границ
            float2x3 superTriangle = CreateSuperTriangle(min, max);

            NativeList<float2> extendedSites = new NativeList<float2>(levelIndices.Length + 3, Allocator.Temp);
            for (int i = 0; i < levelIndices.Length; i++) extendedSites.Add(Sites[levelIndices[i]]);

            int superIndexStart = extendedSites.Length;
            extendedSites.Add(superTriangle[0]);
            extendedSites.Add(superTriangle[1]);
            extendedSites.Add(superTriangle[2]);

            int3 superIndices = new int3(superIndexStart, superIndexStart + 1, superIndexStart + 2);

            Triangles.Add(CreateTriangle(superIndices.x, superIndices.y, superIndices.z, extendedSites));

            // 4. Триангуляция
            for (int i = 0; i < levelIndices.Length; i++) AddPoint(i, extendedSites);

            RemoveSuperTriangleTriangles(superIndices);

            // Восстанавливаем глобальные индексы
            RemapTrianglesToGlobalIndices(levelIndices);

            extendedSites.Dispose();
            levelIndices.Dispose();
        }

        private void RemapTrianglesToGlobalIndices(NativeList<int> globalIndices)
        {
            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle t = Triangles[i];
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
            float2 center = (min + max) * 0.5f;
            float2 size = max - min;
            float maxDim = math.max(size.x, size.y);
            float expansion = 10.0f; // Большой запас, чтобы точно накрыть призраков

            float2 p1 = center + new float2(-math.sqrt(3) * maxDim * expansion, -maxDim * expansion);
            float2 p2 = center + new float2(math.sqrt(3) * maxDim * expansion, -maxDim * expansion);
            float2 p3 = center + new float2(0, 2 * maxDim * expansion);

            return new float2x3(p1, p2, p3);
        }

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeList<float2> sites)
        {
            if (NativeCollectionsExtensions.CalculateCircumCircle(sites[a], sites[b], sites[c], out float2 center,
                    out float radius))
                return new DelaunayTriangle { A = a, B = b, C = c, CircumCenter = center, CircumRadius = radius };
            return new DelaunayTriangle();
        }

        private void AddPoint(int pointIndex, NativeList<float2> sites)
        {
            NativeList<int> badTriangles = new NativeList<int>(128, Allocator.Temp);
            NativeList<int2> polygon = new NativeList<int2>(128, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle triangle = Triangles[i];
                if (NativeCollectionsExtensions.IsPointInCircle(sites[pointIndex], triangle.CircumCenter,
                        triangle.CircumRadius)) badTriangles.Add(i);
            }

            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triangleIndex = badTriangles[i];
                DelaunayTriangle triangle = Triangles[triangleIndex];
                CheckAndAddEdge(triangle.A, triangle.B, badTriangles, polygon);
                CheckAndAddEdge(triangle.B, triangle.C, badTriangles, polygon);
                CheckAndAddEdge(triangle.C, triangle.A, badTriangles, polygon);
            }

            for (int i = badTriangles.Length - 1; i >= 0; i--) Triangles.RemoveAtSwapBack(badTriangles[i]);

            for (int i = 0; i < polygon.Length; i++)
            {
                int2 edge = polygon[i];
                Triangles.Add(CreateTriangle(edge.x, edge.y, pointIndex, sites));
            }

            polygon.Dispose();
            badTriangles.Dispose();
        }

        private void CheckAndAddEdge(int a, int b, NativeList<int> badTriangles, NativeList<int2> polygon)
        {
            bool isShared = false;
            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triangleIndex = badTriangles[i];
                DelaunayTriangle triangle = Triangles[triangleIndex];
                if ((triangle.A == a && triangle.B == b) || (triangle.A == b && triangle.B == a) ||
                    (triangle.B == a && triangle.C == b) || (triangle.B == b && triangle.C == a) ||
                    (triangle.C == a && triangle.A == b) || (triangle.C == b && triangle.A == a))
                {
                    for (int j = 0; j < polygon.Length; j++)
                    {
                        int2 existingEdge = polygon[j];
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
            for (int i = Triangles.Length - 1; i >= 0; i--)
            {
                DelaunayTriangle triangle = Triangles[i];
                if (triangle.A >= superIndices.x || triangle.B >= superIndices.x || triangle.C >= superIndices.x)
                    Triangles.RemoveAtSwapBack(i);
            }
        }
    }
}