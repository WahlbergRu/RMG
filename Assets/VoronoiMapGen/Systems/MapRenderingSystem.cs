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
using VoronoiMapGen.Rendering;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Система рендеринга ячеек Вороного: создаёт меш для ячеек, используя RenderMeshUnmanaged.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MapRenderingSystem : SystemBase
    {
        private bool _cellsSpawned;
        private Material _cellMaterial;

        protected override void OnCreate()
        {
            // Инициализируем материал при создании системы
            _cellMaterial = EnsureDefaultCellMaterial();
            
            // Требуем, чтобы были готовы геометрия и настройки карты
            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>(); // Требуем, чтобы карта была сгенерирована
        }

        protected override void OnUpdate()
        {
            // Получаем настройки карты
            if (!SystemAPI.TryGetSingleton<MapSettings>(out MapSettings settings))
            {
                Debug.LogWarning("MapSettings singleton not found!");
                return;
            }

            if (!_cellsSpawned)
            {
                CellMeshBuilder.Build(EntityManager, _cellMaterial);
                _cellsSpawned = true;
            }
        }

        private Material EnsureDefaultCellMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit");
            Material material = new Material(shader)
            {
                name = "DefaultCellMaterial",
                enableInstancing = true
            };
            material.color = new Color(0.5f, 0.7f, 0.5f, 1.0f);
            material.SetFloat("_Metallic", 0.0f);
            material.SetFloat("_Smoothness", 0.5f);
            return material;
        }

        // private Mesh CreateCellMeshInternal(Entity entity, in VoronoiCell cell, MapSettings settings)
        // {
        //     try
        //     {
        //         // Получаем буферы
        //         DynamicBuffer<CellPolygonVertex> vertsBuffer = EntityManager.GetBuffer<CellPolygonVertex>(entity);
        //         DynamicBuffer<CellTriIndex> trisBuffer = EntityManager.GetBuffer<CellTriIndex>(entity);
        //
        //         if (vertsBuffer.Length < 3 || trisBuffer.Length < 3)
        //         {
        //             Debug.LogWarning($"Entity {entity} has insufficient geometry data (verts: {vertsBuffer.Length}, tris: {trisBuffer.Length})");
        //             return null;
        //         }
        //
        //         var centroid = new float3(cell.Centroid.x, 0, cell.Centroid.y);
        //         Mesh mesh = new Mesh
        //         {
        //             name = $"Cell_L{cell.Level}_S{cell.SiteIndex}",
        //             indexFormat = IndexFormat.UInt32
        //         };
        //
        //         Vector3[] vertices = new Vector3[vertsBuffer.Length];
        //         for (int i = 0; i < vertsBuffer.Length; i++)
        //         {
        //             var vertex = vertsBuffer[i].Value;
        //             // Используем высоту из TerrainData, если она есть
        //             float y = 0.0f;
        //             if (EntityManager.HasComponent<VoronoiMapGen.Components.TerrainData>(entity))
        //             {
        //                 var terrain = EntityManager.GetComponentData<VoronoiMapGen.Components.TerrainData>(entity);
        //                 y = terrain.Elevation * 100.0f; // Масштаб высоты
        //             }
        //             vertices[i] = new Vector3(vertex.x - centroid.x, y, vertex.z - centroid.z); // Применяем высоту
        //         }
        //
        //         int[] triangles = new int[trisBuffer.Length];
        //         for (int i = 0; i < trisBuffer.Length; i++)
        //         {
        //             triangles[i] = trisBuffer[i].Value;
        //         }
        //
        //         mesh.SetVertices(vertices);
        //         mesh.SetTriangles(triangles, 0);
        //         mesh.RecalculateNormals();
        //         mesh.RecalculateBounds();
        //         return mesh;
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"Failed to create mesh for cell entity {entity}: {e.Message}");
        //         return null;
        //     }
        // }

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