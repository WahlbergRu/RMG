using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Camera.Components;

namespace VoronoiMapGen.Features.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CameraControlSystem : SystemBase
    {
        private float3 _currentFocus;
        private float _currentPitch;
        private float _currentYaw;
        private float _currentZoom;

        protected override void OnUpdate()
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;

            if (!SystemAPI.TryGetSingletonRW<CameraSettingsData>(out var settingsRw)) return;
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var mapSettings)) return;

            ref var settings = ref settingsRw.ValueRW;
            
            // Фиксим возможные скачки времени (макс. 0.1 сек), чтобы физика камеры не ломалась при низком FPS
            var dt = math.min(SystemAPI.Time.DeltaTime, 0.1f);

            // 1. Инициализация (Hard Reset при старте)
            if (!settings.IsInitialized)
            {
                _currentFocus = new float3(settings.TargetFocusPoint.x, 0, settings.TargetFocusPoint.z);
                _currentZoom = settings.TargetFocusPoint.y;
                _currentYaw = settings.TargetYaw;
                _currentPitch = settings.TargetPitch;

                ApplyCameraMode(camera, settings.Mode);
                settings.IsInitialized = true;

                UpdateCameraTransform(camera, _currentFocus, _currentZoom, _currentPitch, _currentYaw);
                return;
            }

            // 2. Ввод
            var moveX = Input.GetAxis("Horizontal");
            var moveZ = Input.GetAxis("Vertical");
            var scroll = Input.mouseScrollDelta.y;
            var rotateInput = 0f;

            if (Input.GetKey(KeyCode.Q)) rotateInput = 1f;
            if (Input.GetKey(KeyCode.E)) rotateInput = -1f;
            if (Input.GetMouseButton(1)) rotateInput = Input.GetAxis("Mouse X") * 3f;

            if (settings.InvertPan)
            {
                moveX = -moveX;
                moveZ = -moveZ;
            }

            if (settings.InvertZoom) scroll = -scroll;

            // Режимы
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
                settings.TargetYaw = 45f;
                rotateInput = 0f;
            }
            else // Free
            {
                settings.TargetPitch = math.clamp(settings.TargetPitch, 10f, 85f);
            }

            // 3. Обновление Целей
            var heightRatio = math.clamp((settings.TargetFocusPoint.y - settings.MinZoom) / (settings.MaxZoom - settings.MinZoom), 0f, 1f);
            var speedMult = 1f + heightRatio * 2f;

            if (math.abs(scroll) > 0.001f)
            {
                // --- FIX: УБРАЛИ dt ИЗ СКРОЛЛА ---
                // Колесо мыши - это дискретное событие (шаг), оно не зависит от времени кадра.
                // Если оставить dt, то при зависании игры (генерация LOD) на 0.5сек, зум умножался на огромный dt.
                // Умножаем на 0.02f, чтобы скомпенсировать отсутствие dt и сохранить примерную скорость настройки.
                float fixedTimeStep = 0.02f;
                settings.TargetFocusPoint.y -= scroll * settings.ZoomSpeed * speedMult * fixedTimeStep;
                
                settings.TargetFocusPoint.y = math.clamp(settings.TargetFocusPoint.y, settings.MinZoom, settings.MaxZoom);
            }

            if (math.abs(rotateInput) > 0.001f) settings.TargetYaw += rotateInput * settings.RotateSpeed * dt;

            if (math.abs(moveX) > 0.001f || math.abs(moveZ) > 0.001f)
            {
                var yawRad = math.radians(_currentYaw);
                var sin = math.sin(yawRad);
                var cos = math.cos(yawRad);

                var dx = moveX * cos + moveZ * sin;
                var dz = -moveX * sin + moveZ * cos;

                settings.TargetFocusPoint.x += dx * settings.PanSpeed * speedMult * dt;
                settings.TargetFocusPoint.z += dz * settings.PanSpeed * speedMult * dt;
            }

            // Clamp Focus to Map
            var border = settings.MaxZoom * 0.5f;
            settings.TargetFocusPoint.x = math.clamp(settings.TargetFocusPoint.x, -border, mapSettings.MapSize.x + border);
            settings.TargetFocusPoint.z = math.clamp(settings.TargetFocusPoint.z, -border, mapSettings.MapSize.y + border);

            // 4. Интерполяция
            var t = 1.0f - math.exp(-settings.Smoothing * dt);

            _currentFocus = math.lerp(_currentFocus, new float3(settings.TargetFocusPoint.x, 0, settings.TargetFocusPoint.z), t);
            _currentZoom = math.lerp(_currentZoom, settings.TargetFocusPoint.y, t);
            _currentPitch = math.lerp(_currentPitch, settings.TargetPitch, t);
            _currentYaw = Mathf.LerpAngle(_currentYaw, settings.TargetYaw, t);

            // 5. Финальное обновление
            UpdateCameraTransform(camera, _currentFocus, _currentZoom, _currentPitch, _currentYaw);
        }

        private void UpdateCameraTransform(UnityEngine.Camera cam, float3 focus, float zoom, float pitch, float yaw)
        {
            if (math.any(math.isnan(focus))) return;

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset;

            if (cam.orthographic)
            {
                cam.orthographicSize = zoom;
                offset = Vector3.back * 1000f; // Отодвигаем ортогональную камеру назад, чтобы не срезало горы
            }
            else
            {
                offset = Vector3.back * zoom;
            }

            var finalOffset = rotation * offset;
            cam.transform.position = (Vector3)focus + finalOffset;
            cam.transform.rotation = rotation;
        }

        private void ApplyCameraMode(UnityEngine.Camera cam, CameraMode mode)
        {
            if (mode == CameraMode.Free)
                cam.orthographic = false;
            else
                cam.orthographic = true;
        }
    }
}