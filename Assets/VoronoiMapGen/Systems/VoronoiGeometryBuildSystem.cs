using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Строит уникальные вершины для каждой ячейки, сортирует CW,
    /// пишет в буферы CellPolygonVertex + фан-триангуляцию в CellTriIndex, ставит CellDirtyFlag.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Выполняется только один раз
            if (!SystemAPI.HasSingleton<MapGeneratedTag>() || SystemAPI.HasSingleton<GeometryBuiltTag>())
                return;

            var edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            if (edges.Length == 0) return;

            var cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            using var cells = cellQuery.ToEntityArray(Allocator.Temp);
            if (cells.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < cells.Length; i++)
            {
                var entity = cells[i];
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);
                ProcessCell(entity, cell, edges, ecb, ref state);
                ecb.AddComponent<VoronoiMeshGeneratedTag>(entity);
            }
            
            var GeometryBuiltEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(GeometryBuiltEntity, new GeometryBuiltTag());


            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private static void ProcessCell(Entity entity, VoronoiCell cell, NativeArray<VoronoiEdge> allEdges, EntityCommandBuffer ecb, ref SystemState state)
        {
            if (!state.EntityManager.HasBuffer<CellPolygonVertex>(entity))
            {
                ecb.AddBuffer<CellPolygonVertex>(entity);
            }
            
            if (!state.EntityManager.HasBuffer<CellTriIndex>(entity))
            {
                ecb.AddBuffer<CellTriIndex>(entity);
            }

            var vertsBuf = ecb.AddBuffer<CellPolygonVertex>(entity);
            var triBuf = ecb.AddBuffer<CellTriIndex>(entity);

            var unique = new NativeHashSet<ulong>(16, Allocator.Temp);
            var verts = new NativeList<float2>(16, Allocator.Temp);

            int siteIndex = cell.SiteIndex;
            
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == siteIndex || edge.SiteB == siteIndex)
                {
                    var hA = HashFloat2(edge.VertexA);
                    if (unique.Add(hA)) verts.Add(edge.VertexA);

                    var hB = HashFloat2(edge.VertexB);
                    if (unique.Add(hB)) verts.Add(edge.VertexB);
                }
            }

            if (verts.Length < 3)
            {
                verts.Clear();
                verts.Add(new float2(-0.25f, -0.25f));
                verts.Add(new float2( 0.25f, -0.25f));
                verts.Add(new float2( 0.25f,  0.25f));
                verts.Add(new float2(-0.25f,  0.25f));
            }

            // Сортировка по часовой стрелке вокруг центроида
            verts.Sort(new ClockwiseComparer(cell.Centroid));

            // Записываем вершины
            for (int i = 0; i < verts.Length; i++)
            {
                vertsBuf.Add(new CellPolygonVertex { Value = verts[i] });
            }

            // Fan triangulation: треугольники (0, i, i+1)
            if (verts.Length >= 3)
            {
                for (int i = 1; i < verts.Length - 1; i++)
                {
                    triBuf.Add(new CellTriIndex { Value = 0 });
                    triBuf.Add(new CellTriIndex { Value = i });
                    triBuf.Add(new CellTriIndex { Value = i + 1 });
                }
            }

            // Помечаем, что меш нужно обновить
            ecb.AddComponent<CellDirtyFlag>(entity);

            unique.Dispose();
            verts.Dispose();
        }

        // Хелперы
        private static ulong HashFloat2(float2 v)
        {
            return ((ulong)math.asuint(v.x) << 32) | math.asuint(v.y);
        }

        private struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;

            public ClockwiseComparer(float2 center) => _center = center;

            public int Compare(float2 a, float2 b)
            {
                var da = a - _center;
                var db = b - _center;
                var angleA = math.atan2(da.y, da.x);
                var angleB = math.atan2(db.y, db.x);
                return angleB.CompareTo(angleA); // reverse for clockwise
            }
        }
    }
}