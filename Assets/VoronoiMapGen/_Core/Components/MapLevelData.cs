using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;
// ВАЖНО: Подключаем цивилизацию
using VoronoiMapGen.Features.Civilization.Components; 

namespace VoronoiMapGen.Features.Data
{
    public struct MapLevelData
    {
        public int LevelIndex;
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> Meta;
        public NativeArray<VoronoiCell> Cells;
        public NativeArray<VoronoiEdge> Edges;

        // Данные симуляции
        public NativeArray<TectonicPlateData> Tectonics;
        public NativeArray<ClimateData> Climate;
        public NativeArray<HydrologyData> Hydrology;
        public NativeArray<BiomeData> Biomes;
        public NativeArray<DistrictData> Districts; 

        
        // Данные поселений
        public NativeArray<SettlementData> Settlements;

        public bool IsCreated => Sites.IsCreated && Cells.IsCreated;
        public int Length => Sites.Length;

        public void Dispose()
        {
            if (Sites.IsCreated) Sites.Dispose();
            if (Meta.IsCreated) Meta.Dispose();
            if (Cells.IsCreated) Cells.Dispose();
            if (Edges.IsCreated) Edges.Dispose();

            if (Tectonics.IsCreated) Tectonics.Dispose();
            if (Climate.IsCreated) Climate.Dispose();
            if (Hydrology.IsCreated) Hydrology.Dispose();
            if (Biomes.IsCreated) Biomes.Dispose();
            if (Districts.IsCreated) Districts.Dispose();
            
            if (Settlements.IsCreated) Settlements.Dispose();
        }
    }
}