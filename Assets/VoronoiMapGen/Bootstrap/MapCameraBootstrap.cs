using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapCameraBootstrap : MonoBehaviour
    {
        [Header("Camera Controls")]
        public float PanSpeed = 50f;
        public float ZoomSpeed = 100f;
        public float MinHeight = 20f;   // Насколько близко можно подлететь
        public float MaxHeight = 1500f; // Насколько далеко можно отлететь
        [Range(1f, 20f)]
        public float Smoothness = 5f;   // Чем больше, тем резче остановка

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;

            // Создаем сущность-синглтон для камеры
            var entity = em.CreateEntity();
            em.SetName(entity, "CameraController");

            em.AddComponentData(entity, new CameraSettingsData
            {
                PanSpeed = PanSpeed,
                ZoomSpeed = ZoomSpeed,
                MinHeight = MinHeight,
                MaxHeight = MaxHeight,
                Smoothing = Smoothness,
                TargetPosition = float3.zero, // Будет перезаписано в системе при старте
                IsInitialized = false
            });
        }
    }
}