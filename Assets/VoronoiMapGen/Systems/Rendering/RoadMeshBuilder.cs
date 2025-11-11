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

            EntityQuery edgeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            EntityQuery cellQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>());

            using NativeArray<VoronoiEdge> edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            using NativeArray<VoronoiCell> cells = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);

            HashSet<(int, int)> processed = new HashSet<(int, int)>(new EdgeComparer());

            foreach (VoronoiEdge edge in edges)
            {
                (int, int) key = MeshUtils.EdgeKey(edge.SiteA, edge.SiteB);
                if (!processed.Add(key)) continue;

                VoronoiCell? cellA = FindCell(cells, edge.SiteA);
                VoronoiCell? cellB = FindCell(cells, edge.SiteB);
                if (!cellA.HasValue || !cellB.HasValue) continue;

                float2 a = cellA.Value.Centroid;
                float2 b = cellB.Value.Centroid;

                float3 center = new float3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

                Mesh mesh = MeshUtils.CreateQuadMeshLocal(a, b, center, settings.RoadWidth, "RoadSegment");
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