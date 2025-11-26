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
            in NativeArray<VoronoiSite> parentMeta)
        {
            // 1. Расчет количества точек
            int totalCount;
            if (level == 0)
            {
                totalCount = currentLevelSettings.GlobalSiteCount;
            }
            else
            {
                totalCount = parentCells.Length * currentLevelSettings.MaxSiteCount;
            }

            // Выделяем память под результат
            var sites = new NativeArray<float2>(totalCount, Allocator.Persistent);
            var meta = new NativeArray<VoronoiSite>(totalCount, Allocator.Persistent);

            // Инициализация
            for (int i = 0; i < totalCount; i++)
            {
                sites[i] = new float2(-1, -1); 
                meta[i] = new VoronoiSite { Index = i, Value = -1, Level = level, ParentIndex = -1 };
            }

            // === ИСПРАВЛЕНИЕ ОШИБКИ UNKNOWN_OBJECT_TYPE ===
            // Unity требует, чтобы все NativeArray в джобе были инициализированы.
            // Для Level 0 у нас нет родителей, поэтому мы создаем временные "пустышки".
            
            NativeArray<VoronoiCell> safeParentCells = parentCells;
            bool disposeParentCells = false;
            if (!safeParentCells.IsCreated)
            {
                safeParentCells = new NativeArray<VoronoiCell>(0, Allocator.TempJob);
                disposeParentCells = true;
            }

            NativeArray<VoronoiSite> safeParentMeta = parentMeta;
            bool disposeParentMeta = false;
            if (!safeParentMeta.IsCreated)
            {
                safeParentMeta = new NativeArray<VoronoiSite>(0, Allocator.TempJob);
                disposeParentMeta = true;
            }
            // ==============================================

            // 2. Запуск Джобы
            new GenerateSitesJob
            {
                MapSize = settings.MapSize,
                Seed = settings.Seed + level * 1234,
                Level = level,
                Settings = currentLevelSettings,
                
                ParentCells = safeParentCells, // Передаем безопасную версию
                ParentMeta = safeParentMeta,   // Передаем безопасную версию
                
                Sites = sites,
                SiteMetadata = meta
            }.Schedule().Complete();

            // Очистка временных заглушек
            if (disposeParentCells) safeParentCells.Dispose();
            if (disposeParentMeta) safeParentMeta.Dispose();

            return (sites, meta);
        }
    }

    // Джоба осталась без изменений, но привожу для целостности
    [Unity.Burst.BurstCompile]
    public struct GenerateSitesJob : IJob
    {
        public float2 MapSize;
        public int Seed;
        public int Level;
        public LevelSettings Settings;

        [ReadOnly] public NativeArray<VoronoiCell> ParentCells;
        [ReadOnly] public NativeArray<VoronoiSite> ParentMeta;

        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> SiteMetadata;

        public void Execute()
        {
            var rng = new Unity.Mathematics.Random((uint)Seed);
            int index = 0;

            if (Level == 0)
            {
                int count = Settings.GlobalSiteCount;
                for (int i = 0; i < count; i++)
                {
                    if (index >= Sites.Length) break;
                    
                    float2 pos = rng.NextFloat2(float2.zero, MapSize);
                    float noiseVal = noise.snoise(pos * 0.002f + new float2(Seed));
                    float value = Settings.ValueBias + noiseVal * Settings.ValueScale;

                    AddSite(index++, pos, -1, math.saturate(value));
                }
            }
            else
            {
                for (int p = 0; p < ParentCells.Length; p++)
                {
                    if (index >= Sites.Length) break;

                    var pCell = ParentCells[p];
                    // Здесь безопасно обращаться к ParentMeta, так как для Level > 0 он будет валидным
                    // А если Level == 0, мы сюда не попадем, но Job System требовал инициализации
                    var pMeta = ParentMeta[pCell.SiteIndex];

                    if (pMeta.Value < 0) continue; 

                    var pRng = Unity.Mathematics.Random.CreateFromIndex((uint)(Seed ^ p * 397));
                    if (pRng.NextFloat() < Settings.EmptyCellChance) continue;

                    int count = (int)math.lerp(Settings.MinSiteCount, Settings.MaxSiteCount, pMeta.Value);
                    count = math.max(1, count);

                    float2 center = pCell.Centroid;
                    float radius = 50.0f * Settings.ScaleFactor;

                    for (int c = 0; c < count; c++)
                    {
                        if (index >= Sites.Length) break;

                        float2 dir = pRng.NextFloat2Direction();
                        float dist = math.sqrt(pRng.NextFloat()) * radius;
                        float2 pos = center + dir * dist;
                        pos = math.clamp(pos, new float2(0.1f), MapSize - new float2(0.1f));

                        float val = pMeta.Value * 0.9f + pRng.NextFloat(0, 0.1f);
                        AddSite(index++, pos, pCell.SiteIndex, val);
                    }
                }
            }
            
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