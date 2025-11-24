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
        [ReadOnly] public int Level;

        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // 1. Генерация ячеек
            for (int i = 0; i < Sites.Length; i++)
            {
                Cells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = Sites[i],
                    RegionIndex = i,
                    Level = Level,
                    ParentRegionIndex = -1,
                    Value = 0
                });
            }

            // 2. Генерация ребер (БЕЗ ОБРЕЗКИ)
            for (int i = 0; i < Triangles.Length; i++)
            {
                var tri1 = Triangles[i];
                for (int j = i + 1; j < Triangles.Length; j++)
                {
                    var tri2 = Triangles[j];

                    if (ShareEdge(tri1, tri2, out int siteA, out int siteB))
                    {
                        if (siteA != -1 && siteB != -1)
                        {
                            Edges.Add(new VoronoiEdge
                            {
                                SiteA = siteA,
                                SiteB = siteB,
                                VertexA = tri1.CircumCenter,
                                VertexB = tri2.CircumCenter,
                                CellA = Entity.Null,
                                CellB = Entity.Null,
                                Level = Level
                            });
                        }
                    }
                }
            }
        }

        private bool ShareEdge(DelaunayTriangle a, DelaunayTriangle b, out int siteA, out int siteB)
        {
            siteA = -1; siteB = -1; int sharedCount = 0;
            int a1 = a.A, a2 = a.B, a3 = a.C;
            int b1 = b.A, b2 = b.B, b3 = b.C;

            if (a1 == b1 || a1 == b2 || a1 == b3) { if (sharedCount++ == 0) siteA = a1; else siteB = a1; }
            if (a2 == b1 || a2 == b2 || a2 == b3) { if (sharedCount++ == 0) siteA = a2; else siteB = a2; }
            if (a3 == b1 || a3 == b2 || a3 == b3) { if (sharedCount++ == 0) siteA = a3; else siteB = a3; }

            return sharedCount >= 2;
        }
    }
}