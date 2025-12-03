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
            // Оцениваем макс. кол-во точек
            int parentCount = (parentCells.IsCreated) ? parentCells.Length : 1;
            int totalMaxCount = (level == 0) 
                ? currentLevelSettings.GlobalSiteCount 
                : parentCount * currentLevelSettings.MaxSiteCount;

            var sites = new NativeList<float2>(totalMaxCount, Allocator.Persistent);
            var meta = new NativeList<VoronoiSite>(totalMaxCount, Allocator.Persistent);

            var safeHydrology = parentHydrology.IsCreated ? parentHydrology : new NativeArray<HydrologyData>(0, Allocator.TempJob);
            var safeTectonics = parentTectonics.IsCreated ? parentTectonics : new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
            var safeClimate = parentClimate.IsCreated ? parentClimate : new NativeArray<ClimateData>(0, Allocator.TempJob);
            var safeParentCells = parentCells.IsCreated ? parentCells : new NativeArray<VoronoiCell>(0, Allocator.TempJob);
            var safeParentMeta = parentMeta.IsCreated ? parentMeta : new NativeArray<VoronoiSite>(0, Allocator.TempJob);

            new GenerateSitesJob
            {
                MapSize = settings.MapSize,
                Seed = settings.Seed + level * 54321,
                Level = level,
                Settings = currentLevelSettings,
                
                ParentCells = safeParentCells,
                ParentMeta = safeParentMeta,
                ParentHydrology = safeHydrology,
                ParentTectonics = safeTectonics,
                ParentClimate = safeClimate,
                
                OutSites = sites,
                OutMeta = meta
            }.Run(); // Используем Run (Main Thread) для простоты работы с NativeList.add, или можно переделать под параллель.
            // Для генерации точек Run вполне быстр.

            if (!parentHydrology.IsCreated) safeHydrology.Dispose();
            if (!parentTectonics.IsCreated) safeTectonics.Dispose();
            if (!parentClimate.IsCreated) safeClimate.Dispose();
            if (!parentCells.IsCreated) safeParentCells.Dispose();
            if (!parentMeta.IsCreated) safeParentMeta.Dispose();

            // Перегоняем в Array
            var sArray = sites.ToArray(Allocator.Persistent);
            var mArray = meta.ToArray(Allocator.Persistent);
            sites.Dispose();
            meta.Dispose();

            return (sArray, mArray);
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

        public NativeList<float2> OutSites;
        public NativeList<VoronoiSite> OutMeta;

        public void Execute()
        {
            var rng = new Unity.Mathematics.Random((uint)Seed);
            int globalIndex = 0;

            // --- СТРАТЕГИЯ ДЛЯ L0 (GLOBAL) ---
            if (Level == 0)
            {
                int targetCount = Settings.GlobalSiteCount;
                
                // Простая Poisson-подобная генерация
                // Генерируем кандидатов, выбираем лучшего (дальше всего от остальных)
                for (int i = 0; i < targetCount; i++)
                {
                    float2 bestPos = float2.zero;
                    float bestDist = -1f;

                    // Делаем 10 попыток найти хорошее место
                    for(int k=0; k<10; k++)
                    {
                        float2 candidate = rng.NextFloat2(new float2(10, 10), MapSize - new float2(10, 10));
                        float minDist = float.MaxValue;
                        
                        // Ищем дистанцию до ближайшего уже созданного
                        if (OutSites.Length == 0) minDist = float.MaxValue;
                        else
                        {
                            for(int s=0; s<OutSites.Length; s++)
                            {
                                float d = math.distancesq(candidate, OutSites[s]);
                                if (d < minDist) minDist = d;
                            }
                        }

                        if (minDist > bestDist)
                        {
                            bestDist = minDist;
                            bestPos = candidate;
                        }
                    }
                    AddSite(globalIndex++, bestPos, -1, 0.5f);
                }
            }
            // --- СТРАТЕГИЯ ДЛЯ L1/L2/L3 (Bi-level Constraints) ---
            else
            {
                for (int p = 0; p < ParentCells.Length; p++)
                {
                    var pCell = ParentCells[p];
                    if (pCell.SiteIndex >= ParentMeta.Length) continue;
                    var pMeta = ParentMeta[pCell.SiteIndex];
                    if (pMeta.Value < -0.5f) continue;

                    // Suitability (Пригодность)
                    float suitability = CalculateSuitability(pCell.SiteIndex);
                    
                    int count = (int)math.lerp(Settings.MinSiteCount, Settings.MaxSiteCount, suitability);
                    count = math.max(Settings.MinSiteCount > 0 ? Settings.MinSiteCount : 1, count);
                    
                    if (suitability < 0.01f && Settings.MinSiteCount == 0) continue;

                    // Расчет "Личного пространства" (Poisson radius)
                    // Чем больше точек хотим впихнуть в родителя, тем теснее им придется быть.
                    // Приблизительная площадь родителя ~ ScaleFactor.
                    // Радиус точки ~= sqrt(Area / count) * 0.7
                    
                    float2 center = pCell.Centroid;
                    float safeRadius = (50.0f * Settings.ScaleFactor) / math.sqrt(count); 
                    float minDistSq = safeRadius * safeRadius * 0.5f; // Чуть прощаем пересечения

                    int spawnedInCell = 0;
                    int attemptsTotal = 0;
                    
                    // Список локальных точек для быстрой проверки
                    // Используем цикл попыток "Dart Throwing" (Бросание дротиков)
                    
                    while(spawnedInCell < count && attemptsTotal < count * 20)
                    {
                        attemptsTotal++;
                        
                        // Генерируем точку внутри "описанного круга" родителя
                        // (Умножаем на ScaleFactor, чтобы заполнять углы)
                        float genRadius = 60.0f * Settings.ScaleFactor; // Условный радиус ячейки
                        if (Level == 2) genRadius = 15.0f * Settings.ScaleFactor; // Для L3 поменьше
                        if (Level >= 3) genRadius = 5.0f * Settings.ScaleFactor; 

                        // Лучше использовать "Box" распределение вокруг центра, оно лучше заполняет квадраты Вороного
                        float2 rndOffset = rng.NextFloat2(-genRadius, genRadius);
                        float2 candidate = center + rndOffset;

                        // Clamp to map
                        candidate = math.clamp(candidate, new float2(1), MapSize - new float2(1));

                        // Проверяем: не слишком ли близко к соседям В ЭТОЙ ЖЕ ячейке?
                        // (Проверять всех соседей на карте долго O(N^2), проверяем только последних добавленных)
                        bool isFree = true;
                        
                        // Простая эвристика: смотрим последние 'count' точек
                        int startCheck = math.max(0, OutSites.Length - spawnedInCell);
                        for(int k=startCheck; k<OutSites.Length; k++)
                        {
                            if (math.distancesq(candidate, OutSites[k]) < minDistSq)
                            {
                                isFree = false;
                                break;
                            }
                        }

                        if (isFree)
                        {
                            AddSite(globalIndex++, candidate, pCell.SiteIndex, suitability);
                            spawnedInCell++;
                        }
                    }
                }
            }
        }

        private float CalculateSuitability(int parentIdx)
        {
            float s = 0.5f;
            if (ParentHydrology.Length > parentIdx)
            {
                var h = ParentHydrology[parentIdx];
                var t = ParentTectonics[parentIdx];
                var c = ParentClimate[parentIdx];

                if (h.IsOcean || t.IsOcean) return 0.0f;
                
                if (h.IsRiver) s += 0.3f;
                float temp = 1.0f - math.abs(c.Temperature - 0.5f) * 2;
                s += temp * 0.2f;
            }
            return math.clamp(s, 0f, 1f);
        }

        private void AddSite(int idx, float2 pos, int parentIdx, float val)
        {
            OutSites.Add(pos);
            OutMeta.Add(new VoronoiSite
            {
                Index = idx,
                Position = pos,
                Level = Level,
                ParentIndex = parentIdx,
                Value = val
            });
        }
    }
}