using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.Rendering.Components
{
    // Строгая последовательность полей важна для передачи данных в Mesh
    [StructLayout(LayoutKind.Sequential)] 
    public struct ProceduralVertex : IBufferElementData
    {
        public float3 Position; // 0
        public float3 Normal;   // 12
        public float4 Color;    // 24 (RGBA)
        public float2 UV;       // 40
        // Total size: 48 bytes
    }

    public struct ProceduralIndex : IBufferElementData
    {
        public int Value;
    }

    public struct MeshDirtyTag : IComponentData, IEnableableComponent 
    { 
    }

    public struct ProceduralMeshRequest : IComponentData
    {
        public FixedString64Bytes MaterialName;
        // Возвращаем поле Color (TINT)
        public float4 Color; 
        public float Smoothness;
    }

    public struct ProceduralMeshReference : ICleanupComponentData
    {
        public int MeshInstanceID;
    }

    public struct UnifiedRenderTag : IComponentData
    {
    }
}