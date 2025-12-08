using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Features.MapGeneration.Components;

// Для Color и Range

namespace VoronoiMapGen.Components
{
    // --- Global Tectonic Configuration ---
    [Serializable]
    public struct TectonicConfig
    {
        // Island Shape
        public float IslandRadiusRatio;
        public float IslandFalloff;
        public float HeightOffset;

        // Mountains
        public float MountainFreq;
        public float MountainSharpness;
        public float MountainHeight;

        // Erosion (Carving)
        public float ValleyFreq;
        public float ValleyWidthPower;
        public float CarveStrength;
        public float CarveThreshold;
    }

    // --- Main Map Settings Singleton ---
    public struct MapSettings : IComponentData
    {
        public int Seed;
        public float2 MapSize;
        public int LevelsCount;

        // --- Feature Toggles ---
        public bool ShowRivers;
        public bool ShowRiverGizmos;

        // --- Masks ---
        public int RiverRenderMask;
        public int RiverDebugMask;

        public bool UseAutoLOD;

        public bool ShowDebugWireframe;
        public int DebugLevelMask;
        public int RenderLevelMask;

        // --- Terrain Physics ---
        public float TerrainHeightScale;
        public bool UseCache;

        // --- Logic Configs ---
        public TectonicConfig Tectonics;

        // --- Legacy Visuals (Roads & Borders) ---
        public bool DrawBorders;
        public bool DrawRoads;
        public float RoadWidth;
        public float EdgeWidth;
        public Color BorderColor;
        public Color RoadColor;

        // --- Internal State ---
        public bool IsGenerated;

        // --- Shared Colors ---
        public FixedList128Bytes<float4> DebugLayerColors;
        public FixedList512Bytes<BiomeColorEntry> BiomeColors;
    }

    // --- Per-Level Logic Configuration ---
    [Serializable]
    public struct LevelSettings : IBufferElementData
    {
        public int MinSiteCount;
        public int MaxSiteCount;

        public int GlobalSiteCount => MaxSiteCount;

        public float ScaleFactor;
        public float LODThreshold; // Порог высоты камеры для переключения
        public float RenderThreshold;
        public float ValueBias;
        public float ValueScale;
        public int RelaxationIterations;
        public float EmptyCellChance;

        [Range(0, 20)] public float VisualInset;
        [Range(0, 10)] public int VisualSmoothing;
    }

    // --- Hierarchical Levels ---
    public enum DetailLevel : byte
    {
        Global = 0,
        Regional = 1,
        Settlement = 2,
        Urban = 3,
        Infrastructure = 4,
        Building = 5,
        Detail = 6
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

    // --- Helpers ---
    public struct BiomeColorEntry : IComponentData
    {
        public BiomeType biomeType;
        public float4 color;
    }
}