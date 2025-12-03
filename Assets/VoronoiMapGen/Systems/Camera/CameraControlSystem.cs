using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CameraControlSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // 1. Получаем ссылки и данные
            // Если камеры нет, ничего не делаем
            var camera = Camera.main;
            if (camera == null) return;

            // Пытаемся получить компоненты настроек
            if (!SystemAPI.TryGetSingletonRW<CameraSettingsData>(out var settingsRw)) return;
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var mapSettings)) return;

            // Работаем с данными
            ref var settings = ref settingsRw.ValueRW;
            float dt = SystemAPI.Time.DeltaTime;

            // -----------------------------------------------------------------------
            // 2. Инициализация (Первый запуск)
            // -----------------------------------------------------------------------
            if (!settings.IsInitialized)
            {
                InitializeCamera(camera, ref settings, mapSettings);
                return;
            }

            // -----------------------------------------------------------------------
            // 3. Обработка Ввода (Input)
            // -----------------------------------------------------------------------
            float moveX = Input.GetAxis("Horizontal"); // A/D или стрелки
            float moveZ = Input.GetAxis("Vertical");   // W/S или стрелки
            float scroll = Input.mouseScrollDelta.y;   // Колесико мыши

            // -----------------------------------------------------------------------
            // 4. Логика Зума (Zoom)
            // -----------------------------------------------------------------------
            
            // Текущий уровень "зума" хранится в TargetPosition.y
            // (Для Ortho это Size, для Perspective это Высота)
            float currentZoomLevel = settings.TargetPosition.y;

            // Рассчитываем коэффициент высоты (0..1), чтобы менять скорость
            // Если мы на MinHeight -> ratio = 0, если на MaxHeight -> ratio = 1
            float zoomRatio = math.clamp((currentZoomLevel - settings.MinHeight) / (settings.MaxHeight - settings.MinHeight), 0f, 1f);
            
            // Базовая множитель скорости, чтобы на макс высоте летать быстрее
            float speedMultiplier = 1f + zoomRatio * 2f; 

            if (math.abs(scroll) > 0.001f)
            {
                // Скорость зума тоже зависит от текущей высоты (логарифмическое ощущение)
                float zoomDelta = scroll * settings.ZoomSpeed * speedMultiplier * dt;
                settings.TargetPosition.y -= zoomDelta;
            }

            // Ограничиваем зум (высоту или размер)
            settings.TargetPosition.y = math.clamp(settings.TargetPosition.y, settings.MinHeight, settings.MaxHeight);

            // -----------------------------------------------------------------------
            // 5. Логика Перемещения (Pan)
            // -----------------------------------------------------------------------
            if (math.abs(moveX) > 0.001f || math.abs(moveZ) > 0.001f)
            {
                float3 moveDir = new float3(moveX, 0, moveZ);
                
                // Если ортографическая камера, зум влияет на охват, значит скорость должна расти линейно с размером
                float panSpeed = settings.PanSpeed * speedMultiplier;

                settings.TargetPosition += moveDir * panSpeed * dt;
            }

            // -----------------------------------------------------------------------
            // 6. Ограничение границами карты (Clamping)
            // -----------------------------------------------------------------------
            float padding = settings.MaxHeight * 0.5f; // Запас по краям
            settings.TargetPosition.x = math.clamp(settings.TargetPosition.x, -padding, mapSettings.MapSize.x + padding);
            settings.TargetPosition.z = math.clamp(settings.TargetPosition.z, -padding, mapSettings.MapSize.y + padding);

            // -----------------------------------------------------------------------
            // 7. Применение к Unity Камере (Smoothing & Apply)
            // -----------------------------------------------------------------------
            
            // Интерполяция (Lerp) для плавности
            float smoothFactor = settings.Smoothing * dt;

            if (camera.orthographic)
            {
                // --- ORTHOGRAPHIC MODE ---
                
                // 1. Плавно меняем размер (Zoom)
                // Используем TargetPosition.y как целевой OrthographicSize
                camera.orthographicSize = math.lerp(camera.orthographicSize, settings.TargetPosition.y, smoothFactor);

                // 2. Плавно меняем позицию X и Z (Pan)
                // Y держим фиксированным (например, 100), чтобы не улететь за FarClipPlane
                float3 currentPos = camera.transform.position;
                float3 targetPosXZ = new float3(settings.TargetPosition.x, 100f, settings.TargetPosition.z);
                
                // Lerp только для X и Z
                float3 newPos = math.lerp(currentPos, targetPosXZ, smoothFactor);
                camera.transform.position = newPos;
            }
            else
            {
                // --- PERSPECTIVE MODE ---
                
                // Просто летим всей камерой к TargetPosition (где Y - это высота)
                float3 currentPos = camera.transform.position;
                float3 newPos = math.lerp(currentPos, settings.TargetPosition, smoothFactor);
                camera.transform.position = newPos;
            }
        }

        // Выносим инициализацию в отдельный метод для чистоты
        private void InitializeCamera(Camera camera, ref CameraSettingsData settings, MapSettings mapSettings)
        {
            float centerX = mapSettings.MapSize.x * 0.5f;
            float centerZ = mapSettings.MapSize.y * 0.5f;
            
            // Начальный зум (80% от максимума)
            float initialZoom = settings.MaxHeight * 0.8f;

            // Настраиваем поворот (смотрит вниз)
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (camera.orthographic)
            {
                // Для ортографии ставим камеру высоко физически
                camera.transform.position = new Vector3(centerX, 100f, centerZ);
                camera.orthographicSize = initialZoom;
                
                // TargetPosition хранит X, Zoom, Z
                settings.TargetPosition = new float3(centerX, initialZoom, centerZ);
            }
            else
            {
                // Для перспективы ставим камеру на нужную высоту
                Vector3 startPos = new Vector3(centerX, initialZoom, centerZ);
                camera.transform.position = startPos;
                
                settings.TargetPosition = startPos;
            }

            settings.IsInitialized = true;
        }
    }
}