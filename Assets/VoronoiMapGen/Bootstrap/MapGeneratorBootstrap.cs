using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapGeneratorBootstrap : MonoBehaviour
    {
        [Header("General Settings")]
        public int Seed = 12345;
        public Vector2 MapSize = new Vector2(1000, 1000);
        
        [Header("Level Configurations")] 
        public LevelSettings[] LevelConfigs = new LevelSettings[1]; 
        
        [Header("Debug Visualization")]
        public bool ShowWireframe = false;
        // Заменили int на массив галочек
        public bool[] DebugLevels = new bool[8]; 

        [Header("Rendering Settings")]
        public float EdgeWidth = 10f; 
        public float RoadWidth = 10f;
        public Color RoadColor = Color.yellow;
        public Color BorderColor = Color.blue;
        public bool DrawRoads = true;
        public bool DrawBorders = true;

        [Header("Biome Colors")]
        public Color oceanColor = new Color(0.1f, 0.3f, 0.8f, 1);
        public Color coastColor = new Color(0.9f, 0.8f, 0.6f, 1);
        public Color iceColor = new Color(0.8f, 0.9f, 1.0f, 1);
        public Color desertColor = new Color(0.9f, 0.8f, 0.5f, 1);
        public Color grasslandColor = new Color(0.3f, 0.7f, 0.2f, 1);
        public Color forestColor = new Color(0.1f, 0.5f, 0.1f, 1);
        public Color mountainColor = new Color(0.5f, 0.4f, 0.3f, 1);
        public Color snowColor = new Color(0.95f, 0.95f, 0.95f, 1);

        private void OnValidate()
        {
            if (LevelConfigs == null || LevelConfigs.Length == 0)
            {
                LevelConfigs = new LevelSettings[1];
            }
            if (LevelConfigs[0].SiteCount == 0 && LevelConfigs[0].ScaleFactor == 0.0f)
            {
                ConfigureDefaultLevelConfigs();
            }
        }
        
        void Start()
        {
            // Инициализация массива отладки, если пустой
            if (DebugLevels == null || DebugLevels.Length == 0)
            {
                DebugLevels = new bool[8];
                for (int i = 0; i < 8; i++) DebugLevels[i] = true;
            }

            if (LevelConfigs == null || LevelConfigs.Length == 0)
            {
                 Debug.LogWarning("LevelConfigs was null. Initializing defaults.");
                 LevelConfigs = new LevelSettings[1];
                 ConfigureDefaultLevelConfigs();
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            
            EntityManager entityManager = world.EntityManager;
            
            Entity settingsEntity = entityManager.CreateEntity();
            
            // Создаем настройки с учетом маски
            var mapSettings = new MapSettings
            {
                Seed = Seed,
                MapSize = MapSize,
                LevelsCount = LevelConfigs.Length,
                EdgeWidth = EdgeWidth,
                RoadWidth = RoadWidth,
                RoadColor = RoadColor,
                BorderColor = BorderColor,
                DrawRoads = DrawRoads,
                DrawBorders = DrawBorders,
                IsGenerated = false,
                BiomeColors = new FixedList512Bytes<BiomeColorEntry>(),
                
                // === ВАЖНО: Новые поля ===
                ShowDebugWireframe = ShowWireframe,
                DebugLevelMask = CalculateMask() // Считаем маску
            };
            
            // Заполнение цветов
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ocean, color = new float4(oceanColor.r, oceanColor.g, oceanColor.b, oceanColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Coast, color = new float4(coastColor.r, coastColor.g, coastColor.b, coastColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ice, color = new float4(iceColor.r, iceColor.g, iceColor.b, iceColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Desert, color = new float4(desertColor.r, desertColor.g, desertColor.b, desertColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Grassland, color = new float4(grasslandColor.r, grasslandColor.g, grasslandColor.b, grasslandColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Forest, color = new float4(forestColor.r, forestColor.g, forestColor.b, forestColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Mountain, color = new float4(mountainColor.r, mountainColor.g, mountainColor.b, mountainColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Snow, color = new float4(snowColor.r, snowColor.g, snowColor.b, snowColor.a) });
            
            entityManager.AddComponentData(settingsEntity, mapSettings);
            
            DynamicBuffer<LevelSettings> levelSettingsBuffer = entityManager.AddBuffer<LevelSettings>(settingsEntity);
            for (int i = 0; i < LevelConfigs.Length; i++)
            {
                levelSettingsBuffer.Add(LevelConfigs[i]);
            }
        }

        // Обновление настроек в реальном времени
        void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;

            var query = em.CreateEntityQuery(typeof(MapSettings));
            if (query.HasSingleton<MapSettings>())
            {
                var entity = query.GetSingletonEntity();
                var settings = em.GetComponentData<MapSettings>(entity);
                
                int currentMask = CalculateMask();

                // Проверяем изменения (включая маску)
                if (settings.ShowDebugWireframe != ShowWireframe || settings.DebugLevelMask != currentMask)
                {
                    settings.ShowDebugWireframe = ShowWireframe;
                    settings.DebugLevelMask = currentMask;
                    em.SetComponentData(entity, settings);
                }
            }
        }
        
        // Превращает массив bool[] в int (битовая маска)
        private int CalculateMask()
        {
            int mask = 0;
            if (DebugLevels == null) return mask;
            
            for (int i = 0; i < DebugLevels.Length; i++)
            {
                if (DebugLevels[i])
                {
                    mask |= (1 << i);
                }
            }
            return mask;
        }
        
        private void ConfigureDefaultLevelConfigs()
        {
            LevelConfigs[0] = new LevelSettings {
                SiteCount = 50,
                ScaleFactor = 0.3f,
                LODThreshold = 1000f,
                RenderThreshold = 2000f,
                ValueBias = 0.0f,
                ValueScale = 0.1f,
                RelaxationIterations = 1
            };
        }
    }
}