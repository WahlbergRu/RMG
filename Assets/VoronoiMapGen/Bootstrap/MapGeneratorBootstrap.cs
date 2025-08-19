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
                IsGenerated = false
            });
        }
    }
}