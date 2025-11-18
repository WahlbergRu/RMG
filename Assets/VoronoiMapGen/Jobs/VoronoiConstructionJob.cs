using Unity.Burst;
using Unity.Collections;
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
        [ReadOnly] public float2 MapSize;

        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // Создаем ячейки для текущего уровня
            CreateCellsForLevel();
            
            // Строим рёбра Вороной
            BuildVoronoiEdges();
        }

        private void CreateCellsForLevel()
        {
            for (int i = 0; i < Sites.Length; i++)
            {
                if (SiteMetadata[i].Level != Level) continue;

                Cells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = Sites[i],
                    RegionIndex = i,
                    Level = Level,
                    ParentRegionIndex = SiteMetadata[i].ParentIndex,
                    Value = SiteMetadata[i].Value
                });
            }
        }

        private void BuildVoronoiEdges()
        {
            int estimatedEdgeCount = Triangles.Length * 3;
            Edges.Capacity = math.max(Edges.Capacity, estimatedEdgeCount);

            // Используем NativeHashMap<int2, NativeList<int2>> вместо NativeMultiHashMap
            // Но так как мы в job, нам нужно использовать более простую структуру
            // Создаем буфер для всех рёбер
            using var edgeBuffer = new NativeList<EdgeTriangleInfo>(estimatedEdgeCount * 2, Allocator.Temp);
            using var processedEdges = new NativeHashSet<int2>(estimatedEdgeCount, Allocator.Temp);

            // Собираем все рёбра из треугольников Делоне
            CollectDelaunayEdges(edgeBuffer);

            // Обрабатываем каждое уникальное ребро
            for (int i = 0; i < edgeBuffer.Length; i++)
            {
                EdgeTriangleInfo edgeInfo = edgeBuffer[i];
                int2 edge = edgeInfo.Edge;
                
                if (processedEdges.Contains(edge)) continue;

                // Находим все треугольники для этого ребра
                NativeList<int2> trianglesForEdge = new NativeList<int2>(4, Allocator.Temp);
                for (int j = 0; j < edgeBuffer.Length; j++)
                {
                    if (edgeBuffer[j].Edge.Equals(edge))
                    {
                        trianglesForEdge.Add(edgeBuffer[j].TriangleInfo);
                    }
                }

                if (trianglesForEdge.Length > 0)
                {
                    int2 firstTriangle = trianglesForEdge[0];
                    bool hasSecondTriangle = trianglesForEdge.Length > 1;
                    int2 secondTriangle = hasSecondTriangle ? trianglesForEdge[1] : int2.zero;

                    if (hasSecondTriangle)
                    {
                        // Внутреннее ребро между двумя ячейками
                        ProcessInternalEdge(edge, firstTriangle, secondTriangle);
                    }
                    else
                    {
                        // Граничное ребро
                        ProcessBoundaryEdge(edge, firstTriangle);
                    }
                }

                trianglesForEdge.Dispose();
                processedEdges.Add(edge);
            }
        }

        private struct EdgeTriangleInfo
        {
            public int2 Edge;
            public int2 TriangleInfo; // x = индекс треугольника, y = тип вершины
        }

        private void CollectDelaunayEdges(NativeList<EdgeTriangleInfo> edgeBuffer)
        {
            for (int i = 0; i < Triangles.Length; i++)
            {
                DelaunayTriangle tri = Triangles[i];
                
                // Проверяем, что все вершины принадлежат текущему уровню
                if (SiteMetadata[tri.A].Level != Level || 
                    SiteMetadata[tri.B].Level != Level || 
                    SiteMetadata[tri.C].Level != Level)
                {
                    continue;
                }

                // Ребро AB
                int2 edgeAB = new int2(math.min(tri.A, tri.B), math.max(tri.A, tri.B));
                edgeBuffer.Add(new EdgeTriangleInfo { Edge = edgeAB, TriangleInfo = new int2(i, 0) });

                // Ребро BC
                int2 edgeBC = new int2(math.min(tri.B, tri.C), math.max(tri.B, tri.C));
                edgeBuffer.Add(new EdgeTriangleInfo { Edge = edgeBC, TriangleInfo = new int2(i, 1) });

                // Ребро CA
                int2 edgeCA = new int2(math.min(tri.C, tri.A), math.max(tri.C, tri.A));
                edgeBuffer.Add(new EdgeTriangleInfo { Edge = edgeCA, TriangleInfo = new int2(i, 2) });
            }
        }

        private void ProcessInternalEdge(int2 edge, int2 firstTriInfo, int2 secondTriInfo)
        {
            DelaunayTriangle tri1 = Triangles[firstTriInfo.x];
            DelaunayTriangle tri2 = Triangles[secondTriInfo.x];

            // Определяем, какая вершина не принадлежит ребру
            int oppositeVertex1 = GetOppositeVertex(tri1, edge);
            int oppositeVertex2 = GetOppositeVertex(tri2, edge);

            if (oppositeVertex1 == -1 || oppositeVertex2 == -1) return;

            Edges.Add(new VoronoiEdge
            {
                SiteA = oppositeVertex1,
                SiteB = oppositeVertex2,
                VertexA = tri1.CircumCenter,
                VertexB = tri2.CircumCenter,
                CellA = Entity.Null,
                CellB = Entity.Null,
                Level = Level
            });
        }

        private void ProcessBoundaryEdge(int2 edge, int2 triInfo)
        {
            DelaunayTriangle tri = Triangles[triInfo.x];
            int oppositeVertex = GetOppositeVertex(tri, edge);

            if (oppositeVertex == -1) return;

            // Расширяем граничное ребро безопасно
            float2 extendedVertex = ExtendBoundaryEdgeSafely(tri.CircumCenter, Sites[edge.x], Sites[edge.y]);

            Edges.Add(new VoronoiEdge
            {
                SiteA = oppositeVertex,
                SiteB = edge.x, // Одна из вершин ребра
                VertexA = tri.CircumCenter,
                VertexB = extendedVertex,
                CellA = Entity.Null,
                CellB = Entity.Null,
                Level = Level
            });
        }

        private int GetOppositeVertex(DelaunayTriangle tri, int2 edge)
        {
            if (tri.A != edge.x && tri.A != edge.y) return tri.A;
            if (tri.B != edge.x && tri.B != edge.y) return tri.B;
            if (tri.C != edge.x && tri.C != edge.y) return tri.C;
            return -1;
        }

        private float2 ExtendBoundaryEdgeSafely(float2 circumCenter, float2 siteA, float2 siteB)
        {
            // Вычисляем направление ребра
            float2 edgeDir = math.normalize(siteB - siteA);
            // Перпендикуляр к ребру
            float2 perpDir = new float2(-edgeDir.y, edgeDir.x);
            
            // Ограничиваем длину расширения картой
            float maxExtension = math.max(MapSize.x, MapSize.y) * 1.5f;
            float2 extended = circumCenter + perpDir * maxExtension;
            
            // Обрезаем по границам карты с отступом
            extended.x = math.clamp(extended.x, -maxExtension * 0.1f, MapSize.x + maxExtension * 0.1f);
            extended.y = math.clamp(extended.y, -maxExtension * 0.1f, MapSize.y + maxExtension * 0.1f);
            
            return extended;
        }
    }
}