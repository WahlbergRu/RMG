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
                    // ИСПРАВЛЕНИЕ: Используем более новый API если возможно, или старый
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<UnifiedResourceManager>();
#else
                    _instance = Object.FindObjectOfType<UnifiedResourceManager>();
#endif

                    if (_instance == null)
                    {
                        var go = new GameObject("UnifiedResourceManager");
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

            var frame = Time.frameCount;
            while (_graveyard.Count > 0)
                if (frame >= _graveyard.Peek().dieTime)
                {
                    var item = _graveyard.Dequeue();
                    if (item.obj != null) Destroy(item.obj);
                }
                else
                {
                    break;
                }
        }

        private void OnDestroy()
        {
            foreach (var kvp in _materials)
                if (kvp.Value)
                    DestroyImmediate(kvp.Value);
            _materials.Clear();

            while (_graveyard.Count > 0)
            {
                var item = _graveyard.Dequeue();
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
            var key = $"{shaderName}_{color}_{smoothness}";
            if (_materials.TryGetValue(key, out var mat) && mat != null) return mat;

            var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");

            mat = new Material(shader);
            mat.enableInstancing = true;
            mat.color = new Color(color.x, color.y, color.z, color.w);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Cull", 0);

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

            var delayFrames = Application.isPlaying ? 5 : 1;
            _graveyard.Enqueue(new DeadItem { obj = obj, dieTime = Time.frameCount + delayFrames });
        }

        private struct DeadItem
        {
            public Object obj;
            public int dieTime;
        }
    }
}