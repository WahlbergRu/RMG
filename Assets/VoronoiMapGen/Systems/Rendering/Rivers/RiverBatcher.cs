using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class RiverBatcher
    {
        public static void FlushChunk(
            EntityManager em, Material mat,
            List<Vector3> verts, List<int> tris, List<Vector2> uvs,
            List<Mesh> meshesToTrack)
        {
            if (verts.Count == 0) return;

            // 1. Создаем Unity Mesh
            Mesh m = new Mesh();
            m.name = "RiverChunk";
            // Позволяем мешу быть большим
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.SetUVs(0, uvs);
            
            m.RecalculateNormals();
            m.RecalculateBounds();

            // 2. Регистрируем для Garbage Collection
            meshesToTrack.Add(m);

            // 3. Создаем ECS Entity
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
            em.AddComponentData(e, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 1.0f });
            
            // Тэг для системы, чтобы знать, что это река и удалить её
            em.AddComponent<RiverChunkTag>(e);

            RenderMeshUtility.AddComponents(e, em, 
                new RenderMeshDescription(UnityEngine.Rendering.ShadowCastingMode.Off, false), 
                new RenderMeshArray(new[] { mat }, new[] { m }), 
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                
            // Базовый цвет для URP (на случай потери текстуры)
            em.AddComponentData(e, new URPMaterialPropertyBaseColor { Value = new float4(0, 0.5f, 1f, 0.8f) });

            // 4. Очищаем списки для следующего батча
            verts.Clear(); 
            tris.Clear(); 
            uvs.Clear();
        }
    }
}