using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Mesh
{
    // Компонент для маркировки сгенерированных мешей
    public struct VoronoiMeshTag : IComponentData
    {
        public int CellIndex;
    }

    // Компонент для хранения вершин ячейки
    public struct CellVertices : IBufferElementData
    {
        public float2 Position;
    }

    // Компонент для хранения границ ячейки
    public struct CellBoundary : IComponentData
    {
        public float Height;
        public float BorderWidth;
        public bool HasBorder;
    }

    // Компонент для цвета ячейки
    public struct CellColor : IComponentData
    {
        public float4 Color;
    }

    // Тег для обновления меша
    public struct MeshUpdateRequest : IComponentData
    {
        public bool UpdateVertices;
        public bool UpdateColors;
        public bool UpdateUVs;
    }
}