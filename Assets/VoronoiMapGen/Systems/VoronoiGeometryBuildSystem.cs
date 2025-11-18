using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using System.Collections.Generic;
using Random = Unity.Mathematics.Random;

namespace VoronoiMapGen.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<GeometryBuiltTag>() || !SystemAPI.HasSingleton<MapGeneratedTag>())
                return;

            MapSettings mapSettings = SystemAPI.GetSingleton<MapSettings>();
            EntityQuery edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            using NativeArray<VoronoiEdge> edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            if (edges.Length == 0) return;

            EntityQuery cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            using NativeArray<Entity> cells = cellQuery.ToEntityArray(Allocator.Temp);
            if (cells.Length == 0) return;

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);
            Random random = new Unity.Mathematics.Random((uint)mapSettings.Seed);

            for (int i = 0; i < cells.Length; i++)
            {
                Entity entity = cells[i];
                VoronoiCell cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);
                ProcessCell(entity, cell, edges, ecb, ref state, mapSettings.MapSize);
            }

            if (SystemAPI.TryGetSingletonEntity<MapSettings>(out Entity settingsEntity))
            {
                ecb.AddComponent<GeometryBuiltTag>(settingsEntity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private static void ProcessCell(Entity entity, VoronoiCell cell, NativeArray<VoronoiEdge> allEdges,
                               EntityCommandBuffer ecb, ref SystemState state, float2 mapSize)
        {
            DynamicBuffer<CellPolygonVertex> vertsBuf = GetOrCreateBuffer(state.EntityManager, ecb, entity, new DynamicBuffer<CellPolygonVertex>());
            DynamicBuffer<CellTriIndex> triBuf = GetOrCreateBuffer(state.EntityManager, ecb, entity, new DynamicBuffer<CellTriIndex>());
            
            // Создаем временный список для вершин периметра
            NativeList<float2> perimeterVertices = new NativeList<float2>(32, Allocator.TempJob);
            try
            {
                // Собираем вершины периметра из ребер
                CollectPerimeterVertices(cell, allEdges, ref perimeterVertices, mapSize);
                
                // Фильтруем некорректные вершины
                FilterInvalidVertices(ref perimeterVertices, mapSize);
                
                // Обеспечиваем минимальное количество вершин
                EnsureValidGeometry(ref perimeterVertices, cell.Centroid);
                
                // Сортируем вершины по часовой стрелке
                SortVerticesClockwise(ref perimeterVertices, cell.Centroid);
                
                // Записываем вершины с центральной точкой в начале
                WriteVerticesToBuffer(vertsBuf, perimeterVertices, cell.Centroid);
                
                // Создаем корректную триангуляцию
                CreateProperTriangulation(perimeterVertices.Length, triBuf);
            }
            finally
            {
                perimeterVertices.Dispose();
            }
            
            MarkCellDirty(entity, ecb, ref state);
        }

        private static void CollectPerimeterVertices(VoronoiCell cell, NativeArray<VoronoiEdge> allEdges, 
            ref NativeList<float2> vertices, float2 mapSize)
        {
            NativeHashSet<ulong> uniqueHashes = new NativeHashSet<ulong>(32, Allocator.TempJob);
            
            int siteIndex = cell.SiteIndex;
            for (int i = 0; i < allEdges.Length; i++)
            {
                VoronoiEdge edge = allEdges[i];
                if (edge.SiteA == siteIndex || edge.SiteB == siteIndex)
                {
                    AddValidVertex(edge.VertexA, ref uniqueHashes, ref vertices, mapSize);
                    AddValidVertex(edge.VertexB, ref uniqueHashes, ref vertices, mapSize);
                }
            }
            
            uniqueHashes.Dispose();
        }

        private static void AddValidVertex(float2 vertex, ref NativeHashSet<ulong> unique, ref NativeList<float2> verts, float2 mapSize)
        {
            // Отбрасываем нулевые вершины
            if (math.lengthsq(vertex) < 0.01f)
                return;
                
            // Обрезаем вершины по границам карты
            vertex = ClampToMapBounds(vertex, mapSize);
            
            ulong hash = HashFloat2(vertex);
            if (unique.Add(hash))
            {
                verts.Add(vertex);
            }
        }

        private static float2 ClampToMapBounds(float2 vertex, float2 mapSize)
        {
            float padding = 10.0f;
            vertex.x = math.clamp(vertex.x, -padding, mapSize.x + padding);
            vertex.y = math.clamp(vertex.y, -padding, mapSize.y + padding);
            return vertex;
        }

        private static void FilterInvalidVertices(ref NativeList<float2> verts, float2 mapSize)
        {
            // Создаем новый список для валидных вершин
            NativeList<float2> validVertices = new NativeList<float2>(verts.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    float2 v = verts[i];
                    
                    // Отбрасываем нулевые вершины
                    if (math.lengthsq(v) < 0.01f)
                        continue;
                        
                    // Отбрасываем вершины, слишком сильно вышедшие за границы
                    if (v.x < -mapSize.x * 2 || v.x > mapSize.x * 2 || 
                        v.y < -mapSize.y * 2 || v.y > mapSize.y * 2)
                        continue;
                        
                    validVertices.Add(v);
                }
                
                // Копируем валидные вершины обратно
                verts.Clear();
                for (int i = 0; i < validVertices.Length; i++)
                {
                    verts.Add(validVertices[i]);
                }
            }
            finally
            {
                validVertices.Dispose();
            }
        }

        private static void EnsureValidGeometry(ref NativeList<float2> verts, float2 centroid)
        {
            if (verts.Length < 3)
            {
                verts.Clear();
                float radius = 5.0f;
                int segments = 8;
                for (int i = 0; i < segments; i++)
                {
                    float angle = (float)i / segments * 2 * math.PI;
                    verts.Add(centroid + new float2(math.cos(angle), math.sin(angle)) * radius);
                }
            }
        }

        private static void SortVerticesClockwise(ref NativeList<float2> verts, float2 center)
        {
            // Создаем временный массив для сортировки БЕЗ using
            NativeArray<float2> array = new NativeArray<float2>(verts.Length, Allocator.TempJob);
            try
            {
                // Копируем данные
                for (int i = 0; i < verts.Length; i++)
                {
                    array[i] = verts[i];
                }

                // Сортируем
                array.Sort(new ClockwiseComparer(center));

                // Копируем назад
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = array[i];
                }
            }
            finally
            {
                array.Dispose(); // Обязательно освобождаем вручную
            }
        }

        private static void WriteVerticesToBuffer(DynamicBuffer<CellPolygonVertex> vertsBuf, NativeList<float2> vertices, float2 centroid)
        {
            vertsBuf.Clear();
            
            // 1. Добавляем центральную точку (сайт)
            vertsBuf.Add(new CellPolygonVertex
            {
                Value = new float3(centroid.x, 0f, centroid.y)
            });
            
            // 2. Добавляем периметральные вершины
            for (int v = 0; v < vertices.Length; v++)
            {
                vertsBuf.Add(new CellPolygonVertex
                {
                    Value = new float3(vertices[v].x, 0f, vertices[v].y)
                });
            }
        }

        private static void CreateProperTriangulation(int vertexCount, DynamicBuffer<CellTriIndex> triBuf)
        {
            triBuf.Clear();
            
            if (vertexCount >= 3)
            {
                // Фан-триангуляция: каждый треугольник состоит из центра (0) и двух последовательных периметральных вершин
                for (int i = 1; i < vertexCount; i++)
                {
                    int next = i + 1;
                    if (next > vertexCount)
                        next = 1;
                    
                    // Треугольник: центр (0), текущая вершина (i), следующая вершина (next)
                    triBuf.Add(new CellTriIndex { Value = 0 });
                    triBuf.Add(new CellTriIndex { Value = i });
                    triBuf.Add(new CellTriIndex { Value = next });
                }
                
                // Замыкаем последний треугольник
                if (vertexCount > 2)
                {
                    triBuf.Add(new CellTriIndex { Value = 0 });
                    triBuf.Add(new CellTriIndex { Value = vertexCount });
                    triBuf.Add(new CellTriIndex { Value = 1 });
                }
            }
        }

        private static DynamicBuffer<T> GetOrCreateBuffer<T>(EntityManager em, EntityCommandBuffer ecb, Entity entity, DynamicBuffer<T> defaultBuffer) 
            where T : unmanaged, IBufferElementData
        {
            if (em.HasBuffer<T>(entity))
            {
                return em.GetBuffer<T>(entity);
            }
            else
            {
                return ecb.AddBuffer<T>(entity);
            }
        }

        private static void MarkCellDirty(Entity entity, EntityCommandBuffer ecb, ref SystemState state)
        {
            if (!state.EntityManager.HasComponent<CellDirtyFlag>(entity))
            {
                ecb.AddComponent<CellDirtyFlag>(entity);
            }
        }

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
                float2 da = a - _center;
                float2 db = b - _center;

                float angleA = math.atan2(da.y, da.x);
                float angleB = math.atan2(db.y, db.x);
                
                return angleA.CompareTo(angleB);
            }
        }
    }
}