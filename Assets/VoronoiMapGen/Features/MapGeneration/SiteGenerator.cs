using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration
{
    public static class SiteGenerator
    {
        // Обновленная сигнатура: 8 аргументов (убрали levelSettingsNative и parentSites)
        public static (NativeArray<float2> sites, NativeArray<VoronoiSite> siteMetadata) Generate(
            MapSettings settings,
            LevelSettings currentLevelSettings,
            int level,
            NativeArray<VoronoiCell> pCells,
            NativeArray<VoronoiSite> pMeta,
            NativeArray<HydrologyData> pHydro,
            NativeArray<TectonicPlateData> pTect,
            NativeArray<ClimateData> pClim
        )
        {
            int totalCount;
            if (level == 0)
            {
                totalCount = currentLevelSettings.GlobalSiteCount;
            }
            else
            {
                if (!pCells.IsCreated || pCells.Length == 0) totalCount = 0;
                else totalCount = pCells.Length * currentLevelSettings.MaxSiteCount;
            }

            NativeArray<float2> sites = new NativeArray<float2>(totalCount, Allocator.Persistent);
            NativeArray<VoronoiSite> meta = new NativeArray<VoronoiSite>(totalCount, Allocator.Persistent);

            // Инициализация -1
            InitializeArraysJob initJob = new InitializeArraysJob { Sites = sites, Meta = meta };
            initJob.Schedule(totalCount, 64).Complete();

            // Создаем безопасные алиасы для джобы (Unity Jobs требуют валидные массивы)
            NativeArray<VoronoiCell> safeCells = CreateSafeAlias(pCells, out bool disposeCells);
            NativeArray<VoronoiSite> safeMeta = CreateSafeAlias(pMeta, out bool disposeMeta);
            NativeArray<HydrologyData> safeHydro = CreateSafeAlias(pHydro, out bool disposeHydro);
            NativeArray<TectonicPlateData> safeTect = CreateSafeAlias(pTect, out bool disposeTect);
            NativeArray<ClimateData> safeClim = CreateSafeAlias(pClim, out bool disposeClim);

            try
            {
                new GenerateSitesJob
                {
                    MapSize = settings.MapSize,
                    Seed = settings.Seed + level * 1234,
                    Level = level,
                    Settings = currentLevelSettings,

                    ParentCells = safeCells,
                    ParentMeta = safeMeta,
                    ParentHydrology = safeHydro,
                    ParentTectonics = safeTect,
                    ParentClimate = safeClim,

                    Sites = sites,
                    SiteMetadata = meta
                }.Schedule().Complete();
            }
            finally
            {
                if (disposeCells) safeCells.Dispose();
                if (disposeMeta) safeMeta.Dispose();
                if (disposeHydro) safeHydro.Dispose();
                if (disposeTect) safeTect.Dispose();
                if (disposeClim) safeClim.Dispose();
            }

            return (sites, meta);
        }

        private static NativeArray<T> CreateSafeAlias<T>(NativeArray<T> source, out bool createdNew) where T : struct
        {
            if (source.IsCreated)
            {
                createdNew = false;
                return source;
            }

            createdNew = true;
            return new NativeArray<T>(0, Allocator.TempJob);
        }
    }

    [BurstCompile]
    internal struct InitializeArraysJob : IJobParallelFor
    {
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> Meta;

        public void Execute(int i)
        {
            Sites[i] = new float2(-9999, -9999);
            Meta[i] = new VoronoiSite { Index = i, Value = -1 };
        }
    }

    // --- ВОТ ОПРЕДЕЛЕНИЕ ДЖОБЫ, КОТОРОЕ БЫЛО ПОТЕРЯНО ---
    [BurstCompile]
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
            Random rng = new Random((uint)Seed);
            int index = 0;
            NativeList<float2> localPoints = new NativeList<float2>(64, Allocator.Temp);

            // --- L0: GLOBAL ---
            if (Level == 0)
            {
                int count = Settings.GlobalSiteCount;
                localPoints.Clear();

                for (int i = 0; i < count; i++)
                {
                    if (index >= Sites.Length) break;
                    // Для глобального уровня ищем по всей карте (0,0) -> MapSize
                    float2 pos = GetBestCandidate(ref rng, localPoints, float2.zero, MapSize, 10, true);
                    localPoints.Add(pos);
                    AddSite(index++, pos, -1, 0.5f);
                }
            }
            // --- L1+: CHILDREN ---
            else
            {
                float totalArea = MapSize.x * MapSize.y;
                int parentCount = math.max(1, ParentCells.Length);
                float avgParentRadius = math.sqrt(totalArea / parentCount / math.PI);
                float spawnRadius = avgParentRadius * Settings.ScaleFactor;

                for (int p = 0; p < ParentCells.Length; p++)
                {
                    if (index >= Sites.Length) break;

                    VoronoiCell pCell = ParentCells[p];
                    if (pCell.SiteIndex >= ParentMeta.Length) continue;
                    VoronoiSite pMeta = ParentMeta[pCell.SiteIndex];
                    // Пропускаем "призраков"
                    if (pMeta.Value < -0.5f) continue;

                    // Расчет пригодности (Suitability) для спавна детей
                    float suitability = 0.5f;
                    if (ParentHydrology.Length > pCell.SiteIndex)
                    {
                        HydrologyData hydro = ParentHydrology[pCell.SiteIndex];
                        TectonicPlateData tect = ParentTectonics[pCell.SiteIndex];
                        ClimateData clim = ParentClimate[pCell.SiteIndex];

                        if (hydro.IsOcean || tect.IsOcean)
                        {
                            suitability = 0.0f; // В океане меньше детализация
                        }
                        else
                        {
                            if (hydro.IsRiver) suitability += 0.3f;
                            if (hydro.IsLake) suitability += 0.2f;
                            float tempComfort = 1.0f - math.abs(clim.Temperature - 0.5f) * 2;
                            suitability += tempComfort * 0.2f;
                        }
                    }

                    suitability = math.clamp(suitability, 0.0f, 1.0f);

                    int targetCount = (int)math.lerp(Settings.MinSiteCount, Settings.MaxSiteCount, suitability);
                    // Минимум 1 ребенок, если не океан
                    if (targetCount == 0 && Settings.MinSiteCount > 0) targetCount = 1;
                    if (suitability < 0.01f && Settings.MinSiteCount == 0) targetCount = 0;

                    localPoints.Clear();
                    float2 center = pCell.Centroid;

                    for (int c = 0; c < targetCount; c++)
                    {
                        if (index >= Sites.Length) break;
                        float2 bestPos = GetBestCandidate(ref rng, localPoints, center, MapSize, 8, false, spawnRadius);
                        localPoints.Add(bestPos);
                        AddSite(index++, bestPos, pCell.SiteIndex, suitability);
                    }
                }
            }

            localPoints.Dispose();
        }

        private float2 GetBestCandidate(ref Random rng, NativeList<float2> existingPoints, float2 center,
            float2 mapSize, int attempts, bool globalMode, float radius = 0)
        {
            float2 bestCandidate = float2.zero;
            float maxDist = -1.0f;

            if (existingPoints.Length == 0) return GeneratePoint(ref rng, center, mapSize, globalMode, radius);

            for (int k = 0; k < attempts; k++)
            {
                float2 candidate = GeneratePoint(ref rng, center, mapSize, globalMode, radius);

                float distToClosest = float.MaxValue;
                for (int i = 0; i < existingPoints.Length; i++)
                {
                    float d = math.distancesq(candidate, existingPoints[i]);
                    if (d < distToClosest) distToClosest = d;
                }

                if (distToClosest > maxDist)
                {
                    maxDist = distToClosest;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate;
        }

        private float2 GeneratePoint(ref Random rng, float2 center, float2 mapSize, bool global, float radius)
        {
            if (global) return rng.NextFloat2(new float2(10), mapSize - new float2(10));

            float2 dir = rng.NextFloat2Direction();
            float dist = math.sqrt(rng.NextFloat()) * radius;
            return math.clamp(center + dir * dist, new float2(1), mapSize - new float2(1));
        }

        private void AddSite(int idx, float2 pos, int parentIdx, float val)
        {
            Sites[idx] = pos;
            SiteMetadata[idx] = new VoronoiSite
            {
                Index = idx, Position = pos, Level = Level, ParentIndex = parentIdx, Value = val
            };
        }
    }
}