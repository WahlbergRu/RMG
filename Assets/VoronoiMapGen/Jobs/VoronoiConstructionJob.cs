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
        
        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // Создаём ячейки текущего уровня
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

            var edgeToTriangle = new NativeHashMap<int2, int>(Triangles.Length * 3, Allocator.Temp);
            var createdEdges = new NativeHashSet<int2>(Triangles.Length * 3, Allocator.Temp);
            
            // Строим рёбра
            for (int i = 0; i < Triangles.Length; i++)
            {
                var triangle = Triangles[i];
                
                if (SiteMetadata[triangle.A].Level != Level ||
                    SiteMetadata[triangle.B].Level != Level ||
                    SiteMetadata[triangle.C].Level != Level)
                    continue;
                    
                ProcessTriangleEdges(i, triangle, edgeToTriangle, createdEdges);
            }
            
            // Обработка граничных рёбер
            ProcessBoundaryEdges(edgeToTriangle, createdEdges);
            
            edgeToTriangle.Dispose();
            createdEdges.Dispose();
        }
        
        private void ProcessTriangleEdges(int triangleIndex, DelaunayTriangle triangle,
            NativeHashMap<int2, int> edgeToTriangle,
            NativeHashSet<int2> createdEdges)
        {
            AddEdgeIfValid(triangleIndex, triangle.A, triangle.B, triangle.CircumCenter, edgeToTriangle, createdEdges);
            AddEdgeIfValid(triangleIndex, triangle.B, triangle.C, triangle.CircumCenter, edgeToTriangle, createdEdges);
            AddEdgeIfValid(triangleIndex, triangle.C, triangle.A, triangle.CircumCenter, edgeToTriangle, createdEdges);
        }
        
        private void AddEdgeIfValid(int triangleIndex, int a, int b, float2 circumCenter,
            NativeHashMap<int2, int> edgeToTriangle,
            NativeHashSet<int2> createdEdges)
        {
            int2 normalizedEdge = new int2(math.min(a, b), math.max(a, b));
            
            if (SiteMetadata[a].Level != Level || SiteMetadata[b].Level != Level)
                return;
                
            if (edgeToTriangle.TryGetValue(normalizedEdge, out int existingTriangleIndex))
            {
                var existingTriangle = Triangles[existingTriangleIndex];

                if (createdEdges.Add(normalizedEdge))
                {
                    Edges.Add(new VoronoiEdge
                    {
                        SiteA = normalizedEdge.x,
                        SiteB = normalizedEdge.y,
                        VertexA = existingTriangle.CircumCenter,
                        VertexB = circumCenter,
                        CellA = Entity.Null,
                        CellB = Entity.Null,
                        Level = Level
                    });
                }
                
                edgeToTriangle.Remove(normalizedEdge);
            }
            else
            {
                edgeToTriangle.Add(normalizedEdge, triangleIndex);
            }
        }
        
        private void ProcessBoundaryEdges(NativeHashMap<int2, int> edgeToTriangle,
            NativeHashSet<int2> createdEdges)
        {
            foreach (var kvp in edgeToTriangle)
            {
                int2 edge = new int2(math.min(kvp.Key.x, kvp.Key.y), math.max(kvp.Key.x, kvp.Key.y));
                var triangle = Triangles[kvp.Value];

                if (createdEdges.Add(edge))
                {
                    Edges.Add(new VoronoiEdge
                    {
                        SiteA = edge.x,
                        SiteB = edge.y,
                        VertexA = triangle.CircumCenter,
                        VertexB = ExtendBoundaryEdge(triangle.CircumCenter, Sites[edge.x], Sites[edge.y]),
                        CellA = Entity.Null,
                        CellB = Entity.Null,
                        Level = Level
                    });
                }
            }
        }
        
        private float2 ExtendBoundaryEdge(float2 circumCenter, float2 siteA, float2 siteB)
        {
            float2 edgeDir = math.normalize(siteB - siteA);
            float2 perpDir = new float2(-edgeDir.y, edgeDir.x);
            return circumCenter + perpDir * 1000f;
        }
    }
}
