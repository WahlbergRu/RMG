using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CameraControlSystem : SystemBase
    {
        // Текущие значения для сглаживания (интерполяции)
        private float3 _currentFocus;
        private float _currentZoom;
        private float _currentYaw;
        private float _currentPitch;

        protected override void OnUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;

            if (!SystemAPI.TryGetSingletonRW<CameraSettingsData>(out var settingsRw)) return;
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var mapSettings)) return;

            ref var settings = ref settingsRw.ValueRW;
            float dt = SystemAPI.Time.DeltaTime;

            // 1. Инициализация локальных переменных (чтобы не прыгало при старте)
            if (!settings.IsInitialized)
            {
                _currentFocus = new float3(settings.TargetFocusPoint.x, 0, settings.TargetFocusPoint.z);
                _currentZoom = settings.TargetFocusPoint.y;
                _currentYaw = settings.TargetYaw;
                _currentPitch = settings.TargetPitch;
                
                ApplyCameraMode(camera, settings.Mode);
                settings.IsInitialized = true;
            }

            // -----------------------------------------------------------------------
            // 2. ВВОД
            // -----------------------------------------------------------------------
            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");
            float scroll = Input.mouseScrollDelta.y;
            float rotateInput = 0f;

            if (Input.GetKey(KeyCode.Q)) rotateInput = 1f;
            if (Input.GetKey(KeyCode.E)) rotateInput = -1f;
            if (Input.GetMouseButton(1)) rotateInput = Input.GetAxis("Mouse X") * 2f; 

            if (settings.InvertPan) { moveX = -moveX; moveZ = -moveZ; }
            if (settings.InvertZoom) { scroll = -scroll; }

            // -----------------------------------------------------------------------
            // 3. ЛОГИКА РЕЖИМОВ
            // -----------------------------------------------------------------------
            
            ApplyCameraMode(camera, settings.Mode);

            if (settings.Mode == CameraMode.TopDown2D)
            {
                settings.TargetPitch = 90f;
                settings.TargetYaw = 0f;
                rotateInput = 0f;
            }
            else if (settings.Mode == CameraMode.Isometric)
            {
                settings.TargetPitch = 45f;
                // Для изометрии фиксируем угол, чтобы не ломать перспективу
                settings.TargetYaw = 45f; 
                rotateInput = 0f;
            }
            else // Free
            {
                settings.TargetPitch = math.clamp(settings.TargetPitch, 10f, 85f);
            }

            // -----------------------------------------------------------------------
            // 4. ОБНОВЛЕНИЕ ЦЕЛЕВЫХ ЗНАЧЕНИЙ
            // -----------------------------------------------------------------------

            // Зум
            float heightRatio = math.clamp((settings.TargetFocusPoint.y - settings.MinZoom) / (settings.MaxZoom - settings.MinZoom), 0f, 1f);
            float speedMult = 1f + heightRatio * 2f;

            if (math.abs(scroll) > 0.001f)
            {
                settings.TargetFocusPoint.y -= scroll * settings.ZoomSpeed * speedMult * dt;
                settings.TargetFocusPoint.y = math.clamp(settings.TargetFocusPoint.y, settings.MinZoom, settings.MaxZoom);
            }

            // Вращение
            if (math.abs(rotateInput) > 0.001f)
            {
                settings.TargetYaw += rotateInput * settings.RotateSpeed * dt;
            }

            // Перемещение
            if (math.abs(moveX) > 0.001f || math.abs(moveZ) > 0.001f)
            {
                float yawRad = math.radians(_currentYaw);
                float sin = math.sin(yawRad);
                float cos = math.cos(yawRad);

                float dx = moveX * cos + moveZ * sin;
                float dz = -moveX * sin + moveZ * cos;

                settings.TargetFocusPoint.x += dx * settings.PanSpeed * speedMult * dt;
                settings.TargetFocusPoint.z += dz * settings.PanSpeed * speedMult * dt;
            }

            // Ограничение
            float border = settings.MaxZoom * 0.5f; 
            settings.TargetFocusPoint.x = math.clamp(settings.TargetFocusPoint.x, -border, mapSettings.MapSize.x + border);
            settings.TargetFocusPoint.z = math.clamp(settings.TargetFocusPoint.z, -border, mapSettings.MapSize.y + border);

            // -----------------------------------------------------------------------
            // 5. ИНТЕРПОЛЯЦИЯ И ПРИМЕНЕНИЕ
            // -----------------------------------------------------------------------
            float t = 1.0f - math.exp(-settings.Smoothing * dt);

            _currentFocus = math.lerp(_currentFocus, new float3(settings.TargetFocusPoint.x, 0, settings.TargetFocusPoint.z), t);
            _currentZoom = math.lerp(_currentZoom, settings.TargetFocusPoint.y, t);
            _currentPitch = math.lerp(_currentPitch, settings.TargetPitch, t);
            
            // --- ИСПРАВЛЕНИЕ ЗДЕСЬ ---
            // Используем Unity MathF для углов, так как в Unity.Mathematics этого метода нет
            _currentYaw = Mathf.LerpAngle(_currentYaw, settings.TargetYaw, t);

            // Финал
            Quaternion rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 offset = Vector3.back * _currentZoom;
            
            if (camera.orthographic)
            {
                camera.orthographicSize = _currentZoom;
                offset = Vector3.back * 2000f; // Отодвигаем далеко назад, чтобы не клипалось
            }

            Vector3 finalOffset = rotation * offset;
            
            camera.transform.position = (Vector3)_currentFocus + finalOffset;
            camera.transform.rotation = rotation;
        }

        private void ApplyCameraMode(Camera cam, CameraMode mode)
        {
            if (mode == CameraMode.Free)
            {
                cam.orthographic = false;
            }
            else
            {
                cam.orthographic = true;
            }
        }
    }
}