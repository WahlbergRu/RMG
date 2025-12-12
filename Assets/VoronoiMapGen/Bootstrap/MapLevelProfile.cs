// ============================================================
// FILE: Assets\VoronoiMapGen\Bootstrap\MapLevelProfile.cs
// ============================================================
using System;
using UnityEngine;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Bootstrap
{
    [Serializable]
    public class MapLevelProfile
    {
        [Tooltip("Название уровня (L0 - Global, L3 - City...)")]
        public string ProfileName = "New Level";

        // --- ЛОГИКА (VORONOI) ---
        [Header("Generation Logic")]
        [Min(5)] public int MinSites = 50;
        [Min(5)] public int MaxSites = 100;
        
        [Tooltip("Размер ячеек относительно родителя (0.1 = в 10 раз меньше)")]
        [Range(0.01f, 1f)] public float ScaleFactor = 0.5f;

        [Range(0, 5)] public int RelaxationIterations = 2;
        [Range(0f, 1f)] public float EmptyCellChance = 0.0f;

        // --- LOD (КАМЕРА) ---
        [Header("LOD Switching")]
        [Tooltip("Высота камеры для включения этого уровня")]
        public float LODThreshold = 1000f; 
        
        [Tooltip("Высота, где начинается детальный рендер мешей")]
        public float RenderThreshold = 1200f;

        // --- ВИЗУАЛ ТЕРРЕЙНА ---
        [Header("Terrain Visuals")]
        public TerrainStyle Style = TerrainStyle.Blocky;
        public float HeightScale = 50.0f;
        public float BottomDepth = 30.0f;

        [Range(0f, 2f)] public float TopSurfaceNoise = 0.1f;
        [Range(0.01f, 1f)] public float TextureScale = 0.05f;

        [Header("Cliff Visuals")]
        [Range(1, 10)] public int RockLayers = 3;
        [Range(0f, 1f)] public float LayerInset = 0.2f;

        // --- ВИЗУАЛ РЕК ---
        [Header("River Visuals")]
        [Range(0.1f, 10f)] public float RiverWidthMultiplier = 1.0f;
        [Range(0f, 20f)] public float MeanderAmplitude = 2.0f;

        // --- ВИЗУАЛ ДОРОГ (НОВОЕ) ---
        [Header("Road Visuals")]
        public bool GenerateRoads = true;
        [Range(0.1f, 20f)] 
        public float MainRoadWidth = 2.0f; 
        [Range(0.1f, 10f)] 
        public float SecondaryRoadWidth = 1.0f;
        [Range(0f, 1f)] 
        public float RoadSmoothing = 0.5f; // Сглаживание углов
    }
}