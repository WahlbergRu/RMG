// Assets\VoronoiMapGen\Systems\MapRenderingSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using System;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Система рендеринга ячеек Вороного: создаёт меш для ячеек, используя RenderMeshUnmanaged.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiGeometryBuildSystem))] // Убедимся, что геометрия готова
    public partial class MapRenderingSystem : SystemBase
    {
        private Material _cellMaterial;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Требуем, чтобы были готовы геометрия и настройки карты
            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>(); // Требуем, чтобы карта была сгенерирована
            // Инициализируем материал при создании системы
            _cellMaterial = EnsureDefaultCellMaterial();
        }

        protected override void OnUpdate()
        {
            // Получаем настройки карты
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings))
            {
                Debug.LogWarning("MapSettings singleton not found!");
                return;
            }

            // Используем EntityQuery для поиска сущностей
            EntityQuery query = GetEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.Exclude<RenderMeshUnmanaged>() // Только те, у которых ещё нет RenderMeshUnmanaged
            );

            using var entities = query.ToEntityArray(Allocator.TempJob); // Используем TempJob для производительности
            if (entities.Length == 0)
            {
                // Debug.Log("[MapRenderingSystem] No entities found for rendering or all already rendered.");
                // Если сущностей нет, всё равно считаем задачу выполненной.
                // Создаём синглтонный тег, чтобы система больше не запускалась.
                Entity tagSingletonEntity = EntityManager.CreateEntity();
                EntityManager.AddComponent<RenderingBuiltTag>(tagSingletonEntity);
                // Debug.Log("[MapRenderingSystem] No entities to render. Marking rendering as completed.");
                return; // ВАЖНО: выходим из метода, чтобы не выполнять остальную логику
            }

            Debug.Log($"[MapRenderingSystem] Starting rendering process for {entities.Length} entities...");

            // --- ИСПОЛЬЗУЕМ EntityCommandBuffer для безопасных изменений ---
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                VoronoiCell cell = EntityManager.GetComponentData<VoronoiCell>(entity);

                // Создаём меш для текущей ячейки
                Mesh mesh = CreateCellMeshInternal(entity, in cell, settings);

                if (mesh != null)
                {
                    // Создаём UnityObjectRef для меша и материала
                    UnityObjectRef<Mesh> meshRef = mesh;
                    UnityObjectRef<Material> materialRef = _cellMaterial; // Используем поле

                    // Добавляем RenderMeshUnmanaged через ECB
                    ecb.AddComponent(entity, new RenderMeshUnmanaged(
                        mesh: meshRef,
                        materialForSubMesh: materialRef,
                        subMeshIndex: 0
                    ));

                    // Добавляем LocalTransform для позиции
                    float3 position = new float3(cell.Centroid.x, 0, cell.Centroid.y);
                    if (EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(entity))
                    {
                        var terrain = EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(entity);
                        position.y = terrain.Elevation * 100.0f; // Устанавливаем высоту из TerrainData
                    }
                    ecb.AddComponent(entity, LocalTransform.FromPosition(position));

                    // Добавляем WorldRenderBounds
                    var bounds = new AABB { Center = position, Extents = new float3(1f, 1f, 1f) };
                    ecb.AddComponent(entity, new WorldRenderBounds { Value = bounds });

                    // Debug.Log($"Created mesh for cell entity {entity} at position ({cell.Centroid.x}, {cell.Centroid.y})"); // ЗАКОММЕНТИРОВАНО
                }
                else
                {
                    Debug.LogWarning($"Failed to create mesh for entity {entity}.");
                }
            }

            // Применяем все изменения из ECB
            ecb.Playback(EntityManager);
            ecb.Dispose(); // Всегда освобождаем ECB

            // --- УСТАНАВЛИВАЕМ ТЕГ ЗАВЕРШЕНИЯ ---
            // Создаём сущность для синглтонного тега, чтобы система больше не запускалась
            Entity tagSingletonEntityForCompletion = EntityManager.CreateEntity();
            EntityManager.AddComponent<RenderingBuiltTag>(tagSingletonEntityForCompletion);

            Debug.Log($"[Rendering] Successfully rendering {entities.Length} entities");
            Debug.Log("[MapRenderingSystem] Rendering process completed.");
        }

        private Material EnsureDefaultCellMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Legacy Shaders/Diffuse");
            var material = new Material(shader)
            {
                name = "DefaultCellMaterial",
                enableInstancing = true
            };
            material.color = new Color(0.5f, 0.7f, 0.5f, 1.0f);
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Smoothness", 0.5f);
            return material;
        }

        private Mesh CreateCellMeshInternal(Entity entity, in VoronoiCell cell, MapSettings settings)
        {
            try
            {
                // Получаем буферы
                DynamicBuffer<CellPolygonVertex> vertsBuffer = EntityManager.GetBuffer<CellPolygonVertex>(entity);
                DynamicBuffer<CellTriIndex> trisBuffer = EntityManager.GetBuffer<CellTriIndex>(entity);

                if (vertsBuffer.Length < 3 || trisBuffer.Length < 3)
                {
                    Debug.LogWarning($"Entity {entity} has insufficient geometry data (verts: {vertsBuffer.Length}, tris: {trisBuffer.Length})");
                    return null;
                }

                var centroid = new float3(cell.Centroid.x, 0, cell.Centroid.y);
                Mesh mesh = new Mesh
                {
                    name = $"Cell_L{cell.Level}_S{cell.SiteIndex}",
                    indexFormat = IndexFormat.UInt32
                };

                Vector3[] vertices = new Vector3[vertsBuffer.Length];
                for (int i = 0; i < vertsBuffer.Length; i++)
                {
                    var vertex = vertsBuffer[i].Value;
                    // Используем высоту из TerrainData, если она есть
                    float y = 0.0f;
                    if (EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(entity))
                    {
                        var terrain = EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(entity);
                        y = terrain.Elevation * 100.0f; // Масштаб высоты
                    }
                    vertices[i] = new Vector3(vertex.x - centroid.x, y, vertex.z - centroid.z); // Применяем высоту
                }

                int[] triangles = new int[trisBuffer.Length];
                for (int i = 0; i < trisBuffer.Length; i++)
                {
                    triangles[i] = trisBuffer[i].Value;
                }

                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.Optimize();
                return mesh;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to create mesh for cell entity {entity}: {e.Message}");
                return null;
            }
        }

        protected override void OnDestroy()
        {
            // Очищаем материал при уничтожении системы
            if (_cellMaterial != null)
            {
                UnityEngine.Object.Destroy(_cellMaterial);
            }
            base.OnDestroy();
        }
    }

    // --- Добавляем вспомогательный тег ---
    public struct RenderingBuiltTag : IComponentData {}
    // --- --- --- --- --- --- --- --- --- ---
}