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
        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // Создаем ячейки для каждого сайта
            for (int i = 0; i < Sites.Length; i++)
            {
                Cells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = Sites[i],
                    RegionIndex = i
                });
            }

            // Простой подход: создаем ребра между всеми парами треугольников с общими вершинами
            for (int i = 0; i < Triangles.Length; i++)
            {
                for (int j = i + 1; j < Triangles.Length; j++)
                {
                    var tri1 = Triangles[i];
                    var tri2 = Triangles[j];

                    // Проверяем, являются ли треугольники соседними (имеют 2 общие вершины)
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
                                CellB = Entity.Null
                            });
                        }
                    }
                }
            }
        }

        private bool ShareEdge(DelaunayTriangle a, DelaunayTriangle b, out int siteA, out int siteB)
        {
            siteA = -1;
            siteB = -1;
            int sharedCount = 0;

            // Проверяем все вершины первого треугольника
            int[] vertsA = { a.A, a.B, a.C };
            int[] vertsB = { b.A, b.B, b.C };

            // Считаем общие вершины
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (vertsA[i] == vertsB[j])
                    {
                        sharedCount++;
                        if (sharedCount == 1)
                            siteA = vertsA[i];
                        else if (sharedCount == 2)
                            siteB = vertsA[i];
                        break;
                    }
                }
            }

            return sharedCount >= 2;
        }
    }
}