using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct DelaunayTriangulationJob : IJob
    {
        [ReadOnly] public NativeArray<float2> Sites;
        public NativeList<DelaunayTriangle> Triangles;
        public NativeList<int3> Edges;

        public void Execute()
        {
            if (Sites.Length < 3)
                return;

            var bounds = CalculateBounds(Sites);
            var superTriangle = CreateSuperTriangle(bounds[0], bounds[1]);

            var extendedSites = new NativeList<float2>(Sites.Length + 3, Allocator.Temp);
            extendedSites.AddRange(Sites);
            extendedSites.Add(superTriangle[0]);
            extendedSites.Add(superTriangle[1]);
            extendedSites.Add(superTriangle[2]);

            var superIndices = new int3(Sites.Length, Sites.Length + 1, Sites.Length + 2);

            Triangles.Add(CreateTriangle(superIndices.x, superIndices.y, superIndices.z, extendedSites));

            for (int i = 0; i < Sites.Length; i++)
            {
                AddPoint(i, extendedSites);
            }

            RemoveSuperTriangleTriangles(superIndices);

            extendedSites.Dispose();
        }

        private float2x3 CreateSuperTriangle(float2 min, float2 max)
        {
            float2 center = (min + max) * 0.5f;
            float2 size = max - min;
            float maxDim = math.max(size.x, size.y);

            float2 p1 = center + new float2(-2 * maxDim, -maxDim);
            float2 p2 = center + new float2(0, 2 * maxDim);
            float2 p3 = center + new float2(2 * maxDim, -maxDim);

            return new float2x3(p1, p2, p3);
        }

        private float2x2 CalculateBounds(NativeArray<float2> sites)
        {
            float2 min = sites[0];
            float2 max = sites[0];

            for (int i = 1; i < sites.Length; i++)
            {
                min = math.min(min, sites[i]);
                max = math.max(max, sites[i]);
            }

            return new float2x2(min, max);
        }

        private DelaunayTriangle CreateTriangle(int a, int b, int c, NativeList<float2> sites)
        {
            if (Utils.NativeCollectionsExtensions.CalculateCircumCircle(sites[a], sites[b], sites[c], out float2 center, out float radius))
            {
                return new DelaunayTriangle
                {
                    A = a,
                    B = b,
                    C = c,
                    CircumCenter = center,
                    CircumRadius = radius
                };
            }

            return new DelaunayTriangle();
        }

        private void AddPoint(int pointIndex, NativeList<float2> sites)
        {
            var badTriangles = new NativeList<int>(128, Allocator.Temp);
            var polygon = new NativeList<int2>(128, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                var triangle = Triangles[i];
                if (Utils.NativeCollectionsExtensions.IsPointInCircle(sites[pointIndex], triangle.CircumCenter, triangle.CircumRadius))
                {
                    badTriangles.Add(i);
                }
            }

            for (int i = 0; i < badTriangles.Length; i++)
            {
                int triangleIndex = badTriangles[i];
                var triangle = Triangles[triangleIndex];

                CheckAndAddEdge(triangle.A, triangle.B, badTriangles, polygon);
                CheckAndAddEdge(triangle.B, triangle.C, badTriangles, polygon);
                CheckAndAddEdge(triangle.C, triangle.A, badTriangles, polygon);
            }

            for (int i = badTriangles.Length - 1; i >= 0; i--)
            {
                Triangles.RemoveAtSwapBack(badTriangles[i]);
            }

            for (int i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[i];
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
                var triangle = Triangles[triangleIndex];
                
                if ((triangle.A == a && triangle.B == b) || (triangle.A == b && triangle.B == a) ||
                    (triangle.B == a && triangle.C == b) || (triangle.B == b && triangle.C == a) ||
                    (triangle.C == a && triangle.A == b) || (triangle.C == b && triangle.A == a))
                {
                    for (int j = 0; j < polygon.Length; j++)
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

            if (!isShared)
            {
                polygon.Add(new int2(a, b));
            }
        }

        private void RemoveSuperTriangleTriangles(int3 superIndices)
        {
            for (int i = Triangles.Length - 1; i >= 0; i--)
            {
                var triangle = Triangles[i];
                if (triangle.A >= superIndices.x || triangle.B >= superIndices.x || triangle.C >= superIndices.x)
                {
                    Triangles.RemoveAtSwapBack(i);
                }
            }
        }
    }
}