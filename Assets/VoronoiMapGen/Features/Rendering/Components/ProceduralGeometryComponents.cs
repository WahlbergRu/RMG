using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.Rendering.Components
{
    // --- ДАННЫЕ ГЕОМЕТРИИ (Буферы) ---

    public struct ProceduralVertex : IBufferElementData
    {
        public float3 Position;
        public float3 Normal;
        public float2 UV;
    }

    public struct ProceduralIndex : IBufferElementData
    {
        public int Value;
    }

    // --- УПРАВЛЯЮЩИЕ КОМПОНЕНТЫ ---

    /// <summary>
    /// Компонент-ТЭГ для отслеживания изменений.
    /// Реализует IEnableableComponent: когда он Disabled, Unity считает, что его НЕТ на сущности
    /// для соответствующих запросов. Это дает Query.IsEmpty = true (0.00ms latency).
    /// </summary>
    public struct MeshDirtyTag : IComponentData, IEnableableComponent 
    { 
    }

    public struct ProceduralMeshRequest : IComponentData
    {
        // Поле 'IsDirty' удалено ради оптимизации через IEnableableComponent
        
        public FixedString64Bytes MaterialName;
        public float4 Color;
        public float Smoothness;
        public int SortOrder;
    }

    public struct ProceduralMeshReference : ICleanupComponentData
    {
        public int MeshInstanceID;
    }

    // --- СИСТЕМНЫЕ ТЕГИ ---
    public struct UnifiedRenderTag : IComponentData
    {
    }
}