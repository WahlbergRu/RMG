using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private EntityQuery _settingsQuery;

        protected override void OnCreate()
        {
            _settingsQuery = GetEntityQuery(ComponentType.ReadOnly<MapSettings>());
            RequireForUpdate(_settingsQuery);
        }

        protected override void OnUpdate()
        {
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            var settings = SystemAPI.GetSingleton<MapSettings>();

            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
            {
                Enabled = false; // карта готова, отключаем систему
                return;
            }

            if (EntityManager.HasComponent<MapGenerationInProgress>(settingsEntity))
                return; // генерация уже идёт

            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);


            Debug.Log(settingsEntity);
            Debug.Log(settings);
            try
            {
                
                DynamicBuffer<LevelSettings> levelSettingsBuffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
                NativeArray<LevelSettings> levelArray = levelSettingsBuffer.ToNativeArray(Allocator.TempJob);
                LevelGenerationPipeline.GenerateLevels(EntityManager, settings, levelArray);
                BiomeGenerationPipeline.GenerateBiomes(EntityManager, settings);
                MapReportGenerator.Report(EntityManager, settings, levelArray);
                
                levelArray.Dispose();

                // --- завершение ---
                EntityManager.AddComponent<MapGeneratedTag>(settingsEntity);
                Enabled = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Map generation failed: {e}");
            }
            finally
            {
                if (EntityManager.HasComponent<MapGenerationInProgress>(settingsEntity))
                    EntityManager.RemoveComponent<MapGenerationInProgress>(settingsEntity);
            }
        }
    }
}
