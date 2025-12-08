using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Features.Camera.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapCameraBootstrap : MonoBehaviour
    {
        [Header("References")] public Camera TargetCamera;

        [Header("Mode")] public CameraMode Mode = CameraMode.Free;

        [Header("Controls")] public float PanSpeed = 150f; // Увеличил скорость

        public float ZoomSpeed = 300f;
        public float RotateSpeed = 150f;

        [Header("Invert")] public bool InvertZoom = true; // Обычно зум инвертирован (колесо на себя = дальше)

        public bool InvertPan;

        [Header("Limits")] public float MinZoom = 20f;

        public float MaxZoom = 2000f;

        [Range(1f, 20f)] public float Smoothness = 8f;

        private Entity _cameraEntity;
        private EntityManager _entityManager;
        private bool _isInitialized;

        private void Start()
        {
            if (TargetCamera == null) TargetCamera = Camera.main;

            // --- ФИКС "ИСЧЕЗНОВЕНИЯ" ---
            // Увеличиваем дальность прорисовки, так как в Ortho режиме мы отодвигаем камеру далеко
            TargetCamera.farClipPlane = 10000f;
            TargetCamera.nearClipPlane = 0.1f;

            var world = World.DefaultGameObjectInjectionWorld;
            _entityManager = world.EntityManager;

            _cameraEntity = _entityManager.CreateEntity();
            _entityManager.SetName(_cameraEntity, "CameraController");

            // --- ФИКС СТАРТОВОЙ ПОЗИЦИИ ---
            // Центрируем камеру на карте (500, 500), если карта 1000x1000
            // Ищем настройки карты (MapSize), чтобы узнать центр
            var startFocus = new float3(0, 0, 0);
            if (TryGetMapSize(out var mapSize)) startFocus = new float3(mapSize.x * 0.5f, 0, mapSize.y * 0.5f);

            var startZoom = 800f;
            var startPitch = 60f;
            var startYaw = 0f;

            if (Mode == CameraMode.TopDown2D)
            {
                startPitch = 90f;
                startYaw = 0f;
            }

            if (Mode == CameraMode.Isometric)
            {
                startPitch = 45f;
                startYaw = 45f;
            }

            _entityManager.AddComponentData(_cameraEntity, new CameraSettingsData
            {
                Mode = Mode,
                PanSpeed = PanSpeed,
                ZoomSpeed = ZoomSpeed,
                RotateSpeed = RotateSpeed,
                MinZoom = MinZoom,
                MaxZoom = MaxZoom,
                Smoothing = Smoothness,
                InvertZoom = InvertZoom,
                InvertPan = InvertPan,

                TargetFocusPoint = new float3(startFocus.x, startZoom, startFocus.z), // Y хранит Zoom
                TargetYaw = startYaw,
                TargetPitch = startPitch,

                IsInitialized = false
            });

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized || !_entityManager.Exists(_cameraEntity)) return;

            var currentData = _entityManager.GetComponentData<CameraSettingsData>(_cameraEntity);

            currentData.Mode = Mode;
            currentData.PanSpeed = PanSpeed;
            currentData.ZoomSpeed = ZoomSpeed;
            currentData.RotateSpeed = RotateSpeed;
            currentData.MinZoom = MinZoom;
            currentData.MaxZoom = MaxZoom;
            currentData.Smoothing = Smoothness;
            currentData.InvertZoom = InvertZoom;
            currentData.InvertPan = InvertPan;

            _entityManager.SetComponentData(_cameraEntity, currentData);
        }

        private bool TryGetMapSize(out float2 size)
        {
            size = new float2(1000, 1000); // Default fallback
            
            // Пытаемся найти настройки генератора на этом же объекте или в сцене
            MapGeneratorBootstrap gen;
            
#if UNITY_2023_1_OR_NEWER
            gen = FindFirstObjectByType<MapGeneratorBootstrap>();
#else
            gen = FindObjectOfType<MapGeneratorBootstrap>();
#endif
            
            if (gen != null)
            {
                size = gen.MapSize;
                return true;
            }

            return false;
        }
    }
}