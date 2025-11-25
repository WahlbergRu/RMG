using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using UnityEngine;
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
            in NativeArray<VoronoiCell> parentCells,
            in NativeArray<float2> parentSites,
            in NativeArray<VoronoiSite> parentSiteMetadata)
        {
            int totalAllocation;
            
            // === РАСЧЕТ РАЗМЕРА МАССИВА ===
            if (level == 0)
            {
                // L0: Все как раньше (SiteCount = общее количество)
                int ghostsPerSide = (int)(math.sqrt(levelSettings.SiteCount) * 0.8f);
                ghostsPerSide = math.clamp(ghostsPerSide, 5, 30);
                int ghostCount = 12 + (4 * ghostsPerSide); 
                totalAllocation = levelSettings.SiteCount + ghostCount;
            }
            else
            {
                // L1+: ЛОГИКА ИЗМЕНЕНА
                // Теперь SiteCount - это "Количество детей на ОДНОГО родителя"
                
                int parentGhosts = 0;
                int realParents = 0;

                if (parentSiteMetadata.IsCreated)
                {
                    // Считаем на Main Thread (это быстро для <10k элементов)
                    for (int i = 0; i < parentSiteMetadata.Length; i++)
                    {
                        if (parentSiteMetadata[i].Value < -0.5f) parentGhosts++;
                        else realParents++;
                    }
                }
                
                // Итого = (Копируем призраков) + (Реальные родители * Кол-во детей)
                // Если SiteCount = 50, а родителей 10, выделим память под 500 точек.
                int childrenCount = realParents * levelSettings.SiteCount;
                
                // Добавляем небольшой запас на случай ошибок округления или добивки
                totalAllocation = parentGhosts + childrenCount + 64; 
            }

            // Создаем массивы
            NativeArray<float2> sites = new NativeArray<float2>(totalAllocation, Allocator.Persistent);
            NativeArray<VoronoiSite> siteMetadata = new NativeArray<VoronoiSite>(totalAllocation, Allocator.Persistent);

            MultiLevelSiteGenerationJob siteJob = new MultiLevelSiteGenerationJob
            {
                LevelSettings = levelSettingsNative,
                MapSize = settings.MapSize,
                BaseSeed = settings.Seed,
                ParentLevel = level - 1,
                ParentCells = parentCells,
                ParentSites = parentSites,
                ParentSiteMetadata = parentSiteMetadata,
                Sites = sites,
                SiteMetadata = siteMetadata
            };

            Stopwatch sw = Stopwatch.StartNew();
            siteJob.Schedule(default).Complete();
            sw.Stop();
            
            Debug.Log($"[Level {level}] Generated. Allocated: {totalAllocation} sites.");

            return (sites, siteMetadata);
        }
    }
}