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

            // Создаем ребра между соседними треугольниками
            for (int i = 0; i < Triangles.Length; i++)
            {
                for (int j = i + 1; j < Triangles.Length; j++)
                {
                    var tri1 = Triangles[i];
                    var tri2 = Triangles[j];

                    // Проверяем, являются ли треугольники соседними
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

            // Сравниваем все вершины напрямую (без managed arrays)
            // Вершины треугольника a
            int vertA1 = a.A;
            int vertA2 = a.B;
            int vertA3 = a.C;
            
            // Вершины треугольника b
            int vertB1 = b.A;
            int vertB2 = b.B;
            int vertB3 = b.C;

            // Проверяем все комбинации
            if (vertA1 == vertB1 || vertA1 == vertB2 || vertA1 == vertB3)
            {
                if (sharedCount == 0)
                    siteA = vertA1;
                else if (sharedCount == 1)
                    siteB = vertA1;
                sharedCount++;
            }

            if (vertA2 == vertB1 || vertA2 == vertB2 || vertA2 == vertB3)
            {
                if (sharedCount == 0)
                    siteA = vertA2;
                else if (sharedCount == 1)
                    siteB = vertA2;
                sharedCount++;
            }

            if (vertA3 == vertB1 || vertA3 == vertB2 || vertA3 == vertB3)
            {
                if (sharedCount == 0)
                    siteA = vertA3;
                else if (sharedCount == 1)
                    siteB = vertA3;
                sharedCount++;
            }

            return sharedCount >= 2;
        }
    }
}