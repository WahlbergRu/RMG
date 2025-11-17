// using Unity.Burst; // УБРАНО
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine; // Для Debug.Log
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using Random = Unity.Mathematics.Random;

namespace VoronoiMapGen.Jobs
{
    // [BurstCompile] // УБРАНО
    public struct MultiLevelSiteGenerationJob : IJob
    {
        [ReadOnly] public NativeArray<LevelSettings> LevelSettings;
        [ReadOnly] public float2 MapSize;
        [ReadOnly] public int BaseSeed;
        [ReadOnly] public int ParentLevel;

        // Добавьте это:
        [ReadOnly] public NativeArray<float2> ParentSites; // Позиции родительских точек (Persistent или Temp)
        [ReadOnly] public NativeArray<VoronoiCell> ParentCells; // (Persistent или Temp)
        [ReadOnly] public NativeArray<VoronoiSite> ParentSiteMetadata; // <<< НОВОЕ: Метаданные родительских точек

        public NativeArray<float2> Sites; // Целевой массив (Persistent)
        public NativeArray<VoronoiSite> SiteMetadata; // Целевой массив (Persistent)

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
                    ParentSiteMetadata.IsCreated && ParentSiteMetadata.Length > 0) // <<< ПРОВЕРКА parentSiteMetadata (НОВОЕ)
                {
                    GenerateChildSites(level, settings);
                }
                else
                {
                    Debug.Log("Error: No parent sites, cells, or site metadata for level " + level); // <<< ИЗМЕНЕНО сообщение
                    GenerateGlobalSites(level, settings); // Резервная логика
                }
            }
        }


        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            for (int i = 0; i < Sites.Length; i++)
            {
                uint randomSeed = (uint)(BaseSeed + i * 397);
                if (randomSeed == 0) randomSeed = 1;
                Random random = new Unity.Mathematics.Random(randomSeed);

                float2 position = new float2(
                    random.NextFloat(0, MapSize.x),
                    random.NextFloat(0, MapSize.y)
                );

                float value = level == 0
                    ? CalculateBaseValue(position, settings)
                    : settings.ValueBias + SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale;

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
                    // <<< НОВОЕ: Используем ParentSiteMetadata[parentIndex] для получения данных родителя >>>
                    float parentValue = ParentSiteMetadata[parentIndex].Value; // <<< ИЗМЕНЕНО (НОВОЕ)

                    float2 position = GeneratePointInCell(parentPosition, parentIndex, i, settings);

                    // float parentValue = GetParentCellValue(parentIndex); // <<< УБРАНО (НОВОЕ)
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

            if (sitesGenerated < Sites.Length)
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

        // --- ИЗМЕНЕНО: Принимает parentPosition, а не cell ---
        private float2 GeneratePointInCell(float2 parentPosition, int parentIndex, int index, LevelSettings settings)
        {
            int seed = BaseSeed ^ parentIndex ^ index;
            uint randomSeed = (uint)(seed);
            if (randomSeed == 0) randomSeed = 1;
            Random random = new Unity.Mathematics.Random(randomSeed);

            // Масштабируем offset в зависимости от уровня
            float scale = settings.ScaleFactor * 50f; // Уменьшайте ScaleFactor для более глубоких уровней

            float2 offset = new float2(
                random.NextFloat(-scale, scale),
                random.NextFloat(-scale, scale)
            );

            return parentPosition + offset;
        }

        // <<< УБРАНО: private float GetParentCellValue(int parentIndex) >>>

        private float SimplexNoise(float2 pos, int seed)
        {
            return noise.snoise(pos + new float2(seed));
        }
    }
}