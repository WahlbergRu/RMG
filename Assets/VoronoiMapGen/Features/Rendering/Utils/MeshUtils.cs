using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Utils
{
    public static class MeshUtils
    {
        public static Entity CreateMeshEntity(EntityManager em, Mesh mesh, Material material, float3 position,
            float4 color)
        {
            var entity = em.CreateEntity();

            // Создаем RenderMeshUnmanaged
            UnityObjectRef<Mesh> meshRef = mesh;
            UnityObjectRef<Material> materialRef = material;

            em.AddComponentData(entity, new RenderMeshUnmanaged(
                meshRef,
                materialRef
            ));

            // Добавляем необходимые компоненты
            em.AddComponent<LocalToWorld>(entity);
            em.AddComponentData(entity, new RenderBounds
            {
                Value = mesh.bounds.ToAABB()
            });
            em.AddComponentData(entity, new LocalTransform
            {
                Position = position,
                Rotation = quaternion.identity,
                Scale = 1.0f
            });
            em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = color });

            return entity;
        }
    }
}