using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using Random = Unity.Mathematics.Random;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct MultiLevelSiteGenerationJob : IJob
    {
        [ReadOnly] public NativeArray<LevelSettings> LevelSettings;
        [ReadOnly] public float2 MapSize;
        [ReadOnly] public int BaseSeed;
        [ReadOnly] public int ParentLevel;

        [ReadOnly] public NativeArray<float2> ParentSites;
        [ReadOnly] public NativeArray<VoronoiCell> ParentCells;
        [ReadOnly] public NativeArray<VoronoiSite> ParentSiteMetadata;

        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> SiteMetadata;
        
        public void Execute()
        {
            int level = ParentLevel + 1;
            LevelSettings settings = LevelSettings[level];

            if (ParentLevel == -1)
            {
                GenerateGlobalSites(level, settings);
            }
            else
            {
                if (ParentCells.IsCreated && ParentCells.Length > 0 &&
                    ParentSites.IsCreated && ParentSites.Length > 0 &&
                    ParentSiteMetadata.IsCreated && ParentSiteMetadata.Length > 0)
                {
                    GenerateChildSites(level, settings);
                }
                else
                {
                    Debug.Log("No parent data for level " + level + ", generating globally.");
                    GenerateGlobalSites(level, settings);
                }
            }
        }

        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            // Use a grid-based approach for L0 to ensure even distribution
            if (level == 0)
            {
                int gridCount = (int)math.ceil(math.sqrt(Sites.Length));
                float cellWidth = MapSize.x / gridCount;
                float cellHeight = MapSize.y / gridCount;

                for (int i = 0; i < Sites.Length; i++)
                {
                    int x = i % gridCount;
                    int y = i / gridCount;

                    uint randomSeed = (uint)(BaseSeed + i * 397);
                    if (randomSeed == 0) randomSeed = 1;
                    Random random = new Unity.Mathematics.Random(randomSeed);

                    float2 position = new float2(
                        x * cellWidth + cellWidth * 0.5f + random.NextFloat(-cellWidth * 0.1f, cellWidth * 0.1f),
                        y * cellHeight + cellHeight * 0.5f + random.NextFloat(-cellHeight * 0.1f, cellHeight * 0.1f)
                    );

                    float value = CalculateBaseValue(position, settings);

                    Sites[i] = position;
                    SiteMetadata[i] = new VoronoiSite
                    {
                        Position = position,
                        Index = i,
                        Level = level,
                        ParentIndex = -1,
                        Value = math.saturate(value)
                    };
                }
            }
            else
            {
                // Fallback to random for other levels if needed, though they should use GenerateChildSites
                for (int i = 0; i < Sites.Length; i++)
                {
                    uint randomSeed = (uint)(BaseSeed + i * 397);
                    if (randomSeed == 0) randomSeed = 1;
                    Random random = new Unity.Mathematics.Random(randomSeed);

                    float2 position = new float2(
                        random.NextFloat(0, MapSize.x),
                        random.NextFloat(0, MapSize.y)
                    );

                    float value = settings.ValueBias + SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale;

                    Sites[i] = position;
                    SiteMetadata[i] = new VoronoiSite
                    {
                        Position = position,
                        Index = i,
                        Level = level,
                        ParentIndex = -1,
                        Value = math.saturate(value)
                    };
                }
            }
        }

        private void GenerateChildSites(int level, LevelSettings settings)
        {
            int sitesGenerated = 0;

            for (int parentIndex = 0; parentIndex < ParentCells.Length; parentIndex++)
            {
                VoronoiCell parentCell = ParentCells[parentIndex];
                if (parentCell.Level != ParentLevel) continue;

                int cellSiteCount = CalculateCellSiteCount(parentCell, settings);

                for (int i = 0; i < cellSiteCount && sitesGenerated < Sites.Length; i++)
                {
                    float2 parentPosition = ParentSites[parentIndex];
                    float parentValue = ParentSiteMetadata[parentIndex].Value;

                    float2 position = GeneratePointInCell(parentPosition, parentIndex, i, settings, level);

                    float value = parentValue * 0.7f +
                                 SimplexNoise(position * 0.01f, BaseSeed + parentIndex * 100 + i) * 0.3f;

                    Sites[sitesGenerated] = position;
                    SiteMetadata[sitesGenerated] = new VoronoiSite
                    {
                        Position = position,
                        Index = sitesGenerated,
                        Level = level,
                        ParentIndex = parentIndex,
                        Value = math.saturate(value)
                    };

                    sitesGenerated++;
                }
            }

            // Fill remaining slots if necessary (shouldn't happen if SiteCount is correctly calculated)
            if (sitesGenerated < Sites.Length && level > 0)
            {
                for (int i = sitesGenerated; i < Sites.Length; i++)
                {
                    uint randomSeed = (uint)(BaseSeed + level * 1000 + i);
                    Random random = new Unity.Mathematics.Random(randomSeed);

                    float2 position = new float2(
                        random.NextFloat(0, MapSize.x),
                        random.NextFloat(0, MapSize.y)
                    );

                    Sites[i] = position;
                    SiteMetadata[i] = new VoronoiSite
                    {
                        Position = position,
                        Index = i,
                        Level = level,
                        ParentIndex = -1,
                        Value = 0.5f
                    };
                }
            }
        }

        private float2 GeneratePointInCell(float2 parentPosition, int parentIndex, int index, LevelSettings settings, int level)
        {
            int seed = BaseSeed ^ parentIndex ^ index;
            uint randomSeed = (uint)(seed);
            if (randomSeed == 0) randomSeed = 1;
            Random random = new Unity.Mathematics.Random(randomSeed);

            float scale;
            if (level == 0)
            {
                // For L0, use a very small offset to keep points close to grid positions
                scale = 10f; // Small fixed offset for initial grid stability
            }
            else
            {
                scale = settings.ScaleFactor * 50f;
            }

            float2 offset = new float2(
                random.NextFloat(-scale, scale),
                random.NextFloat(-scale, scale)
            );

            return parentPosition + offset;
        }

        private float CalculateBaseValue(float2 position, LevelSettings settings)
        {
            float continentNoise = SimplexNoise(position * 0.0001f, BaseSeed);
            float elevationBase = continentNoise * 0.8f + 0.2f;
            return math.saturate(elevationBase);
        }

        private int CalculateCellSiteCount(VoronoiCell parentCell, LevelSettings settings)
        {
            return (int)(settings.SiteCount * 0.1f);
        }

        private float SimplexNoise(float2 pos, int seed)
        {
            return noise.snoise(pos + new float2(seed));
        }
    }
}