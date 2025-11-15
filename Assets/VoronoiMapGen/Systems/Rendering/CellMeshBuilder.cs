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
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.Exclude<VoronoiCellMeshTag>());

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            Debug.Log($"CellMeshBuilder.Build: Found {entities.Length} entities to process.");

            if (entities.Length == 0) return;

            List<Mesh> meshes   = new List<Mesh>();
            List<Entity> cellList = new List<Entity>();

            foreach (Entity entity in entities)
            {
                if (!em.Exists(entity))
                {
                    Debug.LogWarning($"CellMeshBuilder.Build: Entity {entity.Index} does not exist.");
                    continue;
                }

                DynamicBuffer<CellPolygonVertex> verts = em.GetBuffer<CellPolygonVertex>(entity);
                DynamicBuffer<CellTriIndex> triPairs  = em.GetBuffer<CellTriIndex>(entity);

                Debug.Log($"Processing Entity {entity.Index}: verts.Length={verts.Length}, triPairs.Length={triPairs.Length}");

                // Проверяем минимальные условия
                if (verts.Length < 3)
                {
                    Debug.LogWarning($"Entity {entity.Index}: Not enough vertices ({verts.Length}) to form a mesh.");
                    continue;
                }

                // Проверяем, есть ли какие-то индексы для треугольников
                if (triPairs.Length == 0)
                {
                    Debug.LogWarning($"Entity {entity.Index}: No triangle indices provided (triPairs.Length is 0).");
                    continue;
                }

                // Проверяем, что индексы в triPairs не выходят за пределы verts
                bool validIndices = true;
                for (int i = 0; i < triPairs.Length; i++)
                {
                    int idx = triPairs[i].Value;
                    if (idx < 0 || idx >= verts.Length)
                    {
                        Debug.LogError($"Entity {entity.Index}: triPair index {idx} at position {i} is out of bounds for verts.Length={verts.Length}");
                        validIndices = false;
                        break;
                    }
                }
                if (!validIndices) continue;

                Mesh mesh = CreateMeshFromCellLocal(em, entity, verts, triPairs);
                if (mesh != null)
                {
                    meshes.Add(mesh);
                    cellList.Add(entity);
                    Debug.Log($"Successfully created mesh for Entity {entity.Index}");
                    // Проверка не нужна, так как массивы синхронизированы в цикле
                }
                else
                {
                     Debug.LogWarning($"Failed to create mesh for Entity {entity.Index}");
                }
            }

            if (cellList.Count == 0)
            {
                Debug.LogWarning("CellMeshBuilder.Build: No valid meshes were created.");
                return;
            }

            Debug.Log($"CellMeshBuilder.Build: Creating individual RenderMeshArrays for {meshes.Count} entities.");
            RenderMeshDescription desc = new RenderMeshDescription(ShadowCastingMode.Off, true);

            for (int i = 0; i < cellList.Count; i++)
            {
                 // Создаём RenderMeshArray, содержащий только одну сетку для одной сущности
                 RenderMeshArray renderMeshArray = new RenderMeshArray(new[] { material }, new [] {meshes[i]});
                 // meshIndex всегда 0, потому что RenderMeshArray содержит только 1 сетку (meshes[i])
                 SetupCellEntity(em, cellList[i], renderMeshArray, desc, 0); // Передаём 0 как meshIndex
            }
        }

        private static Mesh CreateMeshFromCellLocal(EntityManager em, Entity entity,
            DynamicBuffer<CellPolygonVertex> verts, DynamicBuffer<CellTriIndex> triPairs)
        {
            VoronoiCell cell = em.GetComponentData<VoronoiCell>(entity);
            float2 c = cell.Centroid;

            Mesh mesh = new Mesh
            {
                name = $"CellMesh_{entity.Index}",
                indexFormat = IndexFormat.UInt32
            };

            // --- Создание вертексного массива ---
            Vector3[] vArray = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float3 v = verts[i].Value;
                vArray[i] = new Vector3(v.x - c.x, 0f, v.z - c.y);
            }

            // --- Создание индексного массива и заполнение ---
            if (triPairs.Length < 3)
            {
                Debug.LogWarning($"Entity {entity.Index}: Not enough triPairs ({triPairs.Length}) to form a triangle fan. Need at least 3.");
                return null;
            }

            int requiredIndexCount = (triPairs.Length - 2) * 3;
            int[] tArray = new int[requiredIndexCount];

            int idx = 0;
            for (int i = 0; i < triPairs.Length - 2; i++)
            {
                tArray[idx++] = 0; // Индекс центральной вершины в vArray
                tArray[idx++] = triPairs[i].Value;     // Индекс вершины P[i]
                tArray[idx++] = triPairs[i + 1].Value; // Индекс вершины P[i+1]
            }

            Debug.Log($"Entity {entity.Index}: Setting {vArray.Length} vertices and {tArray.Length} indices. SubMesh count: {(tArray.Length / 3)}");

            mesh.SetVertices(vArray);
            if (tArray.Length > 0)
            {
                mesh.SetTriangles(tArray, 0);
            }
            else
            {
                 Debug.LogWarning($"Entity {entity.Index}: tArray is empty, no triangles will be set.");
            }

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            // --- Дополнительная отладка ---
            Debug.Log($"Entity {entity.Index}: Final mesh has {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles.");
            if (mesh.triangles.Length > 0)
            {
                var triangles = mesh.triangles;
                string triLog = $"First few tris for {entity.Index}: ";
                for (int i = 0; i < Mathf.Min(9, triangles.Length); i++)
                {
                    triLog += $"{triangles[i]} ";
                }
                Debug.Log(triLog);
            }

            return mesh;
        }

        private static void SetupCellEntity(EntityManager em, Entity entity,
            RenderMeshArray renderMeshArray, RenderMeshDescription desc, int meshIndex) // meshIndex теперь всегда 0 для этого подхода
        {
            if (!em.Exists(entity)) return;

            float3 pos = new float3(em.GetComponentData<VoronoiCell>(entity).Centroid.x, 0f,
                             em.GetComponentData<VoronoiCell>(entity).Centroid.y);

            // MaterialMeshInfo указывает на индекс материала (0) и индекс сетки (meshIndex) внутри renderMeshArray
            // Так как renderMeshArray содержит только 1 сетку, meshIndex всегда должен быть 0
            MaterialMeshInfo mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(0, meshIndex); // materialIndexInRenderMeshArray = 0, meshIndexInRenderMeshArray = 0 (в нашем случае)

            // Добавляем компоненты
            RenderMeshUtility.AddComponents(entity, em, desc, renderMeshArray, mmi);

            // Добавляем цвет биома
            if (em.HasComponent<CellBiome>(entity))
            {
                CellBiome biome = em.GetComponentData<CellBiome>(entity);
                em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = BiomeColors.Get(biome.Type) });
            }
            else
            {
                em.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = BiomeColors.Get(BiomeType.Grassland) });
            }

            // Устанавливаем позицию трансформации
            em.AddComponentData(entity, LocalTransform.FromPosition(pos));
            // Помечаем сущность как обработанную
            em.AddComponent<VoronoiCellMeshTag>(entity);

            // Debug.Log($"Entity {entity.Index}: Added RenderMesh components, set position to {pos}, added tag. Using meshIndex {meshIndex} for RenderMeshArray with {renderMeshArray.Meshes.Length} meshes.");
        }
    }
}