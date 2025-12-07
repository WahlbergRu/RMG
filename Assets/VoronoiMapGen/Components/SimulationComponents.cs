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
    public enum RiverMorphology : byte
    {
        MountainStream, // V-образная, прямая, узкая
        Meandering,     // Извилистая, широкая долина
        Braided,        // Разветвленная (пока можно упростить до широкой)
        Delta           // Устье
    }

    public struct HydrologyData : IComponentData
    {
        public int FlowTargetIndex;
        public float Flux;          
        public float WaterLevel;
        public bool IsRiver;
        public bool IsLake;
        public bool IsOcean;

        // --- НОВЫЕ ПОЛЯ ДЛЯ АНАЛИТИКИ ---
        public float LocalSlope;        // Уклон на этом участке (разница высот / длину)
        public float StreamPower;       // Энергия потока = Flux * Slope
        public RiverMorphology Type;    // Тип русла (Мезоформа)
        public float BedResistance;     // Сопротивление дна (из Геологии L0)
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
    
    public struct NeighborInfo : IBufferElementData // Можно использовать и в буферах
    {
        public int Index;      // Кто сосед
        public float Distance; // Как далеко (в метрах)
    }
}