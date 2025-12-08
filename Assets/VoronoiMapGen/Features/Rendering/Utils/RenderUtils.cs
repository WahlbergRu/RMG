using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Rendering.Utils
{
    public static class RenderUtils
    {
        public static float4 GetBiomeColor(BiomeType type)
        {
            switch (type)
            {
                case BiomeType.Ocean: return new float4(0.0f, 0.2f, 0.7f, 1.0f);
                case BiomeType.Coast: return new float4(0.8f, 0.7f, 0.5f, 1.0f);
                case BiomeType.Ice: return new float4(0.9f, 0.95f, 1.0f, 1.0f);
                case BiomeType.Desert: return new float4(0.8f, 0.6f, 0.3f, 1.0f);
                case BiomeType.Grassland: return new float4(0.2f, 0.6f, 0.2f, 1.0f);
                case BiomeType.Forest: return new float4(0.0f, 0.4f, 0.1f, 1.0f);
                case BiomeType.Mountain: return new float4(0.4f, 0.4f, 0.4f, 1.0f);
                case BiomeType.Snow: return new float4(0.9f, 0.9f, 0.9f, 1.0f);
                default: return new float4(0.5f, 0.5f, 0.5f, 1);
            }
        }

        public static Material EnsureMaterial(string shaderName = "Universal Render Pipeline/Lit")
        {
            var shader = Shader.Find(shaderName) ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.enableInstancing = true;
            mat.SetFloat("_Smoothness", 0.2f); // Матовый
            return mat;
        }
    }
}