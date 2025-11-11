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
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) Generate(
            MapSettings settings,
            in NativeArray<LevelSettings> levelSettingsNative,
            LevelSettings levelSettings,
            int level,
            in NativeArray<VoronoiCell> parentCells)
        {
            NativeArray<float2> sites = new NativeArray<float2>(levelSettings.SiteCount, Allocator.TempJob);
            NativeArray<VoronoiSite> siteMetadata = new NativeArray<VoronoiSite>(levelSettings.SiteCount, Allocator.TempJob);

            NativeArray<VoronoiCell> currentParentCells = parentCells;
            bool createdTempParent = false;
            if (!parentCells.IsCreated || parentCells.Length == 0 || level == 0)
            {
                currentParentCells = new NativeArray<VoronoiCell>(0, Allocator.TempJob);
                createdTempParent = true;
            }

            MultiLevelSiteGenerationJob siteJob = new MultiLevelSiteGenerationJob
            {
                LevelSettings = levelSettingsNative,
                MapSize = settings.MapSize,
                BaseSeed = settings.Seed,
                ParentLevel = level - 1,
                ParentCells = currentParentCells,
                Sites = sites,
                SiteMetadata = siteMetadata
            };

            Stopwatch sw = Stopwatch.StartNew();
            JobHandle jobHandle = siteJob.Schedule(default);
            jobHandle.Complete();
            sw.Stop();
            Debug.Log($"[Level {level}] MultiLevelSiteGenerationJob completed in {sw.ElapsedMilliseconds} ms");

            if (createdTempParent)
                currentParentCells.Dispose();

            return (sites, siteMetadata);
        }
    }
}