using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.Camera.Components
{
    public enum CameraMode
    {
        Free, // Свободное вращение (Перспектива)
        Isometric, // Фиксированный угол 45 градусов (Ортография)
        TopDown2D // Строго сверху (Ортография)
    }

    public struct CameraSettingsData : IComponentData
    {
        // Настройки
        public CameraMode Mode;
        public float PanSpeed;
        public float ZoomSpeed;
        public float RotateSpeed; // <-- НОВОЕ
        public float Smoothing;

        // Лимиты
        public float MinZoom;
        public float MaxZoom;

        public bool InvertZoom;
        public bool InvertPan;

        // Состояние (Target)
        // .x, .z = Координаты точки на земле, куда смотрим
        // .y = Дистанция (Zoom)
        public float3 TargetFocusPoint;

        public float TargetYaw; // Вращение по горизонтали (0..360)
        public float TargetPitch; // Наклон (10..90)

        public bool IsInitialized;
    }
}