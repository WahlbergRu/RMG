using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GlobalOceanSystem : SystemBase
    {
        private bool _isCreated = false;
        private Material _oceanMat;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
            RequireForUpdate<MapGeneratedTag>();
            
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            
            _oceanMat = new Material(shader);
            _oceanMat.color = new Color(0.05f, 0.25f, 0.7f, 1.0f);
            _oceanMat.SetFloat("_Smoothness", 0.8f);
        }

        protected override void OnUpdate()
        {
            if (_isCreated) return;
            
            var settings = SystemAPI.GetSingleton<MapSettings>();
            CreateOceanPlane(settings.MapSize);
            _isCreated = true;
        }

        private void CreateOceanPlane(float2 mapSize)
        {
            var em = EntityManager;
            var entity = em.CreateEntity();

            float w = mapSize.x * 3.0f;
            float h = mapSize.y * 3.0f;
            float2 center = mapSize * 0.5f;

            Mesh mesh = new Mesh { name = "GlobalOcean" };
            
            Vector3[] verts = new Vector3[]
            {
                new Vector3(center.x - w, 0, center.y - h), 
                new Vector3(center.x + w, 0, center.y - h), 
                new Vector3(center.x - w, 0, center.y + h), 
                new Vector3(center.x + w, 0, center.y + h)  
            };
            
            int[] tris = new int[] { 0, 2, 1, 2, 3, 1 };
            Vector3[] normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            RenderMeshUtility.AddComponents(
                entity, 
                em, 
                new RenderMeshDescription(UnityEngine.Rendering.ShadowCastingMode.Off), 
                new RenderMeshArray(new[] { _oceanMat }, new[] { mesh }), 
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );

            // --- ИЗМЕНЕНИЕ: Опускаем воду на -50 ---
            em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
            em.AddComponentData(entity, new LocalTransform 
            { 
                Position = new float3(0, -50.0f, 0), // <--- БЫЛО -0.5f, СТАЛО -50.0f
                Rotation = quaternion.identity,
                Scale = 1.0f
            });
            em.AddComponentData(entity, new RenderBounds { Value = mesh.bounds.ToAABB() });
        }
    }
}