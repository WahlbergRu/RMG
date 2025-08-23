using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

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
    
    // Тег, чтобы не пересоздавать меш
    public struct VoronoiCellMeshTag : IComponentData
    {}

    // Тег, чтобы отслеживать, что мешы сгенерированы
    public struct VoronoiMeshGeneratedTag : IComponentData
    {
    }
    
    // Сохраняем 2D-полигон ячейки (в мировых XZ, Y = 0 на стадии рендера)
    [InternalBufferCapacity(6)]
    public struct CellPolygonVertex : IBufferElementData
    {
        public float2 Value;
    }
    
    public struct CellDirtyFlag : IComponentData {} // наличие = меш устарел
        
    [MaterialProperty("_BaseColor")]
    public struct CellColor : IComponentData { public float4 Value; }

}