using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Rendering
{
    public static class BorderMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            if (!settings.DrawBorders) return;

            EntityQuery edgeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            using NativeArray<VoronoiEdge> edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);

            HashSet<(int, int)> processed = new HashSet<(int, int)>(new EdgeComparer());

            foreach (VoronoiEdge edge in edges)
            {
                (int, int) key = MeshUtils.EdgeKey(edge.SiteA, edge.SiteB);
                if (!processed.Add(key)) continue;

                float2 vA = edge.VertexA;
                float2 vB = edge.VertexB;

                float3 center = new float3((vA.x + vB.x) * 0.5f, 0f, (vA.y + vB.y) * 0.5f);

                Mesh mesh = MeshUtils.CreateQuadMeshLocal(vA, vB, center, settings.EdgeWidth, "BorderSegment");
                if (material != null) material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 10;

                MeshUtils.CreateSegmentEntity(em, mesh, material, typeof(BorderEntityTag), center);
            }
        }
    }
}