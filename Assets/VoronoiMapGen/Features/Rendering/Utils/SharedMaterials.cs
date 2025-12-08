using System.Collections.Generic;
using UnityEngine;

namespace VoronoiMapGen.Utils
{
    public static class SharedMaterials
    {
        private static readonly Dictionary<string, Material> _cache = new();

        public static Material Get(string shaderName, Color color, float smoothness = 0f)
        {
            // Ключ для кэша
            var key = $"{shaderName}_{color}_{smoothness}";

            if (_cache.TryGetValue(key, out var mat))
                if (mat != null)
                    return mat;

            var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");

            mat = new Material(shader);
            mat.enableInstancing = true; // Важно для батчинга
            mat.color = color;
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Cull", 0); // Off

            _cache[key] = mat;
            return mat;
        }

        public static void Clear()
        {
            foreach (var mat in _cache.Values)
                if (mat != null)
                    Object.DestroyImmediate(mat);
            _cache.Clear();
        }
    }
}