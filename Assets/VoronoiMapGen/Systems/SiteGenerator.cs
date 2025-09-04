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
        /// <summary>
        /// Генерирует массив сайтов (позиции) и метаданные VoronoiSite для уровня.
        /// Возвращаемые NativeArray'ы должны быть освобождены вызывающим кодом.
        /// </summary>
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) Generate(
            MapSettings settings,
            in NativeArray<LevelSettings> levelSettingsNative,
            LevelSettings levelSettings,
            int level,
            in NativeArray<VoronoiCell> parentCells)
        {
            var sites = new NativeArray<float2>(levelSettings.SiteCount, Allocator.TempJob);
            var siteMetadata = new NativeArray<VoronoiSite>(levelSettings.SiteCount, Allocator.TempJob);

            // Если parentCells не создан (уровень 0), передаём пустой массив
            var currentParentCells = parentCells;
            var createdTempParent = false;
            if (!parentCells.IsCreated || parentCells.Length == 0 || level == 0)
            {
                currentParentCells = new NativeArray<VoronoiCell>(0, Allocator.TempJob);
                createdTempParent = true;
            }

            var siteJob = new MultiLevelSiteGenerationJob
            {
                LevelSettings = levelSettingsNative,
                MapSize = settings.MapSize,
                BaseSeed = settings.Seed,
                ParentLevel = level - 1,
                ParentCells = currentParentCells,
                Sites = sites,
                SiteMetadata = siteMetadata
            };

            var sw = Stopwatch.StartNew();
            siteJob.Schedule().Complete();
            sw.Stop();
            Debug.Log($"  MultiLevelSiteGenerationJob completed in {sw.ElapsedMilliseconds} ms (level {level})");

            if (createdTempParent)
                currentParentCells.Dispose();

            return (sites, siteMetadata);
        }
    }
}
