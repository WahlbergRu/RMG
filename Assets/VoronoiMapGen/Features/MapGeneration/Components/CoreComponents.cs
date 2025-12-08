using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.MapGeneration.Components
{
    // --- Tags & Buffers ---

    // Теги состояний
    public struct MapGeneratedTag : IComponentData
    {
    }

    public struct MapGenerationInProgress : IComponentData
    {
    }

    public struct GeometryBuiltTag : IComponentData
    {
    }

    public struct CellDirtyFlag : IComponentData
    {
    }

    // Теги типов сущностей
    public struct VoronoiCellMeshTag : IComponentData
    {
    }

    public struct RoadEntityTag : IComponentData
    {
    }

    public struct BorderEntityTag : IComponentData
    {
    }

    public struct WaterEntityTag : IComponentData
    {
    }

    // Буферы для генерации меша ячейки
    public struct CellPolygonVertex : IBufferElementData
    {
        public float3 Value;
    }

    public struct CellTriIndex : IBufferElementData
    {
        public int Value;
    }
}