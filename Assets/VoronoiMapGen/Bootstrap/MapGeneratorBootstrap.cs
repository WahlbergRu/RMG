// ============================================================
// FILE: Assets\VoronoiMapGen\Bootstrap\MapGeneratorBootstrap.cs
// ============================================================
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;
using VoronoiMapGen.Features.Rendering.Rivers;
using VoronoiMapGen.Features.Rendering.Terrain;

namespace VoronoiMapGen.Bootstrap
{
    public class MapGeneratorBootstrap : MonoBehaviour
    {
        [Header("World Core")] 
        public int Seed = 12345;
        public Vector2 MapSize = new(1000, 1000);
        public float TerrainHeightScale = 50.0f;
        public bool UseCache = true;

        [Header("Climate & Hydrology")]
        [Range(0f, 1f)] public float BaseTemperature = 0.5f;        
        [Range(0f, 2f)] public float TempHeightImpact = 0.9f;       
        [Range(0f, 1f)] public float BaseMoisture = 0.5f;
        [Space]
        [Range(0.1f, 5.0f)] public float RainIntensity = 1.0f;      
        [Range(1.0f, 50f)] public float RiverFluxThreshold = 8.0f; 

        [Header("Civilization Balance")]
        public bool ShowSettlements = true;
        public float GlobalPopScalar = 2000f; 
        
        [Space]
        [Range(0.0f, 1.0f)] public float MinSiteQuality = 0.6f;
        [Range(0.0f, 1.0f)] public float TownSpawnChance = 0.2f;
        [Range(0.0f, 1.0f)] public float OutpostSpawnChance = 0.15f; 

        [Space]
        public int MinPopOutpost = 3000;
        public int MinPopTown = 10000;
        public int MinPopMetropolis = 25000;

        [Header("Levels Configuration")] 
        public bool UseAutoLOD = true;
        [SerializeField] private MapLevelProfile[] Levels; 

        [Header("Render Mask")]
        public bool ShowRivers = true;
        [HideInInspector] public bool[] RiverRenderLevels = new bool[4];
        [HideInInspector] public bool[] RenderLevels = new bool[4];

        [Header("Debug")]
        public bool ShowWireframe;
        public bool ShowRiverGizmos;
        public bool[] DebugLevels = new bool[4];
        public bool[] RiverDebugLevels = new bool[4];
        public Color[] DebugColors = new Color[4];

        private void OnValidate()
        {
            if (Levels == null || Levels.Length == 0)
                Levels = MapPresets.GetDefault5Levels();

            int count = Levels.Length;
            ResizeArray(ref RiverRenderLevels, count, true);
            ResizeArray(ref RenderLevels, count, true);
            ResizeArray(ref DebugLevels, count, false);
            ResizeArray(ref RiverDebugLevels, count, true);
            ResizeArray(ref DebugColors, count, Color.white);
        }

        private void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;

            // -----------------------------------------------------------
            // 1. СОЗДАНИЕ СИНГЛТОНА НАСТРОЕК + СТАТУСА ГЕНЕРАЦИИ
            // -----------------------------------------------------------
            var settingsArchetype = em.CreateArchetype(
                typeof(MapSettings),
                typeof(LevelSettings),      
                typeof(TerrainVisualData),
                // !!! САМОЕ ВАЖНОЕ: Компонент, который слушает UI !!!
                typeof(GenerationStatus) 
            );

            var settingsEntity = em.CreateEntity(settingsArchetype);
            
            // Защита от пустого массива
            if (Levels == null || Levels.Length == 0) 
                Levels = MapPresets.GetDefault5Levels();

            // Заполняем настройки карты
            var mapSettings = CreateMapSettingsStruct();
            
            foreach (var c in DebugColors) mapSettings.DebugLayerColors.Add(new float4(c.r, c.g, c.b, 1f));
            AddBiomeColors(ref mapSettings);
            
            em.SetComponentData(settingsEntity, mapSettings);

            // -----------------------------------------------------------
            // 2. ИНИЦИАЛИЗАЦИЯ СТАТУСА (УБИРАЕМ "WAIT...")
            // -----------------------------------------------------------
            // Как только это выполнится, UI поменяет текст на "Booting up..."
            em.SetComponentData(settingsEntity, new GenerationStatus
            {
                TotalLevels = Levels.Length,
                ProcessedLevels = 0,
                TotalProgress = 0f,
                CurrentStepName = "Booting up...", 
                IsCompleted = false
            });

            // 3. Заполняем буферы настроек уровней
            var logicBuf = em.GetBuffer<LevelSettings>(settingsEntity);
            var visBuf = em.GetBuffer<TerrainVisualData>(settingsEntity);

            foreach (var profile in Levels)
            {
                logicBuf.Add(ConvertToLogic(profile));
                visBuf.Add(ConvertToVisual(profile));
            }
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) 
            {
                 var world = World.DefaultGameObjectInjectionWorld;
                 if (world != null) UpdateSettingsToECS(world.EntityManager);
            }
#endif
        }

        public void UpdateSettingsToECS(EntityManager em)
        {
            var query = em.CreateEntityQuery(typeof(MapSettings));
            if (!query.HasSingleton<MapSettings>()) return;
            var entity = query.GetSingletonEntity();
            
            var currentData = CreateMapSettingsStruct();
            
            currentData.DebugLayerColors.Clear();
            foreach (var c in DebugColors) currentData.DebugLayerColors.Add(new float4(c.r, c.g, c.b, 1f));
            AddBiomeColors(ref currentData);
            
            em.SetComponentData(entity, currentData);
            
            if (em.HasBuffer<TerrainVisualData>(entity) && Levels != null)
            {
                var visBuf = em.GetBuffer<TerrainVisualData>(entity);
                if (visBuf.Length != Levels.Length) visBuf.ResizeUninitialized(Levels.Length);
                for(int i=0; i < Levels.Length; i++) visBuf[i] = ConvertToVisual(Levels[i]);
            }
        }
        
        // --- HELPERS ---

        private MapSettings CreateMapSettingsStruct()
        {
            return new MapSettings
            {
                Seed = Seed, MapSize = MapSize, 
                LevelsCount = Levels != null ? Levels.Length : 0,
                TerrainHeightScale = TerrainHeightScale,
                UseCache = UseCache,
                UseAutoLOD = UseAutoLOD,

                // Toggles
                ShowDebugWireframe = ShowWireframe,
                ShowRivers = ShowRivers,
                ShowRiverGizmos = ShowRiverGizmos,
                ShowSettlements = ShowSettlements,

                // Configs
                Climate = new ClimateConfig {
                    BaseTemperature = BaseTemperature,
                    TemperatureLapseRate = TempHeightImpact,
                    BaseMoisture = BaseMoisture,
                    MoistureNoiseFreq = 0.01f
                },
                Hydrology = new HydrologyConfig { 
                    RainIntensity = RainIntensity,
                    RiverFluxThreshold = RiverFluxThreshold, 
                    MoistureInfluence = 1.0f
                },
                Civilization = new CivilizationConfig {
                    GlobalPopScalar = GlobalPopScalar,
                    MinPopOutpost = MinPopOutpost,
                    MinPopTown = MinPopTown,
                    MinPopMetropolis = MinPopMetropolis,
                    MetroExclusionRadius = 150f, TownExclusionRadius = 80f,
                    MinSuitability = MinSiteQuality,
                    TownSpawnChance = TownSpawnChance,
                    OutpostSpawnChance = OutpostSpawnChance
                },

                // Masks
                DebugLevelMask = CalculateMask(DebugLevels),
                RenderLevelMask = UseAutoLOD ? 0 : CalculateMask(RenderLevels), 
                RiverRenderMask = UseAutoLOD ? 0 : CalculateMask(RiverRenderLevels),
                RiverDebugMask = CalculateMask(RiverDebugLevels), 
                
                DebugLayerColors = new FixedList128Bytes<float4>(),
                BiomeColors = new FixedList512Bytes<BiomeColorEntry>()
            };
        }

        private LevelSettings ConvertToLogic(MapLevelProfile p)
        {
            return new LevelSettings
            {
                MinSiteCount = p.MinSites,
                MaxSiteCount = p.MaxSites,
                ScaleFactor = p.ScaleFactor,
                RelaxationIterations = p.RelaxationIterations,
                EmptyCellChance = p.EmptyCellChance,
                LODThreshold = p.LODThreshold,
                RenderThreshold = p.RenderThreshold,
                GenerateRoads = p.GenerateRoads ? 1 : 0, 
                ValueBias = 0, ValueScale = 1, VisualInset = 0.3f, VisualSmoothing = 1
            };
        }

        private TerrainVisualData ConvertToVisual(MapLevelProfile p)
        {
            return new TerrainVisualData
            {
                Style = p.Style, HeightScale = p.HeightScale, BottomDepth = p.BottomDepth,
                TopNoiseAmplitude = p.TopSurfaceNoise, TextureTiling = p.TextureScale,
                StrataCount = p.RockLayers, StrataInset = p.LayerInset, StrataJitter = 0.1f,
                RiverWidthScale = p.RiverWidthMultiplier, RiverMeanderAmplitude = p.MeanderAmplitude,
                RiverMeanderFrequency = 0.02f, RiverNoiseInfluence = 1.0f
            };
        }

        public void ResetVisualization()
        {
            var world = World.DefaultGameObjectInjectionWorld; 
            if (world != null) {
                world.GetExistingSystemManaged<VoronoiMeshCreateSystem>()?.CleanupResources(true);
                world.GetExistingSystemManaged<RiverRenderingSystem>()?.CleanupResources(true);
            }
        }
        
        private void ResizeArray<T>(ref T[] array, int newSize, T defaultVal) {
             if (array == null) array = new T[0];
             var newArray = new T[newSize];
             for(int i=0;i<Mathf.Min(array.Length,newSize);i++) newArray[i]=array[i];
             if(newSize>array.Length) for(int i=array.Length;i<newSize;i++) newArray[i]=defaultVal;
             array = newArray;
        }
        private int CalculateMask(bool[] levels) {
             var m=0; if(levels==null)return m; for(int i=0;i<levels.Length;i++) if(levels[i]) m|=1<<i; return m;
        }
        private void AddBiomeColors(ref MapSettings s) {
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ocean, color = new float4(0.1f, 0.3f, 0.8f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Coast, color = new float4(0.9f, 0.8f, 0.6f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ice, color = new float4(0.8f, 0.9f, 1.0f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Desert, color = new float4(0.9f, 0.8f, 0.5f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Grassland, color = new float4(0.3f, 0.7f, 0.2f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Forest, color = new float4(0.1f, 0.5f, 0.1f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Mountain, color = new float4(0.5f, 0.4f, 0.3f, 1) });
             s.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Snow, color = new float4(0.95f, 0.95f, 0.95f, 1) });
        }
    }
}