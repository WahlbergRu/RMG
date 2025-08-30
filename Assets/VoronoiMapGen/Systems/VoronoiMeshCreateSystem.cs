using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Система, создающая меш и рендер-компоненты для ячейки Вороного ТОЛЬКО ОДИН РАЗ.
    /// После этого меш может быть обновлён VoronoiMeshUpdateSystem.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshCreateSystem : SystemBase
    {
        private Material _material;

        protected override void OnCreate()
        {
            // Загружаем материал
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Shader 'Universal Render Pipeline/Lit' not found.");
                return;
            }

            _material = new Material(shader) { name = "VoronoiCellMaterial" };

            // Требуем обновления
            RequireForUpdate<CellPolygonVertex>();
            RequireForUpdate<CellTriIndex>();
        }

        protected override void OnUpdate()
        {
            // Ищем ячейки, у которых есть данные, но ещё нет метки меша
            var query = GetEntityQuery(
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.Exclude<VoronoiCellMeshTag>());

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            // Собираем ТОЛЬКО валидные сущности и мешы
            var validEntities = new List<Entity>();
            var validMeshes = new List<UnityEngine.Mesh>();

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityManager.Exists(entity)) continue;

                var vertices = EntityManager.GetBuffer<CellPolygonVertex>(entity);
                var triangles = EntityManager.GetBuffer<CellTriIndex>(entity);

                if (vertices.Length < 3 || triangles.Length < 3)
                {
                    Debug.LogWarning($"Entity {entity.Index} has invalid mesh data. Skipping.");
                    continue;
                }

                // Создаём меш
                var mesh = new UnityEngine.Mesh
                {
                    name = $"VoronoiCell_Mesh_{entity.Index}",
                    indexFormat = IndexFormat.UInt32
                };

                // Вершины
                var meshVertices = new Vector3[vertices.Length];
                for (int j = 0; j < vertices.Length; j++)
                {
                    float2 v = vertices[j].Value;
                    meshVertices[j] = new Vector3(v.x, 0f, v.y);
                }

                // Индексы
                var meshIndices = new int[triangles.Length];
                for (int j = 0; j < triangles.Length; j++)
                {
                    meshIndices[j] = triangles[j].Value;
                }

                mesh.SetVertices(meshVertices);
                mesh.SetTriangles(meshIndices, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();

                // Добавляем ТОЛЬКО валидные данные
                validEntities.Add(entity);
                validMeshes.Add(mesh);
            }

            // Если нет валидных мешей — выходим
            if (validEntities.Count == 0) return;

            // Преобразуем в массивы
            var meshesArray = validMeshes.ToArray();
            var renderMeshArray = new RenderMeshArray(new[] { _material }, meshesArray);

            // Настройка рендера
            var renderMeshDesc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.On,
                receiveShadows: true,
                motionVectorGenerationMode: MotionVectorGenerationMode.Camera
            );

            // Добавляем компоненты для каждой валидной сущности
            for (int i = 0; i < validEntities.Count; i++)
            {
                Entity entity = validEntities[i];
                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<VoronoiCellMeshTag>(entity)) continue;

                // Получаем позицию
                float3 pos = float3.zero;
                if (EntityManager.HasComponent<CellLocalPosition>(entity))
                {
                    pos = EntityManager.GetComponentData<CellLocalPosition>(entity).Value;
                }
                else if (EntityManager.HasComponent<VoronoiCell>(entity))
                {
                    var centroid = EntityManager.GetComponentData<VoronoiCell>(entity).Centroid;
                    pos = new float3(centroid.x, 0f, centroid.y); // Y=0, Z=вертикальная позиция
                }

                // Добавляем компоненты через утилиту
                RenderMeshUtility.AddComponents(
                    entity,
                    EntityManager,
                    renderMeshDesc,
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, i) // i — индекс в validMeshes
                );

                // Устанавливаем цвет материала
                if (!EntityManager.HasComponent<URPMaterialPropertyBaseColor>(entity))
                {
                    EntityManager.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });
                }

                // Устанавливаем трансформацию (через LocalToWorld)
                EntityManager.SetComponentData(entity, new LocalToWorld { Value = float4x4.Translate(pos) });

                // Добавляем метку, что меш создан
                EntityManager.AddComponent<VoronoiCellMeshTag>(entity);
            }
        }
    }
}