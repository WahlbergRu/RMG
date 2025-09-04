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
        
        [Header("Rendering Settings")]
        public float EdgeWidth = 0.1f;
        public float RoadWidth = 0.8f;
        public Color RoadColor = Color.yellow;
        public Color BorderColor = Color.blue;
        public bool DrawRoads = true;
        public bool DrawBorders = true;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("World is null!");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            // Создаем основные настройки
            var settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, new MapSettings
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
                IsGenerated = false
            });
            
            // Добавляем настройки уровней (7 по умолчанию)
            var levelSettings = entityManager.AddBuffer<LevelSettings>(settingsEntity);
            ConfigureDefaultLevelSettings(levelSettings);
        }
        
        private void ConfigureDefaultLevelSettings(DynamicBuffer<LevelSettings> levelSettings)
        {
            // L0: Global
            levelSettings.Add(new LevelSettings {
                SiteCount = 10,
                ScaleFactor = 0.3f,
                LODThreshold = 1000f,
                RenderThreshold = 2000f,
                ValueBias = 0.0f,
                ValueScale = 0.1f
            });
            
            // L1: Regional
            levelSettings.Add(new LevelSettings {
                SiteCount = 50,
                ScaleFactor = 0.4f,
                LODThreshold = 500f,
                RenderThreshold = 1000f,
                ValueBias = 0.2f,
                ValueScale = 0.3f
            });
            
            // L2: Settlement
            levelSettings.Add(new LevelSettings {
                SiteCount = 100,
                ScaleFactor = 0.5f,
                LODThreshold = 200f,
                RenderThreshold = 400f,
                ValueBias = 0.5f,
                ValueScale = 0.4f
            });
            
            // L3: Urban
            levelSettings.Add(new LevelSettings {
                SiteCount = 300,
                ScaleFactor = 0.6f,
                LODThreshold = 100f,
                RenderThreshold = 200f,
                ValueBias = 0.7f,
                ValueScale = 0.5f
            });
            
            // L4: Infrastructure
            levelSettings.Add(new LevelSettings {
                SiteCount = 600,
                ScaleFactor = 0.7f,
                LODThreshold = 50f,
                RenderThreshold = 100f,
                ValueBias = 0.3f,
                ValueScale = 0.6f
            });
            
            // L5: Building
            levelSettings.Add(new LevelSettings {
                SiteCount = 1000,
                ScaleFactor = 0.8f,
                LODThreshold = 20f,
                RenderThreshold = 40f,
                ValueBias = 0.8f,
                ValueScale = 0.7f
            });
            
            // L6: Detail
            levelSettings.Add(new LevelSettings {
                SiteCount = 2000,
                ScaleFactor = 0.9f,
                LODThreshold = 5f,
                RenderThreshold = 10f,
                ValueBias = 0.1f,
                ValueScale = 0.8f
            });
        }
    }
}