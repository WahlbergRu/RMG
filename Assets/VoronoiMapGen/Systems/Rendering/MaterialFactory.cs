using UnityEngine;

namespace VoronoiMapGen.Rendering
{
    public static class MaterialFactory
    {
        public static Material Create(string shaderName, string name, bool instancing, Color? color = null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{shaderName}' not found! Ensure URP is installed.");
                return null;
            }

            var mat = new Material(shader)
            {
                name = name,
                enableInstancing = instancing
            };

            if (color.HasValue) mat.color = color.Value;
            return mat;
        }
    }
}