using Unity.Entities;
using Unity.Mathematics;
using UnityEngine; // Для Color

namespace VoronoiMapGen.Components
{
    // --- Simulation Data ---

    // Геология (L0)
    public struct TectonicPlateData : IComponentData
    {
        public bool IsOcean;        
        public float2 Velocity;     
        public float BaseHeight;    
        public float CrustAge;      
    }

    // Климат (L1)
    public struct ClimateData : IComponentData
    {
        public float Temperature;   
        public float Moisture;      
        public float WindDirection; 
    }

    // Биом (Результат симуляции)
    public struct BiomeData : IComponentData
    {
        public BiomeType Type;
    }

    // Гидрология (L1-L2)
    public struct HydrologyData : IComponentData
    {
        public int FlowTargetIndex; // Индекс соседа, куда течет вода
        public float Flux;          // Объем воды
        public float WaterLevel;    
        public bool IsRiver;        
        public bool IsLake;         
        public bool IsOcean;        
    }

    // --- Terrain & Height Data (Добавлено) ---

    // Финальная высота (используется для дорог и построения меша)
    public struct FinalHeightData : IComponentData
    {
        public float FinalElevation;     // Финальная высота
        public bool IsUrban;             // Это городская зона?
        public float HeightVariation;    // Колебания высоты
    }

    // Параметры рельефа
    public struct TerrainData : IComponentData
    {
        public float Elevation;      // Высота
        public float Slope;          // Уклон
        public float Roughness;      // Неровность
        public float ElevationVariation; // Локальные перепады
    }

    // Параметры релаксации (могут пригодиться для спецэффектов)
    public struct RelaxationData : IComponentData
    {
        public float EdgeInfluence;      
        public float CenterRelaxation;   
        public float DistanceToEdge;     
    }

    // --- Buffers & Helpers ---

    // Буфер соседей (для графов)
    [InternalBufferCapacity(8)]
    public struct CellNeighbor : IBufferElementData
    {
        public int NeighborIndex;  
        public Entity NeighborEntity; 
    }

    // Компонент для хранения данных биома на сущности (удобен для рендеринга)
    public struct CellBiome : IComponentData
    {
        public BiomeType Type;
        public float Elevation;
        public float Moisture;
        public float Temperature;
    }

    public enum BiomeType
    {
        Ocean, Coast, Ice, Desert, Grassland, Forest, Mountain, Snow
    }
    
    // Для настроек цветов в инспекторе
    public struct BiomeColorEntry : IComponentData
    {
        public BiomeType biomeType;
        public float4 color;
    }
}