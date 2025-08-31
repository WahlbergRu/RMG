using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Rendering
{
    public static class MeshUtils
    {
        public static (int, int) EdgeKey(int a, int b) => (math.min(a, b), math.max(a, b));

        public static Mesh CreateQuadMeshLocal(float2 a, float2 b, float3 centerWorld, float width, string name)
        {
            float3 aW = new float3(a.x, 0f, a.y);
            float3 bW = new float3(b.x, 0f, b.y);

            float3 aL = aW - centerWorld;
            float3 bL = bW - centerWorld;

            if (math.lengthsq(bL - aL) < 1e-8f) bL += new float3(0.0001f, 0f, 0f);

            float3 dir = math.normalize(bL - aL);
            float3 perp = new float3(-dir.z, 0f, dir.x) * (width * 0.5f);

            var verts = new[]
            {
                new Vector3(aL.x + perp.x, 0f, aL.z + perp.z),
                new Vector3(aL.x - perp.x, 0f, aL.z - perp.z),
                new Vector3(bL.x - perp.x, 0f, bL.z - perp.z),
                new Vector3(bL.x + perp.x, 0f, bL.z + perp.z)
            };

            var tris = new[] { 0, 1, 3, 1, 2, 3 };

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        public static void CreateSegmentEntity(EntityManager em, Mesh mesh, Material material, System.Type tagType, float3 worldPos)
        {
            var array = new RenderMeshArray(new[] { material }, new[] { mesh });
            var desc  = new RenderMeshDescription(UnityEngine.Rendering.ShadowCastingMode.Off, false);

            var entity = em.CreateEntity();

            RenderMeshUtility.AddComponents(entity, em, desc, array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

            em.AddComponentData(entity, LocalTransform.FromPosition(worldPos));
            em.AddComponent(entity, ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(tagType)));
        }
    }
}
