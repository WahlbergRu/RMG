// Assets\VoronoiMapGen\Systems\Rendering\BorderMeshBuilder.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Rendering;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class BorderMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            if (!settings.DrawBorders) return;
            Debug.Log("[Borders] Starting biome border generation...");

            // 1. Собираем все ячейки с биомами
            var biomeCells = GetBiomeCells(em);
            if (biomeCells.Length == 0)
            {
                Debug.LogWarning("[Borders] No biome cells found for border generation");
                return;
            }
            Debug.Log($"[Borders] Found {biomeCells.Length} biome cells");

            // 2. Находим границы между разными биомами
            var borderSegments = BuildBiomeBorders(em, biomeCells);
            biomeCells.Dispose(); // Освобождаем массив после использования

            // 3. Создаем меш для границ
            CreateBorderMesh(em, borderSegments, material, settings);
            borderSegments.Dispose(); // Освобождаем список сегментов

            Debug.Log("[Borders] Border generation completed");
        }

        private static NativeArray<Entity> GetBiomeCells(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<CellBiome>()
            );
            // Используем ToEntityArray, результат нужно освободить
            return query.ToEntityArray(Allocator.TempJob);
        }

        // --- ИСПРАВЛЕНИЕ УТЕЧЕК И ЛОГИКИ ---
        private static NativeList<BorderSegmentData> BuildBiomeBorders(EntityManager em, NativeArray<Entity> biomeCells)
        {
            var segments = new NativeList<BorderSegmentData>(Allocator.TempJob); // Используем NativeList для эффективности

            // Получаем все ребра Вороного
            EntityQuery edgeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            using var allEdges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.TempJob); // Правильно получаем компоненты

            // Создаем lookup-таблицу: SiteIndex -> Entity
            var siteToEntityMap = new NativeHashMap<int, Entity>(biomeCells.Length, Allocator.TempJob);
            foreach (var cellEntity in biomeCells)
            {
                var cell = em.GetComponentData<VoronoiCell>(cellEntity);
                siteToEntityMap[cell.SiteIndex] = cellEntity;
            }

            foreach (var edge in allEdges) // edge - это компонент VoronoiEdge
            {
                // Проверяем, есть ли обе ячейки в нашем списке биомных ячеек
                if (siteToEntityMap.TryGetValue(edge.SiteA, out Entity cellAEntity) &&
                    siteToEntityMap.TryGetValue(edge.SiteB, out Entity cellBEntity))
                {
                    var biomeA = em.GetComponentData<CellBiome>(cellAEntity);
                    var biomeB = em.GetComponentData<CellBiome>(cellBEntity);

                    if (biomeA.Type != biomeB.Type) // Граница между разными биомами
                    {
                        var cellA = em.GetComponentData<VoronoiCell>(cellAEntity);
                        var cellB = em.GetComponentData<VoronoiCell>(cellBEntity);

                        segments.Add(new BorderSegmentData
                        {
                            PositionA = new float3(cellA.Centroid.x, 0, cellA.Centroid.y), // Высоту можно уточнить
                            PositionB = new float3(cellB.Centroid.x, 0, cellB.Centroid.y),
                            BiomeA = biomeA.Type,
                            BiomeB = biomeB.Type
                        });
                    }
                }
            }

            siteToEntityMap.Dispose(); // Освобождаем lookup-таблицу
            return segments; // Возвращаем список, вызывающая функция должна освободить
        }
        // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

        private static void CreateBorderMesh(EntityManager em, NativeList<BorderSegmentData> segments, Material material, MapSettings settings)
        {
            if (segments.Length == 0) return;

            // Простой подход: создаем линии для каждого сегмента
            // Для визуализации в Unity можно использовать LineRenderer или создать тонкие меш-цилиндры
            // Здесь создадим сущности с RenderMeshUnmanaged для каждого сегмента
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                // Пример создания тонкого цилиндра для границы
                Mesh lineMesh = CreateLineMesh(segment.PositionA, segment.PositionB, 0.1f); // 0.1f - толщина линии

                Entity meshEntity = em.CreateEntity();
                UnityObjectRef<Mesh> meshRef = lineMesh;
                UnityObjectRef<Material> materialRef = material;
                em.AddComponentData(meshEntity, new RenderMeshUnmanaged(
                    mesh: meshRef,
                    materialForSubMesh: materialRef,
                    subMeshIndex: 0
                ));
                em.AddComponentData(meshEntity, new LocalToWorld { Value = float4x4.identity });
                em.AddComponentData(meshEntity, new WorldRenderBounds { Value = new AABB { Center = (segment.PositionA + segment.PositionB) * 0.5f, Extents = math.abs(segment.PositionB - segment.PositionA) * 0.5f + new float3(1, 1, 1) } });

                //Debug.Log($"[Borders] Created border segment from {segment.PositionA} to {segment.PositionB}");
            }
        }

        private static Mesh CreateLineMesh(float3 start, float3 end, float thickness)
        {
            Mesh mesh = new Mesh();
            float3 direction = end - start;
            float length = math.length(direction);
            float3 center = (start + end) * 0.5f;
            float3 axis = math.normalize(direction);

            // Простая реализация цилиндра
            int segments = 8;
            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            int[] triangles = new int[segments * 6];

            float radius = thickness / 2.0f;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * math.PI * 2;
                float3 offset = new float3(math.cos(angle) * radius, 0, math.sin(angle) * radius);
                // Поворачиваем offset вдоль оси направления
                float3x3 rotation = float3x3.identity;
                if (math.abs(axis.y) < 0.99f) // Не параллельна оси Y
                {
                    float3 up = math.normalize(math.cross(axis, math.up()));
                    float3 side = math.normalize(math.cross(up, axis));
                    rotation = new float3x3(side, axis, up);
                }
                else
                {
                    float3 up = math.up();
                    float3 side = math.normalize(math.cross(up, axis));
                    float3 forward = math.normalize(math.cross(side, up));
                    rotation = new float3x3(side, forward, up);
                }
                offset = math.mul(rotation, offset);

                vertices[i * 2] = center - direction * 0.5f + offset;
                vertices[i * 2 + 1] = center + direction * 0.5f + offset;
            }

            int triIndex = 0;
            for (int i = 0; i < segments; i++)
            {
                int currentStart = i * 2;
                int nextStart = ((i + 1) % segments) * 2;

                // Лицевая сторона
                triangles[triIndex++] = currentStart;
                triangles[triIndex++] = nextStart;
                triangles[triIndex++] = currentStart + 1;

                triangles[triIndex++] = currentStart + 1;
                triangles[triIndex++] = nextStart;
                triangles[triIndex++] = nextStart + 1;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public struct BorderSegmentData
    {
        public float3 PositionA;
        public float3 PositionB;
        public BiomeType BiomeA;
        public BiomeType BiomeB;
    }
}