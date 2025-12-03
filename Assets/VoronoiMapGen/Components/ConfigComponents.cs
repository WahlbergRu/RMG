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
        
        public float EdgeWidth;
        public float RoadWidth;
        public Color RoadColor;
        public Color BorderColor;
        
        public bool DrawRoads;
        public bool DrawBorders;
        public bool ShowDebugWireframe; 
        public int DebugLevelMask;     

        public bool IsGenerated;
        public FixedList512Bytes<BiomeColorEntry> BiomeColors;
    }

    [System.Serializable]
    public struct LevelSettings : IBufferElementData
    {
        public int MinSiteCount; 
        public int MaxSiteCount; 
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
        public float LODThreshold;
        public float RenderThreshold;
    }
}