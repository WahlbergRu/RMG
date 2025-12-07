using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Components
{
    // Промежуточные данные для создания ECS компонентов после генерации меша
    public struct BakeData
    {
        public Entity Entity;
        public int MeshIndex;
        public LocalTransform Transform;
        public float4 Color;
    }

    // Все данные, необходимые для генерации конкретной ячейки
    public struct GenerationContext
    {
        public TerrainVisualData Style;
        public float BaseHeight;
        public float BottomDepth;
        public float3 CenterPos;
        public bool IsWater;
        public float4 Color;
    }
}