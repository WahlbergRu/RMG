using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoronoiMapGen.Features.Rendering
{
    public class UnifiedResourceManager : MonoBehaviour
    {
        private static UnifiedResourceManager _instance;
        private static bool _isQuitting;
        private readonly Queue<DeadItem> _graveyard = new();
        private readonly Dictionary<string, Material> _materials = new();

        public static UnifiedResourceManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<UnifiedResourceManager>();
#else
                    _instance = Object.FindObjectOfType<UnifiedResourceManager>();
#endif
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("UnifiedResourceManager");
                        _instance = go.AddComponent<UnifiedResourceManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        private void Update()
        {
            if (_isQuitting) return;
            if (_graveyard.Count == 0) return;

            int frame = Time.frameCount;
            while (_graveyard.Count > 0)
                if (frame >= _graveyard.Peek().dieTime)
                {
                    DeadItem item = _graveyard.Dequeue();
                    if (item.obj != null) Destroy(item.obj);
                }
                else
                {
                    break;
                }
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<string, Material> kvp in _materials)
                if (kvp.Value) DestroyImmediate(kvp.Value);
            _materials.Clear();
            while (_graveyard.Count > 0)
            {
                DeadItem item = _graveyard.Dequeue();
                if (item.obj != null) DestroyImmediate(item.obj);
            }
        }

        public void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _isQuitting = false;
        }

        public static UnifiedResourceManager TryGetInstance()
        {
            if (_isQuitting) return null;
            if (_instance != null) return _instance;
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<UnifiedResourceManager>();
#else
            return Object.FindObjectOfType<UnifiedResourceManager>();
#endif
        }

        public Material GetMaterial(string shaderName, float4 color, float smoothness)
        {
            string key = $"{shaderName}_{color}_{smoothness}";
            if (_materials.TryGetValue(key, out Material mat) && mat != null) return mat;

            // Ищем шейдер
            Shader shader = Shader.Find(shaderName);
            if (!shader) 
            {
                // Fallbacks
                shader = Shader.Find("Universal Render Pipeline/Particles/Lit"); 
                if(!shader) shader = Shader.Find("Universal Render Pipeline/Lit");
                if(!shader) shader = Shader.Find("Standard");
            }

            mat = new Material(shader);
            mat.enableInstancing = true;
            mat.color = new Color(color.x, color.y, color.z, color.w);
            mat.SetFloat("_Smoothness", smoothness);
            
            // --- FIX FOR PARTICLE SHADER OPACITY ---
            // Если мы используем Particle Shader, заставляем его быть Opaque,
            // чтобы карта нормально рендерилась в глубину и принимала тени
            if (shader.name.Contains("Particles"))
            {
                mat.SetFloat("_Mode", 0); // 0 = Opaque, 2 = Fade
                mat.SetFloat("_Surface", 0); // 0 = Opaque, 1 = Transparent (URP param)
                mat.SetInt("_ZWrite", 1);
                mat.renderQueue = 2000; // Geometry queue
                mat.EnableKeyword("_SURFACE_TYPE_OPAQUE");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Opaque");
            }
            // ---------------------------------------

            _materials[key] = mat;
            return mat;
        }

        public void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (!Application.isPlaying || _isQuitting)
            {
                DestroyImmediate(obj);
                return;
            }
            int delayFrames = Application.isPlaying ? 5 : 1;
            _graveyard.Enqueue(new DeadItem { obj = obj, dieTime = Time.frameCount + delayFrames });
        }

        private struct DeadItem
        {
            public Object obj;
            public int dieTime;
        }
    }
}