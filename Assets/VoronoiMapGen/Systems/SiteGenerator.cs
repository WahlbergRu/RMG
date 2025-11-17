using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using Debug = UnityEngine.Debug;

namespace VoronoiMapGen.Systems
{
    public static class SiteGenerator
    {
        // <<< ИЗМЕНЕНО: Добавлен parentSiteMetadata >>>
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) Generate(
            MapSettings settings,
            in NativeArray<LevelSettings> levelSettingsNative,
            LevelSettings levelSettings,
            int level,
            in NativeArray<VoronoiCell> parentCells, // Должен быть Persistent или TempJob
            in NativeArray<float2> parentSites,      // Должен быть Persistent или TempJob
            in NativeArray<VoronoiSite> parentSiteMetadata) // Должен быть Persistent или TempJob (НОВОЕ)
        {
            // --- Создаём ВОЗВРАЩАЕМЫЕ массивы с Persistent ---
            NativeArray<float2> sites = new NativeArray<float2>(levelSettings.SiteCount, Allocator.Persistent);
            NativeArray<VoronoiSite> siteMetadata = new NativeArray<VoronoiSite>(levelSettings.SiteCount, Allocator.Persistent);

            MultiLevelSiteGenerationJob siteJob = new MultiLevelSiteGenerationJob
            {
                LevelSettings = levelSettingsNative,
                MapSize = settings.MapSize,
                BaseSeed = settings.Seed,
                ParentLevel = level - 1,
                ParentCells = parentCells, // Эти массивы должны быть Persistent или TempJob
                ParentSites = parentSites, // Эти массивы должны быть Persistent или TempJob
                ParentSiteMetadata = parentSiteMetadata, // <<< ПЕРЕДАЁМ parentSiteMetadata (НОВОЕ)
                Sites = sites, // Возвращаемый массив - Persistent
                SiteMetadata = siteMetadata // Возвращаемый массив - Persistent
            };

            Stopwatch sw = Stopwatch.StartNew();
            JobHandle jobHandle = siteJob.Schedule(default);
            jobHandle.Complete(); // <<< ВАЖНО: Дожидаемся завершения джоба СРАЗУ
            sw.Stop();
            Debug.Log($"[Level {level}] MultiLevelSiteGenerationJob completed in {sw.ElapsedMilliseconds} ms");

            // Возвращаем Persistent массивы. MapGenerationSystem принимает на себя владение.
            return (sites, siteMetadata);
        }
    }
}