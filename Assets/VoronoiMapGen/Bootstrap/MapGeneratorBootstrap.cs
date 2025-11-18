using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UI;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapGeneratorBootstrap : MonoBehaviour
    {
        public int Seed = 12345;
        public Vector2 MapSize = new Vector2(1000, 1000);
        
        [Header("Multi-Level Settings")]
        [MinMax(1,7)]
        public int LevelsCount = 7;
        
        [Header("Level Configurations")]
        public LevelSettings[] LevelConfigs = new LevelSettings[7]; // Инициализация массива
        
        [Header("Rendering Settings")]
        public float EdgeWidth = 0.1f;
        public float RoadWidth = 0.8f;
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
        
        // Метод OnValidate вызывается в редакторе при изменении значений в Inspector
        // или при загрузке скрипта. Используем его для установки значений по умолчанию.
        private void OnValidate()
        {
            // Убедимся, что массив инициализирован
            if (LevelConfigs == null || LevelConfigs.Length == 0)
            {
                LevelConfigs = new LevelSettings[7];
            }

            // Заполняем значения по умолчанию только в редакторе, если массив пуст (все поля == 0)
            // Проверяем первый элемент как индикатор "пустоты"
            if (LevelConfigs.Length > 0 && LevelConfigs[0].SiteCount == 0 && LevelConfigs[0].ScaleFactor == 0.0f)
            {
                ConfigureDefaultLevelConfigs();
            }
        }
        
        void Start()
        {
            // Убедимся, что LevelConfigs инициализирован, на всякий случай
            if (LevelConfigs == null || LevelConfigs.Length == 0)
            {
                 Debug.LogWarning("LevelConfigs was null or empty at runtime. Initializing with defaults.");
                 LevelConfigs = new LevelSettings[7];
                 ConfigureDefaultLevelConfigs();
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("World is null!");
                return;
            }
            
            EntityManager entityManager = world.EntityManager;
            
            // Создаем основные настройки
            Entity settingsEntity = entityManager.CreateEntity();
            var mapSettings = new MapSettings
            {
                Seed = Seed,
                MapSize = MapSize,
                LevelsCount = LevelsCount,
                EdgeWidth = EdgeWidth,
                RoadWidth = RoadWidth,
                RoadColor = RoadColor,
                BorderColor = BorderColor,
                DrawRoads = DrawRoads,
                DrawBorders = DrawBorders,
                IsGenerated = false,
                BiomeColors = new FixedList512Bytes<BiomeColorEntry>()
            };
            
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ocean, color = new float4(oceanColor.r, oceanColor.g, oceanColor.b, oceanColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Coast, color = new float4(coastColor.r, coastColor.g, coastColor.b, coastColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Ice, color = new float4(iceColor.r, iceColor.g, iceColor.b, iceColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Desert, color = new float4(desertColor.r, desertColor.g, desertColor.b, desertColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Grassland, color = new float4(grasslandColor.r, grasslandColor.g, grasslandColor.b, grasslandColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Forest, color = new float4(forestColor.r, forestColor.g, forestColor.b, forestColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Mountain, color = new float4(mountainColor.r, mountainColor.g, mountainColor.b, mountainColor.a) });
            mapSettings.BiomeColors.Add(new BiomeColorEntry { biomeType = BiomeType.Snow, color = new float4(snowColor.r, snowColor.g, snowColor.b, snowColor.a) });
            
            entityManager.AddComponentData(settingsEntity, mapSettings);
            
            // Добавляем настройки уровней (из сериализованного массива)
            DynamicBuffer<LevelSettings> levelSettingsBuffer = entityManager.AddBuffer<LevelSettings>(settingsEntity);
            for (int i = 0; i < Mathf.Min(LevelConfigs.Length, LevelsCount); i++)
            {
                levelSettingsBuffer.Add(LevelConfigs[i]);
            }
        }
        
        private void ConfigureDefaultLevelConfigs()
        {
            // L0: Global
            if (LevelConfigs.Length > 0) LevelConfigs[0] = new LevelSettings {
                SiteCount = 10,
                ScaleFactor = 0.3f,
                LODThreshold = 1000f,
                RenderThreshold = 2000f,
                ValueBias = 0.0f,
                ValueScale = 0.1f
            };

            // L1: Regional
            if (LevelConfigs.Length > 1) LevelConfigs[1] = new LevelSettings {
                SiteCount = 50,
                ScaleFactor = 0.4f,
                LODThreshold = 500f,
                RenderThreshold = 1000f,
                ValueBias = 0.2f,
                ValueScale = 0.3f
            };

            // L2: Settlement
            if (LevelConfigs.Length > 2) LevelConfigs[2] = new LevelSettings {
                SiteCount = 100,
                ScaleFactor = 0.5f,
                LODThreshold = 200f,
                RenderThreshold = 400f,
                ValueBias = 0.5f,
                ValueScale = 0.4f
            };

            // L3: Urban
            if (LevelConfigs.Length > 3) LevelConfigs[3] = new LevelSettings {
                SiteCount = 300,
                ScaleFactor = 0.6f,
                LODThreshold = 100f,
                RenderThreshold = 200f,
                ValueBias = 0.7f,
                ValueScale = 0.5f
            };

            // L4: Infrastructure
            if (LevelConfigs.Length > 4) LevelConfigs[4] = new LevelSettings {
                SiteCount = 600,
                ScaleFactor = 0.7f,
                LODThreshold = 50f,
                RenderThreshold = 100f,
                ValueBias = 0.3f,
                ValueScale = 0.6f
            };

            // L5: Building
            if (LevelConfigs.Length > 5) LevelConfigs[5] = new LevelSettings {
                SiteCount = 1000,
                ScaleFactor = 0.8f,
                LODThreshold = 20f,
                RenderThreshold = 40f,
                ValueBias = 0.8f,
                ValueScale = 0.7f
            };

            // L6: Detail
            if (LevelConfigs.Length > 6) LevelConfigs[6] = new LevelSettings {
                SiteCount = 2000,
                ScaleFactor = 0.9f,
                LODThreshold = 5f,
                RenderThreshold = 10f,
                ValueBias = 0.1f,
                ValueScale = 0.8f
            };
        }
    }
}