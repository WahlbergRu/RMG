using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Bootstrap
{
    public class MapCameraBootstrap : MonoBehaviour
    {
        [Header("References")]
        public Camera TargetCamera; // Ссылка на камеру, чтобы определять режим (Ortho/Perspective)

        [Header("Camera Controls")]
        public float PanSpeed = 50f;
        public float ZoomSpeed = 100f;
        
        [Tooltip("Для Perspective: Высота (Y).\nДля Orthographic: Размер (Size).")]
        public float MinHeight = 20f;   
        
        [Tooltip("Для Perspective: Высота (Y).\nДля Orthographic: Размер (Size).")]
        public float MaxHeight = 1500f; 
        
        [Range(1f, 20f)]
        public float Smoothness = 5f;

        private Entity _cameraEntity;
        private EntityManager _entityManager;
        private bool _isInitialized;

        void Start()
        {
            if (TargetCamera == null) TargetCamera = Camera.main;

            var world = World.DefaultGameObjectInjectionWorld;
            _entityManager = world.EntityManager;

            // Создаем сущность
            _cameraEntity = _entityManager.CreateEntity();
            _entityManager.SetName(_cameraEntity, "CameraController");

            // Инициализируем начальными данными
            _entityManager.AddComponentData(_cameraEntity, new CameraSettingsData
            {
                PanSpeed = PanSpeed,
                ZoomSpeed = ZoomSpeed,
                MinHeight = MinHeight,
                MaxHeight = MaxHeight,
                Smoothing = Smoothness,
                TargetPosition = float3.zero, 
                IsInitialized = false
                // Если в вашей структуре CameraSettingsData есть поле типа IsOrthographic, 
                // добавьте его инициализацию здесь:
                // IsOrthographic = TargetCamera.orthographic
            });

            _isInitialized = true;
        }

        void Update()
        {
            // Если сущность еще не создана или была уничтожена, выходим
            if (!_isInitialized || !_entityManager.Exists(_cameraEntity)) return;

            // 1. Получаем ТЕКУЩИЕ данные из ECS (чтобы сохранить TargetPosition, который меняет Система)
            var currentData = _entityManager.GetComponentData<CameraSettingsData>(_cameraEntity);

            // 2. Обновляем только настроечные параметры из Инспектора (Live Link)
            currentData.PanSpeed = PanSpeed;
            currentData.ZoomSpeed = ZoomSpeed;
            currentData.MinHeight = MinHeight;
            currentData.MaxHeight = MaxHeight;
            currentData.Smoothing = Smoothness;

            // Опционально: если вы добавите поле IsOrthographic в компонент данных, обновляйте его тут:
            // currentData.IsOrthographic = TargetCamera != null && TargetCamera.orthographic;

            // 3. Записываем обновленные данные обратно в ECS
            _entityManager.SetComponentData(_cameraEntity, currentData);
        }
    }
}