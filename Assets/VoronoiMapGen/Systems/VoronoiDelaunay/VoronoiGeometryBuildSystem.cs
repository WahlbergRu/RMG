using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>() || SystemAPI.HasSingleton<GeometryBuiltTag>())
                return;

            MapSettings mapSettings = new MapSettings { EdgeWidth = 0, MapSize = new float2(1000, 1000) };
            if (SystemAPI.TryGetSingleton(out MapSettings settings)) mapSettings = settings;

            var edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            var cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            var cells = cellQuery.ToEntityArray(Allocator.Temp);

            if (edges.Length == 0 || cells.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < cells.Length; i++)
            {
                var entity = cells[i];
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);

                // Фильтр призраков
                if (cell.Centroid.x < -10 || cell.Centroid.y < -10 || 
                    cell.Centroid.x > mapSettings.MapSize.x + 10 || 
                    cell.Centroid.y > mapSettings.MapSize.y + 10)
                    continue;

                ProcessCell(entity, cell, edges, mapSettings, ecb, ref state);
                ecb.AddComponent<VoronoiMeshGeneratedTag>(entity);
            }
            
            var builtEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(builtEntity, new GeometryBuiltTag());

            ecb.Playback(state.EntityManager);
            edges.Dispose();
            cells.Dispose();
            ecb.Dispose();
        }

        private static void ProcessCell(Entity entity, VoronoiCell cell, NativeArray<VoronoiEdge> allEdges, MapSettings settings, EntityCommandBuffer ecb, ref SystemState state)
        {
            if (!state.EntityManager.HasBuffer<CellPolygonVertex>(entity)) ecb.AddBuffer<CellPolygonVertex>(entity);
            if (!state.EntityManager.HasBuffer<CellTriIndex>(entity)) ecb.AddBuffer<CellTriIndex>(entity);

            var vertsBuf = ecb.AddBuffer<CellPolygonVertex>(entity);
            var triBuf = ecb.AddBuffer<CellTriIndex>(entity);
            var rawVerts = new NativeList<float2>(16, Allocator.Temp);
            var unique = new NativeHashSet<ulong>(16, Allocator.Temp);
            
            int siteIndex = cell.SiteIndex;
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == siteIndex || edge.SiteB == siteIndex)
                {
                    // Используем новый HashFloat2 с округлением!
                    if (unique.Add(HashFloat2(edge.VertexA))) rawVerts.Add(edge.VertexA);
                    if (unique.Add(HashFloat2(edge.VertexB))) rawVerts.Add(edge.VertexB);
                }
            }
            unique.Dispose();

            if (rawVerts.Length < 3) { rawVerts.Dispose(); return; }

            rawVerts.Sort(new ClockwiseComparer(cell.Centroid));

            var clippedVerts = ClipPolygonToMapBounds(rawVerts, settings.MapSize);
            float inset = settings.EdgeWidth * 0.5f;
            
            // === СМЕЩЕНИЕ ПО ВЫСОТЕ ===
            // Каждый уровень поднимаем на 0.2, чтобы L1 был визуально НАД L0
            float heightOffset = cell.Level * 0.2f;

            for (int i = 0; i < clippedVerts.Length; i++)
            {
                float2 v = clippedVerts[i];
                if (inset > 0.001f)
                {
                    float2 dir = v - cell.Centroid;
                    float dist = math.length(dir);
                    if (dist > inset + 0.001f) v = cell.Centroid + (dir / dist) * (dist - inset);
                }
                
                // Записываем высоту Y = heightOffset
                vertsBuf.Add(new CellPolygonVertex { Value = new float3(v.x, heightOffset, v.y) });
            }

            if (clippedVerts.Length >= 3)
            {
                for (int i = 1; i < clippedVerts.Length - 1; i++)
                {
                    triBuf.Add(new CellTriIndex { Value = 0 });
                    triBuf.Add(new CellTriIndex { Value = i });
                    triBuf.Add(new CellTriIndex { Value = i + 1 });
                }
            }

            ecb.AddComponent<CellDirtyFlag>(entity);
            rawVerts.Dispose();
            clippedVerts.Dispose();
        }

        // === ИСПРАВЛЕННЫЙ ХЕШЕР (ОКРУГЛЕНИЕ) ===
        // Это уберет "взрывы" геометрии, склеивая точки, которые ближе 1 см друг к другу.
        private static ulong HashFloat2(float2 v)
        {
            // Умножаем на 100 (точность до 0.01) и приводим к int
            int x = (int)math.round(v.x * 100f);
            int y = (int)math.round(v.y * 100f);
            return ((ulong)(uint)x << 32) | (uint)y;
        }
        
        // ... Остальные методы (ClipPolygonToMapBounds, Comparer) оставляем как были ...
        
        private static NativeList<float2> ClipPolygonToMapBounds(NativeList<float2> polygon, float2 mapSize)
        {
            var outputList = new NativeList<float2>(polygon.Length + 8, Allocator.Temp);
            outputList.AddRange(polygon.AsArray()); // Fix for warning
            
            var inputList = new NativeList<float2>(polygon.Length + 8, Allocator.Temp);

            ClipEdge(outputList, inputList, new float2(1, 0), 0); Swap(ref outputList, ref inputList);
            ClipEdge(outputList, inputList, new float2(-1, 0), -mapSize.x); Swap(ref outputList, ref inputList);
            ClipEdge(outputList, inputList, new float2(0, 1), 0); Swap(ref outputList, ref inputList);
            ClipEdge(outputList, inputList, new float2(0, -1), -mapSize.y); Swap(ref outputList, ref inputList);

            inputList.Dispose();
            return outputList; 
        }
        private static void Swap(ref NativeList<float2> a, ref NativeList<float2> b) { var temp = a; a = b; b = temp; }
        private static void ClipEdge(NativeList<float2> input, NativeList<float2> output, float2 normal, float clipDist)
        {
            output.Clear();
            if (input.Length == 0) return;
            float2 prevPoint = input[input.Length - 1];
            bool prevInside = (math.dot(prevPoint, normal) >= clipDist);
            for (int i = 0; i < input.Length; i++) {
                float2 currPoint = input[i];
                bool currInside = (math.dot(currPoint, normal) >= clipDist);
                if (currInside) {
                    if (!prevInside) output.Add(Intersect(prevPoint, currPoint, normal, clipDist));
                    output.Add(currPoint);
                } else if (prevInside) {
                    output.Add(Intersect(prevPoint, currPoint, normal, clipDist));
                }
                prevPoint = currPoint; prevInside = currInside;
            }
        }
        private static float2 Intersect(float2 p1, float2 p2, float2 normal, float clipDist)
        {
            float d1 = math.dot(p1, normal) - clipDist;
            float d2 = math.dot(p2, normal) - clipDist;
            float t = d1 / (d1 - d2);
            return math.lerp(p1, p2, t);
        }
        private struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;
            public ClockwiseComparer(float2 center) => _center = center;
            public int Compare(float2 a, float2 b) => math.atan2(b.y - _center.y, b.x - _center.x).CompareTo(math.atan2(a.y - _center.y, a.x - _center.x));
        }
    }
}