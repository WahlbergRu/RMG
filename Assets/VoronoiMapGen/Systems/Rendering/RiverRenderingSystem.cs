using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using System.Collections.Generic;

namespace VoronoiMapGen.Systems.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMeshCreateSystem))] 
    public partial class RiverRenderingSystem : SystemBase
    {
        private Material _riverMaterial;
        private List<Mesh> _createdMeshes = new List<Mesh>();

        private int _lastRiverMask = -1;
        private int _lastTerrainMask = -1;
        private bool _lastShowRivers = false;

        protected override void OnCreate()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); 
            if (shader == null) shader = Shader.Find("Hidden/Internal-ErrorShader");

            _riverMaterial = new Material(shader);
            _riverMaterial.color = new Color(0.0f, 0.5f, 1.0f, 0.8f); 
            _riverMaterial.SetFloat("_Smoothness", 0.9f);
            _riverMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off); 
            _riverMaterial.enableInstancing = true;
            
            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>();
        }

        protected override void OnDestroy()
        {
            // При закрытии удаляем мгновенно
            CleanupResources(immediate: true);
            if (_riverMaterial != null) Object.DestroyImmediate(_riverMaterial);
        }

        // --- ИСПРАВЛЕНИЕ: БЕЗОПАСНАЯ ОЧИСТКА ---
        public void CleanupResources(bool immediate = false)
        {
            foreach (var m in _createdMeshes) {
                if (m != null) 
                {
                    if (immediate) Object.DestroyImmediate(m);
                    else Object.Destroy(m); // Отложенное удаление (Play Mode)
                }
            }
            _createdMeshes.Clear();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;

            // Проверяем изменения настроек (маски слоев или вкл/выкл)
            bool settingsChanged = (settings.RiverRenderMask != _lastRiverMask) ||
                                   (settings.RenderLevelMask != _lastTerrainMask) ||
                                   (settings.ShowRivers != _lastShowRivers);

            _lastRiverMask = settings.RiverRenderMask;
            _lastTerrainMask = settings.RenderLevelMask;
            _lastShowRivers = settings.ShowRivers;

            // Если настройки изменились -> удаляем старые реки
            if (settingsChanged || !settings.ShowRivers)
            {
                var q = EntityManager.CreateEntityQuery(typeof(RiverChunkTag));
                if (!q.IsEmpty) EntityManager.DestroyEntity(q);
                
                // Чистим меши безопасно (false = не immediate)
                if (_createdMeshes.Count > 0) CleanupResources(false);
            }

            if (!settings.ShowRivers) return;

            var existingRivers = SystemAPI.QueryBuilder().WithAll<RiverChunkTag>().Build();
            if (!existingRivers.IsEmpty) return;

            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;

            // Ждем инициализации террейна (хотя бы данных), чтобы взять стили
            var terrainQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCellMeshTag>().Build();
            if (terrainQuery.IsEmpty) return;

            var visBuffer = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity);
            var styles = visBuffer.ToNativeArray(Allocator.TempJob); 

            // Запускаем Builder
            RiverMeshBuilder.Build(EntityManager, _riverMaterial, settings, styles, _createdMeshes);
            
            styles.Dispose();
        }
    }
}