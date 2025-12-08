using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapCameraBootstrap : MonoBehaviour
    {
        [Header("References")]
        public Camera TargetCamera;

        [Header("Mode")]
        public CameraMode Mode = CameraMode.Free;

        [Header("Controls")]
        public float PanSpeed = 50f;
        public float ZoomSpeed = 100f;
        public float RotateSpeed = 150f;
        public bool InvertZoom = false;
        public bool InvertPan = false;
        
        [Header("Limits")]
        public float MinZoom = 20f;   
        public float MaxZoom = 1500f; 
        
        [Range(1f, 20f)]
        public float Smoothness = 10f;

        private Entity _cameraEntity;
        private EntityManager _entityManager;
        private bool _isInitialized;

        void Start()
        {
            if (TargetCamera == null) TargetCamera = Camera.main;

            var world = World.DefaultGameObjectInjectionWorld;
            _entityManager = world.EntityManager;

            _cameraEntity = _entityManager.CreateEntity();
            _entityManager.SetName(_cameraEntity, "CameraController");

            // Рассчитываем начальную точку фокуса (бросаем луч в центр экрана)
            float3 startFocus = float3.zero;
            float startZoom = 500f;
            float startPitch = 60f;
            float startYaw = 0f;

            // Пытаемся найти точку на земле, куда смотрит камера
            Ray ray = new Ray(TargetCamera.transform.position, TargetCamera.transform.forward);
            if (new Plane(Vector3.up, 0).Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                startFocus = new float3(hitPoint.x, 0, hitPoint.z);
                startZoom = Vector3.Distance(TargetCamera.transform.position, hitPoint);
            }
            
            // Если режим Изометрия или 2D - форсируем начальные углы
            if (Mode == CameraMode.TopDown2D) { startPitch = 90f; startYaw = 0f; }
            if (Mode == CameraMode.Isometric) { startPitch = 45f; startYaw = 45f; }

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

        void Update()
        {
            if (!_isInitialized || !_entityManager.Exists(_cameraEntity)) return;

            var currentData = _entityManager.GetComponentData<CameraSettingsData>(_cameraEntity);

            // Live Update настроек
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
    }
}