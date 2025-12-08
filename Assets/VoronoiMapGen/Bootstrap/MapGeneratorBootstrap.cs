using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components; // Для новых компонентов рендера
using VoronoiMapGen.Features.Rendering.Rivers;
using VoronoiMapGen.Features.Rendering.Terrain;

namespace VoronoiMapGen.Bootstrap
{
    [Serializable]
    public class VisualLevelSettings
    {
        public string Name = "Level Settings";
        public TerrainStyle Style = TerrainStyle.Blocky;
        public float HeightScale = 50.0f;
        public float BottomDepth = 30.0f;
        [Range(0f, 2f)] public float TopSurfaceNoise = 0.2f;

        [Header("Stratified Settings")] [Range(1, 10)]
        public int RockLayers = 3;

        [Range(0f, 2f)] public float LayerInset = 0.3f;

        [Header("River Visuals")] 
        [Tooltip("Множитель ширины реки")] [Range(0.1f, 5f)]
        public float RiverWidthMultiplier = 1.0f;

        [Tooltip("Амплитуда изгибов (змейка)")] [Range(0f, 20f)]
        public float MeanderAmp = 2.0f;

        [Tooltip("Частота изгибов")] [Range(0.001f, 0.5f)]
        public float MeanderFreq = 0.02f;

        [Tooltip("Влияние шума")] [Range(0f, 5f)]
        public float NoiseInfluence = 1.0f;
    }

    public class MapGeneratorBootstrap : MonoBehaviour
    {
        [Header("General Settings")] public int Seed = 12345;

        public bool UseCache = true;
        public Vector2 MapSize = new(1000, 1000);
        public float TerrainHeightScale = 50.0f;

        // --- RIVERS SETTINGS ---
        public bool ShowRivers = true;
        public bool[] RiverRenderLevels = new bool[4];
        public bool ShowRiverGizmos;
        public bool[] RiverDebugLevels = new bool[4];

        [Header("Level Configurations")]
        public bool UseAutoLOD = true;

        public LevelSettings[] LevelConfigs = new LevelSettings[1];

        [Header("Visual Styles Per Level")] public VisualLevelSettings[] VisualConfigs = new VisualLevelSettings[1];

        public bool ShowWireframe;
        public bool[] DebugLevels = new bool[4];
        public Color[] DebugColors = new Color[4];

        public bool[] RenderLevels = new bool[4];

        [HideInInspector] public Color oceanColor = new(0.1f, 0.3f, 0.8f, 1);
        [HideInInspector] public Color coastColor = new(0.9f, 0.8f, 0.6f, 1);
        [HideInInspector] public Color iceColor = new(0.8f, 0.9f, 1.0f, 1);
        [HideInInspector] public Color desertColor = new(0.9f, 0.8f, 0.5f, 1);
        [HideInInspector] public Color grasslandColor = new(0.3f, 0.7f, 0.2f, 1);
        [HideInInspector] public Color forestColor = new(0.1f, 0.5f, 0.1f, 1);
        [HideInInspector] public Color mountainColor = new(0.5f, 0.4f, 0.3f, 1);
        [HideInInspector] public Color snowColor = new(0.95f, 0.95f, 0.95f, 1);

        private void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var settingsEntity = em.CreateEntity();

            var mapSettings = new MapSettings
            {
                Seed = Seed, MapSize = MapSize, LevelsCount = LevelConfigs.Length,
                TerrainHeightScale = TerrainHeightScale,
                UseCache = UseCache,
                UseAutoLOD = UseAutoLOD, 

                ShowDebugWireframe = ShowWireframe,
                ShowRivers = ShowRivers,
                ShowRiverGizmos = ShowRiverGizmos,

                DebugLevelMask = CalculateMask(DebugLevels),
                RenderLevelMask = CalculateMask(RenderLevels),
                RiverRenderMask = CalculateMask(RiverRenderLevels),
                RiverDebugMask = CalculateMask(RiverDebugLevels),

                DebugLayerColors = new FixedList128Bytes<float4>(),
                BiomeColors = new FixedList512Bytes<BiomeColorEntry>()
            };

            foreach (var c in DebugColors) mapSettings.DebugLayerColors.Add(new float4(c.r, c.g, c.b, 1f));

            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ocean, color = new float4(0.1f, 0.3f, 0.8f, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Coast, color = new float4(coastColor.r, coastColor.g, coastColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ice, color = new float4(iceColor.r, iceColor.g, iceColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Desert, color = new float4(desertColor.r, desertColor.g, desertColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Grassland, color = new float4(grasslandColor.r, grasslandColor.g, grasslandColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Forest, color = new float4(forestColor.r, forestColor.g, forestColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Mountain, color = new float4(mountainColor.r, mountainColor.g, mountainColor.b, 1) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Snow, color = new float4(snowColor.r, snowColor.g, snowColor.b, 1) });

            em.AddComponentData(settingsEntity, mapSettings);

            var logicBuf = em.AddBuffer<LevelSettings>(settingsEntity);
            for (var i = 0; i < LevelConfigs.Length; i++) logicBuf.Add(LevelConfigs[i]);

            var visBuf = em.AddBuffer<TerrainVisualData>(settingsEntity);
            for (var i = 0; i < VisualConfigs.Length; i++)
            {
                var s = VisualConfigs[i];
                visBuf.Add(new TerrainVisualData
                {
                    Style = s.Style, HeightScale = s.HeightScale, BottomDepth = s.BottomDepth,
                    TopNoiseAmplitude = s.TopSurfaceNoise, StrataCount = s.RockLayers, StrataInset = s.LayerInset,
                    StrataJitter = 0.1f,
                    RiverWidthScale = s.RiverWidthMultiplier,
                    RiverMeanderAmplitude = s.MeanderAmp,
                    RiverMeanderFrequency = s.MeanderFreq,
                    RiverNoiseInfluence = s.NoiseInfluence
                });
            }
        }

        private void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            UpdateSettingsToECS(world.EntityManager);
        }

        private void OnValidate()
        {
            var targetLen = LevelConfigs.Length;
            if (targetLen == 0) return;

            if (VisualConfigs.Length != targetLen) ResizeArray(ref VisualConfigs, targetLen, new VisualLevelSettings());
            if (DebugLevels.Length != targetLen) ResizeArray(ref DebugLevels, targetLen, true);
            if (RenderLevels.Length != targetLen) ResizeArray(ref RenderLevels, targetLen, true);
            if (RiverRenderLevels.Length != targetLen) ResizeArray(ref RiverRenderLevels, targetLen, true);
            if (RiverDebugLevels.Length != targetLen) ResizeArray(ref RiverDebugLevels, targetLen, true);
            if (DebugColors.Length != targetLen) ResizeArray(ref DebugColors, targetLen, Color.magenta);
        }

        public void ResetVisualization()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;

            em.CompleteAllTrackedJobs();
            UpdateSettingsToECS(em);

            // 1. Удаляем реки
            var chunkRiverQuery = em.CreateEntityQuery(typeof(RiverChunkTag));
            if (!chunkRiverQuery.IsEmpty) em.DestroyEntity(chunkRiverQuery);

            var oldRiverQuery = em.CreateEntityQuery(typeof(RiverSegmentOwner));
            if (!oldRiverQuery.IsEmpty) em.DestroyEntity(oldRiverQuery);

            // 2. Очищаем компоненты рендеринга с ячеек
            var cellQuery = em.CreateEntityQuery(typeof(VoronoiCell), typeof(VoronoiCellMeshTag));
            if (!cellQuery.IsEmpty)
            {
                // Стандартные компоненты
                em.RemoveComponent<VoronoiCellMeshTag>(cellQuery);
                em.RemoveComponent<MaterialMeshInfo>(cellQuery);
                em.RemoveComponent<RenderMeshUnmanaged>(cellQuery);
                em.RemoveComponent<RenderMeshArray>(cellQuery);
                em.RemoveComponent<RenderBounds>(cellQuery);
                em.RemoveComponent<WorldRenderBounds>(cellQuery);
                em.RemoveComponent<URPMaterialPropertyBaseColor>(cellQuery);

                // Новые процедурные компоненты
                em.RemoveComponent<ProceduralMeshRequest>(cellQuery);
                em.RemoveComponent<ProceduralVertex>(cellQuery);
                em.RemoveComponent<ProceduralIndex>(cellQuery);
                em.RemoveComponent<MeshDirtyTag>(cellQuery);
                
                // ProceduralMeshReference должен удаляться аккуратно системой Unified, 
                // но если мы сносим все, лучше пометить это, иначе могут остаться "висящие" меши.
                // В данной архитектуре, удаление Reference не освобождает Unity Mesh мгновенно без системы.
                // UnifiedProceduralRenderSystem имеет логику очистки для сущностей БЕЗ Request но С Reference.
                // Мы удалили Request выше, так что Reference удалится сам в OnUpdate системы.
            }

            var immediate = !Application.isPlaying;
            var meshSystem = world.GetExistingSystemManaged<VoronoiMeshCreateSystem>();
            if (meshSystem != null) meshSystem.CleanupResources(immediate);

            var riverSystem = world.GetExistingSystemManaged<RiverRenderingSystem>();
            if (riverSystem != null) riverSystem.CleanupResources(immediate);
        }

        public void UpdateSettingsToECS(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(MapSettings));
            if (!query.HasSingleton<MapSettings>()) return;

            var entity = query.GetSingletonEntity();
            var currentData = em.GetComponentData<MapSettings>(entity);

            currentData.UseAutoLOD = UseAutoLOD;

            if (!UseAutoLOD)
            {
                currentData.RenderLevelMask = CalculateMask(RenderLevels);
                currentData.RiverRenderMask = CalculateMask(RiverRenderLevels);
            }

            currentData.DebugLevelMask = CalculateMask(DebugLevels);
            currentData.RiverDebugMask = CalculateMask(RiverDebugLevels);
            currentData.ShowDebugWireframe = ShowWireframe;
            currentData.TerrainHeightScale = TerrainHeightScale;
            currentData.ShowRivers = ShowRivers;
            currentData.ShowRiverGizmos = ShowRiverGizmos;

            currentData.DebugLayerColors.Clear();
            foreach (var c in DebugColors)
                if (currentData.DebugLayerColors.Length < currentData.DebugLayerColors.Capacity)
                    currentData.DebugLayerColors.Add(new float4(c.r, c.g, c.b, 1f));
            em.SetComponentData(entity, currentData);

            if (em.HasBuffer<TerrainVisualData>(entity))
            {
                var buf = em.GetBuffer<TerrainVisualData>(entity);
                if (buf.Length != VisualConfigs.Length) buf.ResizeUninitialized(VisualConfigs.Length);

                for (var i = 0; i < VisualConfigs.Length; i++)
                {
                    var s = VisualConfigs[i];
                    buf[i] = new TerrainVisualData
                    {
                        Style = s.Style,
                        HeightScale = s.HeightScale,
                        BottomDepth = s.BottomDepth,
                        TopNoiseAmplitude = s.TopSurfaceNoise,
                        StrataCount = s.RockLayers,
                        StrataInset = s.LayerInset,
                        StrataJitter = 0.1f,
                        RiverWidthScale = s.RiverWidthMultiplier,
                        RiverMeanderAmplitude = s.MeanderAmp,
                        RiverMeanderFrequency = s.MeanderFreq,
                        RiverNoiseInfluence = s.NoiseInfluence
                    };
                }
            }
        }

        private void ResizeArray<T>(ref T[] array, int newSize, T defaultVal)
        {
            var newArray = new T[newSize];
            for (var i = 0; i < Mathf.Min(array.Length, newSize); i++) newArray[i] = array[i];
            if (newSize > array.Length)
                for (var i = array.Length; i < newSize; i++)
                    newArray[i] = defaultVal;
            array = newArray;
        }

        private int CalculateMask(bool[] levels)
        {
            var mask = 0;
            if (levels == null) return mask;
            for (var i = 0; i < levels.Length; i++)
                if (levels[i])
                    mask |= 1 << i;
            return mask;
        }
    }
}