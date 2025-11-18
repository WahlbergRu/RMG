// Utils/CellUtils.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Utils
{
    public static class CellUtils
    {
        /// <summary>
        /// Вычисляет вершины полигона ячейки Вороной из рёбер
        /// </summary>
        public static void CalculateCellVertices(
            int cellSiteIndex, 
            NativeArray<VoronoiEdge> edges, 
            NativeList<float2> vertices,
            float2 mapSize)
        {
            vertices.Clear();
            
            // Собираем все рёбра, принадлежащие ячейке
            NativeList<VoronoiEdge> cellEdges = new NativeList<VoronoiEdge>(Allocator.Temp);
            for (int i = 0; i < edges.Length; i++)
            {
                if (edges[i].SiteA == cellSiteIndex || edges[i].SiteB == cellSiteIndex)
                {
                    cellEdges.Add(edges[i]);
                }
            }

            if (cellEdges.Length == 0)
            {
                cellEdges.Dispose();
                return;
            }

            // Строим полигон, соединяя вершины рёбер
            NativeHashSet<float2> addedVertices = new NativeHashSet<float2>(cellEdges.Length * 2, Allocator.Temp);
            
            for (int i = 0; i < cellEdges.Length; i++)
            {
                VoronoiEdge edge = cellEdges[i];
                
                // Добавляем вершины, если они внутри карты
                if (IsPointInMap(edge.VertexA, mapSize) && !addedVertices.Contains(edge.VertexA))
                {
                    vertices.Add(edge.VertexA);
                    addedVertices.Add(edge.VertexA);
                }
                
                if (IsPointInMap(edge.VertexB, mapSize) && !addedVertices.Contains(edge.VertexB))
                {
                    vertices.Add(edge.VertexB);
                    addedVertices.Add(edge.VertexB);
                }
            }

            // Сортируем вершины против часовой стрелки вокруг центроида
            if (vertices.Length > 2)
            {
                float2 centroid = CalculateCentroid(vertices.AsArray());
                vertices.Sort(new VertexDistanceComparer(centroid));
            }

            addedVertices.Dispose();
            cellEdges.Dispose();
        }

        private static bool IsPointInMap(float2 point, float2 mapSize)
        {
            return point.x >= -10f && point.x <= mapSize.x + 10f && 
                   point.y >= -10f && point.y <= mapSize.y + 10f;
        }

        private static float2 CalculateCentroid(NativeArray<float2> vertices)
        {
            float2 sum = float2.zero;
            for (int i = 0; i < vertices.Length; i++)
            {
                sum += vertices[i];
            }
            return sum / vertices.Length;
        }

        /// <summary>
        /// Сравниватель для сортировки вершин против часовой стрелки
        /// </summary>
        private struct VertexDistanceComparer : IComparer<float2>
        {
            private readonly float2 _centroid;

            public VertexDistanceComparer(float2 centroid)
            {
                _centroid = centroid;
            }

            public int Compare(float2 a, float2 b)
            {
                float angleA = math.atan2(a.y - _centroid.y, a.x - _centroid.x);
                float angleB = math.atan2(b.y - _centroid.y, b.x - _centroid.x);
                return angleA.CompareTo(angleB);
            }
        }
    }
}