using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using System.Collections.Generic; // Добавлено для IComparer<>

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Строит уникальные вершины для каждой ячейки, сортирует CW,
    /// пишет в буферы CellPolygonVertex + фан-триангуляцию в CellTriIndex, ставит CellDirtyFlag.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))] 
    [UpdateAfter(typeof(MapGenerationSystem))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MapGeneratedTag>();
            state.RequireForUpdate<VoronoiCell>();
            state.RequireForUpdate<VoronoiEdge>();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Выполняется только один раз после полной генерации
            if (SystemAPI.HasSingleton<GeometryBuiltTag>() || !SystemAPI.HasSingleton<MapGeneratedTag>())
                return;
                
            var mapSettings = SystemAPI.GetSingleton<MapSettings>();
            var edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            if (edges.Length == 0) return;

            var cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            using var cells = cellQuery.ToEntityArray(Allocator.Temp);
            if (cells.Length == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var random = new Unity.Mathematics.Random((uint)mapSettings.Seed); // Явное указание namespace

            for (int i = 0; i < cells.Length; i++)
            {
                var entity = cells[i];
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);
                ProcessCell(entity, cell, edges, ecb, ref state, random, mapSettings.MapSize);
                ecb.AddComponent<VoronoiCellMeshTag>(entity); // Используем существующий тег
            }
            
            if (SystemAPI.TryGetSingletonEntity<MapSettings>(out var settingsEntity))
            {
                ecb.AddComponent<GeometryBuiltTag>(settingsEntity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private static void ProcessCell(Entity entity, VoronoiCell cell, NativeArray<VoronoiEdge> allEdges, 
                                       EntityCommandBuffer ecb, ref SystemState state, Unity.Mathematics.Random random, float2 mapSize) // Явное указание namespace
        {
            // Инициализируем буферы если их нет
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

            var unique = new NativeHashSet<ulong>(32, Allocator.Temp);
            var verts = new NativeList<float2>(32, Allocator.Temp);

            int siteIndex = cell.SiteIndex;
            
            // Собираем все вершины, принадлежащие этой ячейке
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == siteIndex || edge.SiteB == siteIndex)
                {
                    AddUniqueVertex(edge.VertexA, ref unique, ref verts);
                    AddUniqueVertex(edge.VertexB, ref unique, ref verts);
                }
            }

            // Если вершин меньше 3 - создаём заглушку (должно быть редко)
            if (verts.Length < 3)
            {
                Debug.LogWarning($"Cell at {cell.Centroid} has only {verts.Length} vertices. Creating fallback geometry.");
                CreateFallbackGeometry(ref verts, cell.Centroid);
            }

            // Сортировка по часовой стрелке вокруг центроида
            SortVerticesClockwise(ref verts, cell.Centroid);

            // Записываем вершины с высотой
            for (int i = 0; i < verts.Length; i++)
            {
                float height = SampleCellHeight(verts[i], cell, random, mapSize);
                vertsBuf.Add(new CellPolygonVertex { 
                    Value = new float3(verts[i].x, height, verts[i].y) 
                });
            }

            // Fan triangulation: треугольники (0, i, i+1)
            CreateFanTriangulation(verts.Length, triBuf);

            // Помечаем, что меш нужно обновить
            ecb.AddComponent<CellDirtyFlag>(entity);

            unique.Dispose();
            verts.Dispose();
        }

        private static void AddUniqueVertex(float2 vertex, ref NativeHashSet<ulong> unique, ref NativeList<float2> verts)
        {
            var hash = HashFloat2(vertex);
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
            if (vertexCount >= 3)
            {
                for (int i = 1; i < vertexCount - 1; i++)
                {
                    triBuf.Add(new CellTriIndex { Value = 0 });
                    triBuf.Add(new CellTriIndex { Value = i });
                    triBuf.Add(new CellTriIndex { Value = i + 1 });
                }
            }
            else
            {
                // Минимальный треугольник для отладки
                triBuf.Add(new CellTriIndex { Value = 0 });
                triBuf.Add(new CellTriIndex { Value = 1 });
                triBuf.Add(new CellTriIndex { Value = 2 });
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
                var da = a - _center;
                var db = b - _center;
                var angleA = math.atan2(da.y, da.x);
                var angleB = math.atan2(db.y, db.x);
                return angleB.CompareTo(angleA); // reverse for clockwise
            }
        }
    }
}