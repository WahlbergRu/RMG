using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace VoronoiMapGen.Components
{
    public struct VoronoiSite : IComponentData
    {
        public float2 Position;
        public int Index;
    }

    public struct VoronoiCell : IComponentData
    {
        public int SiteIndex;
        public float2 Centroid;
        public int RegionIndex;
    }

    public struct VoronoiEdge : IComponentData
    {
        public int SiteA;
        public int SiteB;
        public float2 VertexA;
        public float2 VertexB;
        public Entity CellA;
        public Entity CellB;
    }

    public struct DelaunayTriangle : IComponentData
    {
        public int A;
        public int B;
        public int C;
        public float2 CircumCenter;
        public float CircumRadius;
    }

    public struct TriangleNeighbor : IBufferElementData
    {
        public Entity NeighborTriangle;
        public int SharedEdge;
    }

    // Тег, чтобы отслеживать, что мешы сгенерированы
    public struct VoronoiMeshGeneratedTag : IComponentData
    {
    }
    
    public struct RoadEntityTag : IComponentData { }
    public struct BorderEntityTag : IComponentData { }
    
    public struct MapRenderingSettings : IComponentData
    {
        public float EdgeWidth;
        public float RoadWidth;
        public Color RoadColor;
        public Color BorderColor;
        public bool DrawRoads;
        public bool DrawBorders;
        public Vector2 MapSize;
    }
}