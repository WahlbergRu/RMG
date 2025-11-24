using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Rendering
{
    public static class CellMeshBuilder
    {
        public static void Build(EntityManager em, Material material)
        {
            // Добавили ComponentType.ReadOnly<VoronoiCell>() в запрос, чтобы читать центроид
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.Exclude<VoronoiCellMeshTag>());

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            var meshes = new List<UnityEngine.Mesh>();
            var cellList = new List<Entity>();

            foreach (var entity in entities)
            {
                if (!em.Exists(entity)) continue;

                var verts = em.GetBuffer<CellPolygonVertex>(entity);
                var tris = em.GetBuffer<CellTriIndex>(entity);
                var cell = em.GetComponentData<VoronoiCell>(entity); // Получаем ячейку

                if (verts.Length < 3 || tris.Length < 3) continue;

                // Передаем центроид для перевода в локальные координаты
                meshes.Add(CreateMeshFromCellLocal(entity, verts, tris, cell.Centroid));
                cellList.Add(entity);
            }

            if (cellList.Count == 0) return;

            var renderMeshArray = new RenderMeshArray(new[] { material }, meshes.ToArray());
            var desc = new RenderMeshDescription(ShadowCastingMode.On, true);

            for (int i = 0; i < cellList.Count; i++)
                SetupCellEntity(em, cellList[i], renderMeshArray, desc, i);
        }

        private static UnityEngine.Mesh CreateMeshFromCellLocal(Entity entity,
            DynamicBuffer<CellPolygonVertex> verts, DynamicBuffer<CellTriIndex> tris, float2 centroid)
        {
            var mesh = new UnityEngine.Mesh
            {
                name = $"CellMesh_{entity.Index}",
                indexFormat = IndexFormat.UInt32
            };

            // Преобразуем вершины в ЛОКАЛЬНЫЕ координаты
            var vArray = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float3 worldPos = verts[i].Value;
                
                // ВАЖНО: Вычитаем центроид. 
                // worldPos.x - centroid.x
                // worldPos.z - centroid.y (так как в 3D игре Y - это высота, а Z - глубина, а у вас 2D карта на плоскости XZ)
                vArray[i] = new Vector3(worldPos.x - centroid.x, 0, worldPos.z - centroid.y);
            }

            // Используем индексы как есть
            var tArray = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                tArray[i] = tris[i].Value;
            }

            mesh.SetVertices(vArray);
            mesh.SetTriangles(tArray, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void SetupCellEntity(EntityManager em, Entity entity,
            RenderMeshArray renderMeshArray, RenderMeshDescription desc, int meshIndex)
        {
            if (!em.Exists(entity)) return;

            // Здесь мы ставим Transform сущности в точку центроида.
            // Т.к. меш теперь локальный (вокруг 0,0), то (Centroid + LocalMesh) даст правильную позицию.
            float3 pos = new float3(em.GetComponentData<VoronoiCell>(entity).Centroid.x, 0f,
                                    em.GetComponentData<VoronoiCell>(entity).Centroid.y);

            RenderMeshUtility.AddComponents(entity, em, desc, renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, meshIndex));

            // Цвет биома (если есть)
            if (em.HasComponent<CellBiome>(entity))
            {
                // Пример заглушки, т.к. BiomeColors статический класс не был предоставлен, 
                // но логика понятна. Раскомментируйте, если у вас есть этот класс.
                var biome = em.GetComponentData<CellBiome>(entity);
                em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = BiomeColors.Get(biome.Type) });
            }
            // Временный цвет для отладки, если нет биомов
            else 
            {
                // Случайный цвет для наглядности
                var rnd = new Unity.Mathematics.Random((uint)entity.Index + 1);
                float4 col = new float4(rnd.NextFloat(), rnd.NextFloat(), rnd.NextFloat(), 1f);
                em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = col });
            }
            
            em.AddComponentData(entity, LocalTransform.FromPosition(pos));
            em.AddComponent<VoronoiCellMeshTag>(entity);
        }
    }
}