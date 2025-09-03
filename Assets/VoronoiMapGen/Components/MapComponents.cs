using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Components
{
    public struct MapGenerationRequest : IComponentData
    {
        public int Seed;
        public int SiteCount;
        public float2 MapSize;
        public bool IsGenerated;
        public int LevelsCount; 
        public float LevelScaleFactor; 
    }

    public struct CellBiome : IComponentData
    {
        public BiomeType Type;
        public float Elevation;
        public float Moisture;
        public float Temperature;
    }

    // Сделаем enum обычным, а не byte для совместимости
    public enum BiomeType
    {
        Ocean,
        Coast,
        Ice,
        Desert,
        Grassland,
        Forest,
        Mountain,
        Snow
    }

    public struct CellRegion : IComponentData
    {
        public int RegionId;
        public bool IsWater;
        public bool IsCoast;
    }

    public struct CellNeighbors : IBufferElementData
    {
        public Entity NeighborCell;
        public float Distance;
    }

    public struct MapGeneratedTag : IComponentData { }
     
    
    /// <summary>
    /// Высота ячейки (по центроиду)
    /// </summary>
    public struct CellHeight : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Высота ребра (по середине сегмента)
    /// </summary>
    public struct EdgeHeight : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Высота вершины полигона (может использоваться для true 3D-ландшафта)
    /// </summary>
    public struct VertexHeight : IBufferElementData
    {
        public float Value;
    }
}