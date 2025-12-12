// ============================================================
// FILE: Assets\VoronoiMapGen\_Core\Components\ConfigComponents.cs
// ============================================================
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Components
{
    // --- ТЕКТОНИКА ---
    [Serializable]
    public struct TectonicConfig
    {
        public float IslandRadiusRatio;
        public float IslandFalloff;
        public float HeightOffset;
        public float MountainFreq;
        public float MountainSharpness;
        public float MountainHeight;
        public float ValleyFreq;
        public float ValleyWidthPower;
        public float CarveStrength;
        public float CarveThreshold;
    }

    // --- ГИДРОЛОГИЯ (Обновленная) ---
    [Serializable]
    public struct HydrologyConfig
    {
        public float RainIntensity;      // Множитель осадков (для расчета Flux)
        public float RiverFluxThreshold; // Порог превращения ручья в реку
        public float MoistureInfluence;  // Влияние влажности климата на реки
    }

    // --- КЛИМАТ (Новая) ---
    [Serializable]
    public struct ClimateConfig
    {
        public float BaseTemperature;       // Базовая температура (0.5 = умеренно)
        public float TemperatureLapseRate;  // Насколько холодает с высотой
        public float BaseMoisture;          // Фоновая влажность биомов
        public float MoistureNoiseFreq;     // Частота шума вариации влажности
    }

    // --- ЦИВИЛИЗАЦИЯ ---
    [Serializable]
    public struct CivilizationConfig
    {
        public float GlobalPopScalar; 
        public int MinPopOutpost;
        public int MinPopTown;
        public int MinPopMetropolis;
        public float MetroExclusionRadius;
        public float TownExclusionRadius;
        
        public float MinSuitability;     // 0.0 - 1.0 (Отсекает плохие земли)
        public float TownSpawnChance;    // 0.0 - 1.0 (Вероятность города-спутника)
        public float OutpostSpawnChance; // 0.0 - 1.0 (Вероятность деревни)
    }

    // --- ГЛАВНЫЙ СИНГЛТОН НАСТРОЕК ---
    public struct MapSettings : IComponentData
    {
        public int Seed;
        public float2 MapSize;
        public int LevelsCount;

        // Визуализация
        public bool ShowRivers;
        public bool ShowRiverGizmos;
        public bool ShowSettlements; 

        // Маски уровней
        public int RiverRenderMask;
        public int RiverDebugMask;
        public bool UseAutoLOD;
        public bool ShowDebugWireframe;
        public int DebugLevelMask;
        public int RenderLevelMask;

        public float TerrainHeightScale;
        public bool UseCache;

        // Вложенные конфиги
        public TectonicConfig Tectonics;
        public HydrologyConfig Hydrology;       
        public ClimateConfig Climate;       
        public CivilizationConfig Civilization;

        public bool IsGenerated;

        public FixedList128Bytes<float4> DebugLayerColors;
        public FixedList512Bytes<BiomeColorEntry> BiomeColors;
    }

    // --- Остальные структуры оставляем для совместимости ---
    [Serializable]
    public struct LevelSettings : IBufferElementData
    {
        public int MinSiteCount;
        public int MaxSiteCount;
        public int GlobalSiteCount => MaxSiteCount;
        public float ScaleFactor;
        public float LODThreshold; 
        public float RenderThreshold;
        public int GenerateRoads;
        public float ValueBias;
        public float ValueScale;
        public int RelaxationIterations;
        public float EmptyCellChance;
        [Range(0, 20)] public float VisualInset;
        [Range(0, 10)] public int VisualSmoothing;
    }

    public enum DetailLevel : byte
    {
        Global=0, Regional=1, Settlement=2, Urban=3, Infrastructure=4, Building=5, Detail=6
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

    public struct BiomeColorEntry : IComponentData
    {
        public BiomeType biomeType;
        public float4 color;
    }
}