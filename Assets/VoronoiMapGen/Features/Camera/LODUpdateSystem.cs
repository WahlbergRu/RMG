using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Camera.Components;

namespace VoronoiMapGen.Features.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LODUpdateSystem : SystemBase
    {
        private int _lastLevelIndex = -1;
        
        // Гистерезис, чтобы не моргало на границе переключения
        private const float HYSTERESIS = 10.0f; 

        protected override void OnUpdate()
        {
            // 1. Получаем доступ к настройкам
            if (!SystemAPI.TryGetSingletonRW<MapSettings>(out var mapSettingsRw)) return;
            
            // Если Авто-ЛOД выключен вручную - не вмешиваемся
            if (!mapSettingsRw.ValueRO.UseAutoLOD) 
            {
                _lastLevelIndex = -1; 
                return;
            }

            if (!SystemAPI.TryGetSingleton<CameraSettingsData>(out var camSettings)) return;
            
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<LevelSettings>(settingsEntity)) return;
            var levels = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            if (levels.Length == 0) return;

            // 2. Получаем текущий зум (дистанция до земли)
            float currentZoom = camSettings.TargetFocusPoint.y;

            // 3. Определяем целевой уровень детализации
            // (Ищем первый уровень, чей порог меньше текущей высоты)
            int targetLevel = levels.Length - 1; // По умолчанию самый детальный

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

            // 4. Применяем изменения
            if (targetLevel != _lastLevelIndex)
            {
                // --- ЗЕМЛЯ: EXCLUSIVE (Только один уровень) ---
                // Мы не хотим видеть L0 и L1 одновременно, они наложатся друг на друга.
                int terrainMask = (1 << targetLevel);

                // --- РЕКИ: CUMULATIVE (Все уровни до текущего) ---
                // Если мы на L2, мы хотим видеть реки L0 + L1 + L2.
                // Чем ближе зум, тем больше мелких деталей (устьев, притоков) появляется.
                int riverMask = 0;
                for (int k = 0; k <= targetLevel; k++)
                {
                    riverMask |= (1 << k);
                }

                // Записываем маски, если они изменились
                bool changed = false;
                if (mapSettingsRw.ValueRO.RenderLevelMask != terrainMask)
                {
                    mapSettingsRw.ValueRW.RenderLevelMask = terrainMask;
                    changed = true;
                }
                
                if (mapSettingsRw.ValueRO.RiverRenderMask != riverMask)
                {
                    mapSettingsRw.ValueRW.RiverRenderMask = riverMask;
                    changed = true;
                }

                if (changed)
                {
                    Debug.Log($"[LOD] Zoom: {currentZoom:F0} -> Level {targetLevel}. TerrainMask: {Convert.ToString(terrainMask, 2)}, RiverMask: {Convert.ToString(riverMask, 2)}");
                }
                
                _lastLevelIndex = targetLevel;
            }
        }
    }
}