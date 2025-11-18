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
}