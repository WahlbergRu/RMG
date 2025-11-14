// Assets\VoronoiMapGen\Systems\Rendering\WaterMeshBuilder.cs
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
    public static class WaterMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            Debug.Log("[Water] Starting water surface generation...");

            // 1. Собираем водные ячейки
            var waterCells = GetWaterCells(em);
            if (waterCells.Length == 0)
            {
                Debug.LogWarning("[Water] No water cells found");
                return; // waterCells.Length == 0, значит массив не создан, Dispose не нужен
            }
            Debug.Log($"[Water] Found {waterCells.Length} water cells");

            // 2. Объединяем смежные водные ячейки в группы
            var waterGroups = GroupWaterCells(em, waterCells);
            // ВАЖНО: waterCells.Dispose() должен быть вызван после использования
            waterCells.Dispose(); // Исправление утечки

            // 3. Создаем меш для каждой группы
            for (int i = 0; i < waterGroups.Count; i++)
            {
                var group = waterGroups[i];
                CreateWaterMesh(em, group, material, settings, i);
                group.Dispose(); // Освобождаем каждую группу NativeList
            }

            Debug.Log($"[Water] Generated {waterGroups.Count} water meshes");
        }

        private static NativeArray<Entity> GetWaterCells(EntityManager em)
        {
            // Используем временный Query, чтобы не хранить его как поле
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<WaterEntityTag>() // Предполагаем, что у водных ячеек есть этот тег
            );
            // ToEntityArray создает NativeArray, который нужно освободить
            // Используем Allocator.TempJob для лучшей производительности в Job'ах, но т.к. это не Job, используем Allocator.Temp
            // Однако, в контексте SystemBase, часто используется Allocator.TempJob даже в .Run()
            // Но для ясности и совместимости с .Run() используем Allocator.TempJob и освобождаем вручную
            // Но Unity рекомендует использовать 'using' для NativeArray, созданных с ToEntityArray
            // Однако, ToEntityArray не поддерживает 'using' напрямую в C# 7.3 и ниже так же, как другие NativeContainers.
            // Лучший способ - получить массив и вручную вызвать Dispose.
            // Или обернуть в using var, как показано ниже.
            using var tempQuery = query; // Убедимся, что Query освобождается
            return tempQuery.ToEntityArray(Allocator.TempJob); // Этот массив должен быть освобождён вызывающим
            // --- ИСПРАВЛЕНИЕ ---
            // Нужно освободить массив, возвращённый ToEntityArray.
            // Вызывающая функция (Build) теперь вызывает Dispose.
            // ---
        }

        private static List<NativeList<Entity>> GroupWaterCells(EntityManager em, NativeArray<Entity> waterCells)
        {
            var groups = new List<NativeList<Entity>>();
            var visited = new HashSet<Entity>();

            foreach (var cellEntity in waterCells)
            {
                if (visited.Contains(cellEntity)) continue;

                var currentGroup = new NativeList<Entity>(Allocator.TempJob); // Используем TempJob для производительности
                Queue<Entity> queue = new Queue<Entity>();
                queue.Enqueue(cellEntity);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!visited.Add(current)) continue; // Уже посещён

                    currentGroup.Add(current);

                    // Найти соседей и добавить их в очередь
                    var neighbors = GetCellNeighbors(em, current);
                    foreach (var neighbor in neighbors)
                    {
                        if (em.HasComponent<WaterEntityTag>(neighbor) && !visited.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (currentGroup.Length > 0) // Убедимся, что группа не пуста
                {
                    groups.Add(currentGroup); // Добавляем, владелец - список
                }
                else
                {
                    currentGroup.Dispose(); // Если группа пуста, освобождаем её
                }
            }

            return groups;
        }

        // --- ИСПРАВЛЕНИЕ ОШИБКИ CS1061 ---
        private static List<Entity> GetCellNeighbors(EntityManager em, Entity cellEntity)
        {
            var neighbors = new List<Entity>();
            var cell = em.GetComponentData<VoronoiCell>(cellEntity); // Получаем компонент VoronoiCell
            int currentSiteIndex = cell.SiteIndex; // Извлекаем SiteIndex

            EntityQuery edgeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.TempJob); // Получаем компоненты, а не сущности

            foreach (var edge in edges) // edge теперь компонент VoronoiEdge
            {
                // Проверяем, является ли текущая ячейка одним из участников ребра
                if (edge.SiteA == currentSiteIndex || edge.SiteB == currentSiteIndex)
                {
                    // Находим индекс соседней ячейки
                    int neighborSiteIndex = (edge.SiteA == currentSiteIndex) ? edge.SiteB : edge.SiteA;

                    // Ищем сущность соседней ячейки по её SiteIndex
                    Entity neighborEntity = FindCellBySiteIndex(em, neighborSiteIndex);
                    if (neighborEntity != Entity.Null)
                    {
                        neighbors.Add(neighborEntity);
                    }
                }
            }

            return neighbors;
        }
        // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

        // --- ВСПОМОГАТЕЛЬНЫЙ МЕТОД ---
        private static Entity FindCellBySiteIndex(EntityManager em, int siteIndex)
        {
            EntityQuery cellQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>());
            using var cellEntities = cellQuery.ToEntityArray(Allocator.TempJob);

            foreach (var entity in cellEntities)
            {
                var cell = em.GetComponentData<VoronoiCell>(entity);
                if (cell.SiteIndex == siteIndex)
                {
                    return entity;
                }
            }
            return Entity.Null; // Не найдено
        }
        // --- --- --- --- --- --- --- ---


        private static void CreateWaterMesh(EntityManager em, NativeList<Entity> cellGroup, Material material, MapSettings settings, int groupIndex)
        {
            // Простой подход: создаем один меш, охватывающий все ячейки в группе
            // Более сложная реализация может строить меш по вершинам краевых ячеек
            float3 averagePos = float3.zero;
            foreach (var entity in cellGroup)
            {
                var cell = em.GetComponentData<VoronoiCell>(entity);
                averagePos += new float3(cell.Centroid.x, 0, cell.Centroid.y); // Высоту можно уточнить
            }
            averagePos /= cellGroup.Length;

            // --- ИСПРАВЛЕНИЕ ОШИБКИ CS0034 ---
            // Явно преобразуем float3 в Vector3 для Unity.Mesh
            Vector3 avgPosV3 = new Vector3(averagePos.x, averagePos.y, averagePos.z);

            // Создаем простую плоскость для группы
            Mesh mesh = new Mesh { name = $"WaterGroup_{groupIndex}" };
            Vector3[] vertices = {
                avgPosV3 + new Vector3(-50, 0, -50), // Используем Vector3
                avgPosV3 + new Vector3(50, 0, -50),  // Используем Vector3
                avgPosV3 + new Vector3(50, 0, 50),   // Используем Vector3
                avgPosV3 + new Vector3(-50, 0, 50)   // Используем Vector3
            };
            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Создаем сущность и добавляем компоненты
            Entity meshEntity = em.CreateEntity();
            UnityObjectRef<Mesh> meshRef = mesh;
            UnityObjectRef<Material> materialRef = material;
            em.AddComponentData(meshEntity, new RenderMeshUnmanaged(
                mesh: meshRef,
                materialForSubMesh: materialRef,
                subMeshIndex: 0
            ));

            // Устанавливаем позицию через LocalToWorld
            em.AddComponentData(meshEntity, new LocalToWorld { Value = float4x4.Translate(averagePos) });

            // Устанавливаем границы рендеринга
            var bounds = new AABB { Center = averagePos, Extents = new float3(60, 1, 60) };
            em.AddComponentData(meshEntity, new WorldRenderBounds { Value = bounds });

            Debug.Log($"[Water] Created mesh for group {groupIndex} with {cellGroup.Length} cells at {averagePos}");
        }
    }
}