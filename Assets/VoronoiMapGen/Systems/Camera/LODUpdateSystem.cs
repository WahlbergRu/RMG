using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LODUpdateSystem : SystemBase
    {
        private int _lastLevelIndex = -1;
        private const float HYSTERESIS = 10.0f; 

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingletonRW<MapSettings>(out var mapSettingsRw)) return;
            
            // --- ПРОВЕРКА ---
            // Если Авто-ЛОД выключен, мы выходим и позволяем Bootstrap'у управлять масками вручную
            if (!mapSettingsRw.ValueRO.UseAutoLOD) 
            {
                _lastLevelIndex = -1; // Сброс состояния
                return;
            }

            if (!SystemAPI.TryGetSingleton<CameraSettingsData>(out var camSettings)) return;
            
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<LevelSettings>(settingsEntity)) return;
            var levels = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            if (levels.Length == 0) return;

            float currentZoom = camSettings.TargetFocusPoint.y;
            
            // Логика расчета уровня
            int targetLevel = levels.Length - 1; 

            for (int i = 0; i < levels.Length; i++)
            {
                float threshold = levels[i].LODThreshold;
                float checkThreshold = (i == _lastLevelIndex) ? (threshold - HYSTERESIS) : threshold;

                if (currentZoom > checkThreshold)
                {
                    targetLevel = i;
                    break; 
                }
            }

            if (targetLevel != _lastLevelIndex)
            {
                int newMask = (1 << targetLevel);

                if (mapSettingsRw.ValueRO.RenderLevelMask != newMask)
                {
                    mapSettingsRw.ValueRW.RenderLevelMask = newMask;
                    mapSettingsRw.ValueRW.RiverRenderMask = newMask;
                    
                    Debug.Log($"[LOD] Zoom: {currentZoom:F0} -> Switched to Level: {targetLevel}");
                }
                
                _lastLevelIndex = targetLevel;
            }
        }
    }
}