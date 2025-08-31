using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Rendering
{
    public static class BiomeColors
    {
        public static float4 Get(BiomeType type) => type switch
        {
            BiomeType.Ocean     => new float4(0.1f, 0.3f, 0.8f, 1),
            BiomeType.Coast     => new float4(0.9f, 0.8f, 0.6f, 1),
            BiomeType.Ice       => new float4(0.8f, 0.9f, 1.0f, 1),
            BiomeType.Desert    => new float4(0.9f, 0.8f, 0.5f, 1),
            BiomeType.Grassland => new float4(0.3f, 0.7f, 0.2f, 1),
            BiomeType.Forest    => new float4(0.1f, 0.5f, 0.1f, 1),
            BiomeType.Mountain  => new float4(0.5f, 0.4f, 0.3f, 1),
            BiomeType.Snow      => new float4(0.95f, 0.95f, 0.95f, 1),
            _                   => new float4(1, 1, 1, 1)
        };
    }
}