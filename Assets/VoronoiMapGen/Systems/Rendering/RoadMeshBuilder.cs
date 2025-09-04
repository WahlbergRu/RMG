using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Rendering
{
    public static class RoadMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            if (!settings.DrawRoads) return;

            var edgeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            var cellQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>());

            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            using var cells = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);

            var processed = new HashSet<(int, int)>(new EdgeComparer());

            foreach (var edge in edges)
            {
                var key = MeshUtils.EdgeKey(edge.SiteA, edge.SiteB);
                if (!processed.Add(key)) continue;

                var cellA = FindCell(cells, edge.SiteA);
                var cellB = FindCell(cells, edge.SiteB);
                if (!cellA.HasValue || !cellB.HasValue) continue;

                float2 a = cellA.Value.Centroid;
                float2 b = cellB.Value.Centroid;

                float3 center = new float3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

                var mesh = MeshUtils.CreateQuadMeshLocal(a, b, center, settings.RoadWidth, "RoadSegment");
                MeshUtils.CreateSegmentEntity(em, mesh, material, typeof(RoadEntityTag), center);
            }
        }

        private static VoronoiCell? FindCell(NativeArray<VoronoiCell> cells, int siteIndex)
        {
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].SiteIndex == siteIndex) return cells[i];
            return null;
        }
    }
}