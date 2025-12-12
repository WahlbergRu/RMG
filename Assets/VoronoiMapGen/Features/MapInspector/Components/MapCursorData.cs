// FILE: Assets\VoronoiMapGen\Features\MapInspector\Components\MapCursorData.cs

using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapInspector.Components
{
    public struct MapCursorData : IComponentData
    {
        public bool IsHovering;
        public int HoveredCellIndex;
        public float3 HoveredPosition;
        
        public bool IsDirty; 

        // Идентификация
        public int CellID;
        public int ParentID;     
        public int LevelIndex;  

        // География
        public BiomeType CachedBiome;
        public float CachedElevation; 
        public bool IsRiver;         
        public bool IsOcean;          

        // === ДОБАВЛЕНО ДЛЯ UI ===
        public float Temperature; // 0..1
        public float Moisture;    // 0..1
        // ========================

        // Цивилизация
        public SettlementType CachedSettlement;
        public float CachedScore;
        public int CachedPopulation;
        public float CachedFertility;
    }
}