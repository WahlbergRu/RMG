using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine; // Для Color

namespace VoronoiMapGen.Components
{
    // --- Configuration ---

    public struct MapSettings : IComponentData
    {
        public int Seed;
        public float2 MapSize;
        public int LevelsCount; 
        
        // Переключатели
        public bool ShowRivers;      
        public bool ShowRiverGizmos; 
        
        // Маски (исправлено под раздельное управление)
        public int RiverRenderMask;  
        public int RiverDebugMask;   
        
        // Legacy Render
        public float EdgeWidth;
        public float RoadWidth;
        public Color RoadColor;
        public Color BorderColor;
        public bool DrawRoads;
        public bool DrawBorders;
        
        public bool ShowDebugWireframe; 
        public int DebugLevelMask;     
        public int RenderLevelMask;
        
        public float TerrainHeightScale; 
        public bool UseCache;

        public bool IsGenerated;
        public FixedList128Bytes<float4> DebugLayerColors; 
        public FixedList512Bytes<BiomeColorEntry> BiomeColors;
    }

    [System.Serializable]
    public struct LevelSettings : IBufferElementData
    {
        public int MinSiteCount; 
        public int MaxSiteCount; 
        
        // --- ИСПРАВЛЕНИЕ: Добавлено свойство ---
        public int GlobalSiteCount => MaxSiteCount; 
        
        public float ScaleFactor;     
        public float LODThreshold;
        public float RenderThreshold;
        public float ValueBias;
        public float ValueScale;
        public int RelaxationIterations;
        public float EmptyCellChance; 
        [Range(0, 20)] public float VisualInset;
        [Range(0, 10)] public int VisualSmoothing;  
    }

    public struct CameraSettingsData : IComponentData
    {
        public float PanSpeed;        
        public float ZoomSpeed;       
        public float MinHeight;       
        public float MaxHeight;       
        public float Smoothing;       
        public float3 TargetPosition; 
        public bool IsInitialized;    
    }

    public enum DetailLevel : byte
    {
        Global = 0, Regional = 1, Settlement = 2, 
        Urban = 3, Infrastructure = 4, Building = 5, Detail = 6
    }

    public struct DetailLevelData : IComponentData
    {
        public DetailLevel Level;
        public int ParentIndex;
        public int ChildCount;
        public float InfluenceRadius;
        
        // --- ИСПРАВЛЕНИЕ: Поля возвращены ---
        public float LODThreshold;
        public float RenderThreshold;
    }
    
    public struct BiomeColorEntry : IComponentData
    {
        public BiomeType biomeType;
        public float4 color;
    }
}