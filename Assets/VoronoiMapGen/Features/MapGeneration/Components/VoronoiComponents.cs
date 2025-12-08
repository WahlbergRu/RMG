using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.MapGeneration.Components
{
    // --- Voronoi & Delaunay Structures ---

    /// <summary>
    ///     Треугольник Делоне. Используется для построения графа.
    /// </summary>
    public struct DelaunayTriangle : IComponentData
    {
        public int A, B, C; // Индексы вершин (сайтов)
        public float2 CircumCenter; // Центр описанной окружности
        public float CircumRadius; // Радиус описанной окружности
    }

    /// <summary>
    ///     Точка (центр) ячейки Вороного.
    /// </summary>
    public struct VoronoiSite : IComponentData
    {
        public float2 Position;
        public int Index;
        public int Level;
        public int ParentIndex;
        public float Value; // Suitability / Desirability
    }

    /// <summary>
    ///     Сама ячейка (полигон).
    /// </summary>
    public struct VoronoiCell : IComponentData
    {
        public int SiteIndex;
        public float2 Centroid;
        public int RegionIndex;
        public int Level;
        public int ParentRegionIndex;
        public float Value;
        public Entity ParentEntity;
    }

    /// <summary>
    ///     Ребро между двумя ячейками.
    /// </summary>
    public struct VoronoiEdge : IComponentData
    {
        public int SiteA;
        public int SiteB;
        public float2 VertexA;
        public float2 VertexB;
        public Entity CellA;
        public Entity CellB;
        public int Level;
    }
}