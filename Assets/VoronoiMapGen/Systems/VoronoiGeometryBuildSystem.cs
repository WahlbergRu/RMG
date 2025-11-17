using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using System.Collections.Generic;
using Random = Unity.Mathematics.Random; // Добавлено для IComparer<>

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
        // public void OnCreate(ref SystemState state)
        // {
        //     state.RequireForUpdate<MapGeneratedTag>();
        //     state.RequireForUpdate<VoronoiCell>();
        //     state.RequireForUpdate<VoronoiEdge>();
        // }
        //
        // [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Выполняется только один раз после полной генерации
            if (SystemAPI.HasSingleton<GeometryBuiltTag>() || !SystemAPI.HasSingleton<MapGeneratedTag>())
                return;
                
            MapSettings mapSettings = SystemAPI.GetSingleton<MapSettings>();
            EntityQuery edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            using NativeArray<VoronoiEdge> edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            if (edges.Length == 0) return;

            EntityQuery cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            using NativeArray<Entity> cells = cellQuery.ToEntityArray(Allocator.Temp);
            if (cells.Length == 0) return;

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            Random random = new Unity.Mathematics.Random((uint)mapSettings.Seed); // Явное указание namespace

            for (int i = 0; i < cells.Length; i++)
            {
                Entity entity = cells[i];
                VoronoiCell cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);
                ProcessCell(entity, cell, edges, ecb, ref state);
                // ecb.AddComponent<VoronoiCellMeshTag>(entity);
            }
            
            if (SystemAPI.TryGetSingletonEntity<MapSettings>(out Entity settingsEntity))
            {
                ecb.AddComponent<GeometryBuiltTag>(settingsEntity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private static void ProcessCell(Entity entity, VoronoiCell cell, NativeArray<VoronoiEdge> allEdges,
                               EntityCommandBuffer ecb, ref SystemState state)
        {
            // Подготовка/получение буферов: если есть — взять через EntityManager, иначе добавить через ECB и использовать возвращаемый буфер
            DynamicBuffer<CellPolygonVertex> vertsBuf;
            DynamicBuffer<CellTriIndex> triBuf;

            if (state.EntityManager.HasBuffer<CellPolygonVertex>(entity))
            {
                vertsBuf = state.EntityManager.GetBuffer<CellPolygonVertex>(entity);
                vertsBuf.Clear();
            }
            else
            {
                vertsBuf = ecb.AddBuffer<CellPolygonVertex>(entity);
            }

            if (state.EntityManager.HasBuffer<CellTriIndex>(entity))
            {
                triBuf = state.EntityManager.GetBuffer<CellTriIndex>(entity);
                triBuf.Clear();
            }
            else
            {
                triBuf = ecb.AddBuffer<CellTriIndex>(entity);
            }

            // Собираем уникальные вершины во временные структуры
            NativeHashSet<ulong> unique = new NativeHashSet<ulong>(32, Allocator.Temp);
            NativeList<float2> verts = new NativeList<float2>(32, Allocator.Temp);

            int siteIndex = cell.SiteIndex;
            for (int i = 0; i < allEdges.Length; i++)
            {
                VoronoiEdge edge = allEdges[i];
                if (edge.SiteA == siteIndex || edge.SiteB == siteIndex)
                {
                    AddUniqueVertex(edge.VertexA, ref unique, ref verts);
                    AddUniqueVertex(edge.VertexB, ref unique, ref verts);
                }
            }

            // Если вершин < 3 — подстраховка
            if (verts.Length < 3)
            {
                CreateFallbackGeometry(ref verts, cell.Centroid);
            }

            // Сортируем перед записью в vertsBuf!
            SortVerticesClockwise(ref verts, cell.Centroid);

            // Записываем вершины с высотой (после сортировки)
            for (int v = 0; v < verts.Length; v++)
            {
                vertsBuf.Add(new CellPolygonVertex
                {
                    Value = new float3(verts[v].x, 0f, verts[v].y)
                });
            }

            // Создаём фан-триангуляцию в формате, ожидаемом MeshUpdateSystem: для каждого треугольника — ПАРА индексов (i, i+1).
            CreateFanTriangulation(verts.Length, triBuf);

            // Помечаем меш как грязный
            if (!state.EntityManager.HasComponent<CellDirtyFlag>(entity))
                ecb.AddComponent<CellDirtyFlag>(entity);

            unique.Dispose();
            verts.Dispose();
        }


        private static void AddUniqueVertex(float2 vertex, ref NativeHashSet<ulong> unique, ref NativeList<float2> verts)
        {
            ulong hash = HashFloat2(vertex);
            if (unique.Add(hash))
            {
                verts.Add(vertex);
            }
        }

        private static void CreateFallbackGeometry(ref NativeList<float2> verts, float2 centroid)
        {
            verts.Clear();
            float radius = 2.0f;
            int segments = 8;
            
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * 2 * math.PI;
                verts.Add(centroid + new float2(math.cos(angle), math.sin(angle)) * radius);
            }
        }

        private static void SortVerticesClockwise(ref NativeList<float2> verts, float2 center)
        {
            verts.Sort(new ClockwiseComparer(center));
        }

        private static void CreateFanTriangulation(int vertexCount, DynamicBuffer<CellTriIndex> triBuf)
        {
            Debug.Log(vertexCount);
            if (vertexCount >= 3)
            {
                // Для фан-триангуляции мы хотим, чтобы каждый треугольник был (0, i, i+1),
                // но в буфере сохраняем только пары (i, i+1) — MeshUpdate вставит центр (0).
                for (int i = 1; i < vertexCount - 1; i++)
                {
                    triBuf.Add(new CellTriIndex { Value = i - 1 });
                    triBuf.Add(new CellTriIndex { Value = i });
                    triBuf.Add(new CellTriIndex { Value = i + 1 });
                }
            }
        }

        // Улучшенный сэмплинг высоты с учётом уровня детализации и биомов
        private static float SampleCellHeight(float2 position, VoronoiCell cell, Unity.Mathematics.Random random, float2 mapSize) // Явное указание namespace
        {
            // Нормализуем позицию для шума
            float2 normalizedPos = position / mapSize;
            
            // Базовый перлиновый шум с несколькими октавами
            float baseHeight = 0.0f;
            float amplitude = 1.0f;
            float frequency = 1.0f;
            
            for (int octave = 0; octave < 4; octave++)
            {
                float2 samplePos = normalizedPos * frequency * 10.0f;
                float noiseValue = noise.snoise(samplePos + new float2(random.NextFloat(), random.NextFloat()));
                baseHeight += noiseValue * amplitude;
                
                amplitude *= 0.5f;
                frequency *= 2.0f;
            }
            
            // Нормализуем в диапазон [0, 1]
            baseHeight = math.saturate((baseHeight + 1.0f) * 0.5f);
            
            // Учитываем уровень детализации - чем выше уровень, тем детальнее рельеф
            float detailFactor = math.clamp(cell.Level / 6.0f, 0.1f, 1.0f);
            float heightVariation = random.NextFloat(0.2f, 0.8f) * detailFactor;
            
            // Финальная высота с учётом "ценности" ячейки
            float finalHeight = baseHeight * 100.0f + (cell.Value * 50.0f) + (heightVariation * 30.0f);
            
            // Уровень моря - опускаем низкие области
            if (finalHeight < 20.0f && cell.Level == 1) // Только для регионального уровня
            {
                finalHeight = math.lerp(0.0f, finalHeight, 0.3f); // Плавный переход к воде
            }
            
            return finalHeight;
        }
        
        // Хелперы
        private static ulong HashFloat2(float2 v)
        {
            return ((ulong)math.asuint(v.x) << 32) | math.asuint(v.y);
        }

        private struct ClockwiseComparer : IComparer<float2> // Теперь IComparer<> найден благодаря using System.Collections.Generic
        {
            private readonly float2 _center;

            public ClockwiseComparer(float2 center) => _center = center;

            public int Compare(float2 a, float2 b)
            {
                float2 da = a - _center;
                float2 db = b - _center;
                float angleA = math.atan2(da.y, da.x);
                float angleB = math.atan2(db.y, db.x);
                return angleB.CompareTo(angleA); // reverse for clockwise
            }
        }
    }
}