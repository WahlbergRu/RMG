using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct VoronoiConstructionJob : IJob
    {
        [ReadOnly] public NativeArray<DelaunayTriangle> Triangles;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMetadata;
        [ReadOnly] public int Level;

        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // === 1. Создаём ячейки текущего уровня ===
            for (int i = 0; i < Sites.Length; i++)
            {
                if (SiteMetadata[i].Level != Level) continue;

                Cells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = Sites[i],
                    RegionIndex = i,
                    Level = Level,
                    ParentRegionIndex = SiteMetadata[i].ParentIndex
                });
            }

            // === 2. Предварительно выделяем буфер для рёбер ===
            int estimatedEdgeCount = Triangles.Length * 3 / 2; // грубая оценка
            Edges.Capacity = math.max(Edges.Capacity, estimatedEdgeCount);

            // === 3. Строим рёбра ===
            NativeParallelMultiHashMap<int2, int> edgeToTriangleIndices = new NativeParallelMultiHashMap<int2, int>(estimatedEdgeCount, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle triangle = Triangles[i];

                if (SiteMetadata[triangle.A].Level != Level ||
                    SiteMetadata[triangle.B].Level != Level ||
                    SiteMetadata[triangle.C].Level != Level)
                    continue;

                ProcessTriangleEdges(i, triangle, edgeToTriangleIndices);
            }

            // === 4. Обрабатываем рёбра: внутренние и граничные ===
            NativeHashSet<int2> processedEdges = new NativeHashSet<int2>(estimatedEdgeCount, Allocator.Temp);

            foreach (KeyValue<int2, int> kvp in edgeToTriangleIndices)
            {
                int2 edge = kvp.Key;
                int triangleIndex = kvp.Value;

                if (processedEdges.Contains(edge)) continue;

                // Проверяем, есть ли вторая сторона ребра (внутреннее ребро)
                if (edgeToTriangleIndices.TryGetFirstValue(edge, out int firstTri, out NativeParallelMultiHashMapIterator<int2> it))
                {
                    bool foundSecond = edgeToTriangleIndices.TryGetNextValue(out int secondTri, ref it);

                    if (foundSecond)
                    {
                        // Внутреннее ребро: между двумя треугольниками
                        DelaunayTriangle tri1 = Triangles[firstTri];
                        DelaunayTriangle tri2 = Triangles[secondTri];

                        Edges.Add(new VoronoiEdge
                        {
                            SiteA = edge.x,
                            SiteB = edge.y,
                            VertexA = tri1.CircumCenter,
                            VertexB = tri2.CircumCenter,
                            CellA = Entity.Null,
                            CellB = Entity.Null,
                            Level = Level
                        });
                    }
                    else
                    {
                        // Граничное ребро: только один треугольник
                        DelaunayTriangle tri = Triangles[firstTri];

                        Edges.Add(new VoronoiEdge
                        {
                            SiteA = edge.x,
                            SiteB = edge.y,
                            VertexA = tri.CircumCenter,
                            VertexB = ExtendBoundaryEdge(tri.CircumCenter, Sites[edge.x], Sites[edge.y]),
                            CellA = Entity.Null,
                            CellB = Entity.Null,
                            Level = Level
                        });
                    }
                }

                processedEdges.Add(edge);
            }

            processedEdges.Dispose();
            edgeToTriangleIndices.Dispose();
        }

        private void ProcessTriangleEdges(int triangleIndex, DelaunayTriangle triangle,
            NativeParallelMultiHashMap<int2, int> edgeToTriangleIndices)
        {
            // Ребро AB
            int2 edgeAB = new int2(math.min(triangle.A, triangle.B), math.max(triangle.A, triangle.B));
            edgeToTriangleIndices.Add(edgeAB, triangleIndex);

            // Ребро BC
            int2 edgeBC = new int2(math.min(triangle.B, triangle.C), math.max(triangle.B, triangle.C));
            edgeToTriangleIndices.Add(edgeBC, triangleIndex);

            // Ребро CA
            int2 edgeCA = new int2(math.min(triangle.C, triangle.A), math.max(triangle.C, triangle.A));
            edgeToTriangleIndices.Add(edgeCA, triangleIndex);
        }

        private float2 ExtendBoundaryEdge(float2 circumCenter, float2 siteA, float2 siteB)
        {
            float2 edgeDir = math.normalize(siteB - siteA);
            float2 perpDir = new float2(-edgeDir.y, edgeDir.x);
            return circumCenter + perpDir * 1000f;
        }
    }
}