using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapGeneratorBootstrap : MonoBehaviour
    {
        public int Seed = 12345;
        public int SiteCount = 100;
        public Vector2 MapSize = new Vector2(100, 100);

        [Header("Rendering Settings")]
        public float EdgeWidth = 0.1f;
        public float RoadWidth = 0.8f;
        public Color RoadColor = Color.yellow;
        public Color BorderColor = Color.blue;
        public bool DrawRoads = true;
        public bool DrawBorders = true;
        public int LevelsCount; // Обычно 3-5
        public float LevelScaleFactor; // Как уменьшать масштаб (напр. 0.3)

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("World is null!");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            var requestEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(requestEntity, new MapGenerationRequest
            {
                Seed = Seed,
                SiteCount = SiteCount,
                MapSize = MapSize,
                IsGenerated = false,
                LevelsCount = LevelsCount,
                LevelScaleFactor = LevelScaleFactor
            });

            var settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, new MapRenderingSettings
            {
                EdgeWidth = EdgeWidth,
                RoadWidth = RoadWidth,
                RoadColor = RoadColor,
                BorderColor = BorderColor,
                DrawRoads = DrawRoads,
                DrawBorders = DrawBorders,
                MapSize = MapSize
            });
        }
    }
}