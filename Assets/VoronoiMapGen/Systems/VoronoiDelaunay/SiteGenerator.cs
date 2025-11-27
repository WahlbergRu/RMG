using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;

namespace VoronoiMapGen.Systems
{
    public static class SiteGenerator
    {
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) Generate(
            MapSettings settings,
            in NativeArray<LevelSettings> levelSettingsNative,
            LevelSettings currentLevelSettings,
            int level,
            in NativeArray<VoronoiCell> parentCells,
            in NativeArray<float2> parentSites,
            in NativeArray<VoronoiSite> parentMeta,
            in NativeArray<HydrologyData> parentHydrology,
            in NativeArray<TectonicPlateData> parentTectonics,
            in NativeArray<ClimateData> parentClimate
            )
        {
            int totalCount;
            if (level == 0)
            {
                totalCount = currentLevelSettings.GlobalSiteCount;
            }
            else
            {
                if (!parentCells.IsCreated || parentCells.Length == 0) totalCount = 0;
                else totalCount = parentCells.Length * currentLevelSettings.MaxSiteCount;
            }

            var sites = new NativeArray<float2>(totalCount, Allocator.Persistent);
            var meta = new NativeArray<VoronoiSite>(totalCount, Allocator.Persistent);

            for (int i = 0; i < totalCount; i++)
            {
                sites[i] = new float2(-1, -1); 
                meta[i] = new VoronoiSite { Index = i, Value = -1, Level = level, ParentIndex = -1 };
            }

            var safeHydrology = parentHydrology.IsCreated ? parentHydrology : new NativeArray<HydrologyData>(0, Allocator.TempJob);
            var safeTectonics = parentTectonics.IsCreated ? parentTectonics : new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
            var safeClimate = parentClimate.IsCreated ? parentClimate : new NativeArray<ClimateData>(0, Allocator.TempJob);
            var safeParentCells = parentCells.IsCreated ? parentCells : new NativeArray<VoronoiCell>(0, Allocator.TempJob);
            var safeParentMeta = parentMeta.IsCreated ? parentMeta : new NativeArray<VoronoiSite>(0, Allocator.TempJob);

            new GenerateSitesJob
            {
                MapSize = settings.MapSize,
                Seed = settings.Seed + level * 1234,
                Level = level,
                Settings = currentLevelSettings,
                
                ParentCells = safeParentCells,
                ParentMeta = safeParentMeta,
                ParentHydrology = safeHydrology,
                ParentTectonics = safeTectonics,
                ParentClimate = safeClimate,
                
                Sites = sites,
                SiteMetadata = meta
            }.Schedule().Complete();

            if (!parentHydrology.IsCreated) safeHydrology.Dispose();
            if (!parentTectonics.IsCreated) safeTectonics.Dispose();
            if (!parentClimate.IsCreated) safeClimate.Dispose();
            if (!parentCells.IsCreated) safeParentCells.Dispose();
            if (!parentMeta.IsCreated) safeParentMeta.Dispose();

            return (sites, meta);
        }
    }

    [Unity.Burst.BurstCompile]
    public struct GenerateSitesJob : IJob
    {
        public float2 MapSize;
        public int Seed;
        public int Level;
        public LevelSettings Settings;

        [ReadOnly] public NativeArray<VoronoiCell> ParentCells;
        [ReadOnly] public NativeArray<VoronoiSite> ParentMeta;
        
        [ReadOnly] public NativeArray<HydrologyData> ParentHydrology;
        [ReadOnly] public NativeArray<TectonicPlateData> ParentTectonics;
        [ReadOnly] public NativeArray<ClimateData> ParentClimate;

        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> SiteMetadata;

        public void Execute()
        {
            var rng = new Unity.Mathematics.Random((uint)Seed);
            int index = 0;

            // --- L0: GLOBAL ---
            if (Level == 0)
            {
                int count = Settings.GlobalSiteCount;
                for (int i = 0; i < count; i++)
                {
                    if (index >= Sites.Length) break;
                    float2 pos = rng.NextFloat2(float2.zero, MapSize);
                    AddSite(index++, pos, -1, 0.5f);
                }
            }
            // --- L1+: CHILDREN ---
            else
            {
                for (int p = 0; p < ParentCells.Length; p++)
                {
                    if (index >= Sites.Length) break;

                    var pCell = ParentCells[p];
                    if (pCell.SiteIndex >= ParentMeta.Length) continue;
                    var pMeta = ParentMeta[pCell.SiteIndex];
                    if (pMeta.Value < -0.5f) continue;

                    // 1. Оценка пригодности (Suitability)
                    float suitability = 0.5f; 

                    if (ParentHydrology.Length > pCell.SiteIndex)
                    {
                        var hydro = ParentHydrology[pCell.SiteIndex];
                        var tect = ParentTectonics[pCell.SiteIndex];
                        var clim = ParentClimate[pCell.SiteIndex];

                        // Логика: В океане suitability низкая, у рек высокая.
                        // НО! Мы не обнуляем генерацию, мы просто меняем плотность.
                        if (hydro.IsOcean || tect.IsOcean) suitability = 0.0f;
                        else 
                        {
                            if (hydro.IsRiver) suitability += 0.3f;
                            if (hydro.IsLake) suitability += 0.2f;
                            float tempComfort = 1.0f - math.abs(clim.Temperature - 0.5f) * 2; 
                            suitability += tempComfort * 0.2f;
                        }
                    }

                    suitability = math.clamp(suitability, 0.0f, 1.0f);

                    // 2. Расчет количества (ПЛОТНОСТЬ)
                    // Важно: Гарантируем минимум MinSiteCount, даже если suitability 0.
                    // Это обеспечит сплошное покрытие.
                    int count = (int)math.lerp(Settings.MinSiteCount, Settings.MaxSiteCount, suitability);
                    
                    // Защита от дурака: если в настройках Min=0, ставим хотя бы 1, чтобы не было дыр
                    count = math.max(1, count); 
                    
                    // Центр и радиус разброса
                    float2 center = pCell.Centroid;
                    // ScaleFactor влияет на то, насколько широко дети разлетаются от центра родителя.
                    // 1.0 = на всю ячейку, 0.5 = кучкуются в центре.
                    // Для сплошного покрытия лучше ставить ближе к 0.8-1.0 в настройках.
                    float radius = 50.0f * Settings.ScaleFactor; 

                    for (int c = 0; c < count; c++)
                    {
                        if (index >= Sites.Length) break;

                        float2 dir = rng.NextFloat2Direction();
                        // Распределяем равномернее, чтобы заполнить пространство
                        float dist = math.sqrt(rng.NextFloat()) * radius;
                        
                        float2 pos = center + dir * dist;
                        pos = math.clamp(pos, new float2(1), MapSize - new float2(1));

                        AddSite(index++, pos, pCell.SiteIndex, suitability);
                    }
                }
            }
            
            // Финал
            for (int i = index; i < Sites.Length; i++)
            {
                Sites[i] = new float2(-999, -999);
                SiteMetadata[i] = new VoronoiSite { Index = i, Value = -1 };
            }
        }

        private void AddSite(int idx, float2 pos, int parentIdx, float val)
        {
            Sites[idx] = pos;
            SiteMetadata[idx] = new VoronoiSite
            {
                Index = idx,
                Position = pos,
                Level = Level,
                ParentIndex = parentIdx,
                Value = val
            };
        }
    }
}