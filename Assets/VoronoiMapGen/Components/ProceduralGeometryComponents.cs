using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections; 
using UnityEngine;

namespace VoronoiMapGen.Components
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

    public struct ProceduralMeshRequest : IComponentData
    {
        public bool IsDirty; 
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

    // Вот этот потерянный тег:
    public struct UnifiedRenderTag : IComponentData {}
}