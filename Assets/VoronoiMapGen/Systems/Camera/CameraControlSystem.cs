using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    // Запускаем в PresentationSystemGroup, чтобы движение было плавным каждый кадр
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CameraControlSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // 1. Проверки на наличие необходимых данных
            if (Camera.main == null) return;
            if (!SystemAPI.TryGetSingletonRW<CameraSettingsData>(out var cameraSettingsRef)) return;
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var mapSettings)) return;

            ref var settings = ref cameraSettingsRef.ValueRW;
            float dt = SystemAPI.Time.DeltaTime;
            
            // 2. Инициализация (ставим камеру в центр карты при первом запуске)
            if (!settings.IsInitialized)
            {
                float centerX = mapSettings.MapSize.x * 0.5f;
                float centerZ = mapSettings.MapSize.y * 0.5f;
                // Стартуем с высоты 80% от максимума, чтобы видеть всю карту
                float startY = settings.MaxHeight * 0.8f; 

                var startPos = new float3(centerX, startY, centerZ);
                
                
                Camera.main.transform.position = startPos;
                Camera.main.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Строго вниз
                
                settings.TargetPosition = startPos;
                settings.IsInitialized = true;
                return;
            }

            // 3. Ввод данных (Input)
            // Используем Legacy Input для простоты. Если у вас New Input System, замените эти строки.
            float moveX = Input.GetAxis("Horizontal"); // A/D
            float moveZ = Input.GetAxis("Vertical");   // W/S
            float scroll = Input.mouseScrollDelta.y;   // Колесико

            // 4. Расчет высотного множителя (Height Factor)
            // Чем выше камера, тем быстрее она должна двигаться.
            // Нормализуем высоту от 0 до 1 (примерно) или используем линейную зависимость.
            float currentHeight = settings.TargetPosition.y;
            // Если мы на высоте MinHeight -> множитель ~0.1 (медленно)
            // Если мы на высоте MaxHeight -> множитель 1.0 (быстро)
            float heightRatio = math.clamp(currentHeight / settings.MaxHeight, 0.05f, 1.0f);

            // 5. Логика Зума (Zoom)
            if (math.abs(scroll) > 0.01f)
            {
                // Зум тоже должен зависеть от текущей высоты, чтобы не пролетать сквозь землю мгновенно
                float zoomStep = scroll * settings.ZoomSpeed * heightRatio * 5.0f * dt; // x5 для чувствительности
                settings.TargetPosition.y -= zoomStep;
            }

            // Ограничиваем высоту (Clamp Zoom)
            settings.TargetPosition.y = math.clamp(settings.TargetPosition.y, settings.MinHeight, settings.MaxHeight);

            // 6. Логика Панорамирования (Pan)
            if (math.abs(moveX) > 0.01f || math.abs(moveZ) > 0.01f)
            {
                // Вектор движения в плоскости XZ
                float3 moveDir = new float3(moveX, 0, moveZ);
                
                // Скорость зависит от высоты (heightRatio)
                float currentPanSpeed = settings.PanSpeed * (1f + heightRatio * 5f); // *5 чтобы на верху летать быстро
                
                settings.TargetPosition += moveDir * currentPanSpeed * dt;
            }

            // 7. Ограничение границами карты (Clamp Bounds)
            // Добавляем отступы (padding), чтобы нельзя было улететь совсем в пустоту
            float padding = settings.MaxHeight * 0.5f; 
            settings.TargetPosition.x = math.clamp(settings.TargetPosition.x, -padding, mapSettings.MapSize.x + padding);
            settings.TargetPosition.z = math.clamp(settings.TargetPosition.z, -padding, mapSettings.MapSize.y + padding);

            // 8. Применение сглаживания (Lerp)
            // Двигаем реальную камеру к целевой позиции
            float3 newPos = math.lerp(Camera.main.transform.position, settings.TargetPosition, settings.Smoothing * dt);
            Camera.main.transform.position = newPos;
        }
    }
}