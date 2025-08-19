using Unity.Entities;
using Unity.Mathematics;

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
}