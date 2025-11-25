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
                GenerateChildSites(level, settings);
            }
        }

        // --- L0 GENERATION ---
        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            int currentIndex = 0;
            
            // 1. GHOST WALL
            float wallDistance = math.min(MapSize.x, MapSize.y) * 0.5f; 
            int ghostsPerSide = (int)(math.sqrt(settings.SiteCount) * 0.8f); 
            ghostsPerSide = math.clamp(ghostsPerSide, 5, 30);

            // Corners & Walls logic (Standard)
            AddSite(currentIndex++, new float2(-wallDistance, -wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x + wallDistance, -wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(-wallDistance, MapSize.y + wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x + wallDistance, MapSize.y + wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(-wallDistance, 0), level, settings, true);
            AddSite(currentIndex++, new float2(0, -wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x + wallDistance, 0), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x, -wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(-wallDistance, MapSize.y), level, settings, true);
            AddSite(currentIndex++, new float2(0, MapSize.y + wallDistance), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x + wallDistance, MapSize.y), level, settings, true);
            AddSite(currentIndex++, new float2(MapSize.x, MapSize.y + wallDistance), level, settings, true);

            for (int i = 0; i < ghostsPerSide; i++)
            {
                float t = (float)i / (ghostsPerSide - 1);
                if (currentIndex < Sites.Length) AddSite(currentIndex++, new float2(math.lerp(0, MapSize.x, t), -wallDistance), level, settings, true);
                if (currentIndex < Sites.Length) AddSite(currentIndex++, new float2(math.lerp(0, MapSize.x, t), MapSize.y + wallDistance), level, settings, true);
                if (currentIndex < Sites.Length) AddSite(currentIndex++, new float2(-wallDistance, math.lerp(0, MapSize.y, t)), level, settings, true);
                if (currentIndex < Sites.Length) AddSite(currentIndex++, new float2(MapSize.x + wallDistance, math.lerp(0, MapSize.y, t)), level, settings, true);
            }

            // 2. INTERNAL FILL
            int maxSites = currentIndex + settings.SiteCount;
            if (maxSites > Sites.Length) maxSites = Sites.Length;

            float padding = math.min(MapSize.x, MapSize.y) * 0.02f;
            
            for (int i = currentIndex; i < maxSites; i++)
            {
                uint randomSeed = (uint)(BaseSeed + i * 92834);
                Random random = new Random(randomSeed);
                float2 position = new float2(
                    random.NextFloat(padding, MapSize.x - padding),
                    random.NextFloat(padding, MapSize.y - padding)
                );
                AddSite(i, position, level, settings, false);
            }
            
            // Fill remaining
            for (int i = maxSites; i < Sites.Length; i++)
            {
                 Sites[i] = new float2(-10000, -10000); 
                 SiteMetadata[i] = new VoronoiSite { Index = i, Value = -1 };
            }
        }

        // --- L1+ GENERATION ---
        private void GenerateChildSites(int level, LevelSettings settings)
        {
            int currentIndex = 0;
            
            // 1. Считаем РЕАЛЬНЫХ родителей для правильного радиуса
            int realParentCount = 0;
            for (int i = 0; i < ParentSiteMetadata.Length; i++)
            {
                if (ParentSiteMetadata[i].Value > -0.5f) realParentCount++;
            }
            if (realParentCount < 1) realParentCount = 1;

            // 2. Рассчитываем средний радиус родителя (только по площади карты)
            float mapArea = MapSize.x * MapSize.y;
            float avgParentArea = mapArea / realParentCount; 
            float avgParentRadius = math.sqrt(avgParentArea / math.PI);

            // 3. КОПИРУЕМ ПРИЗРАКОВ
            for (int i = 0; i < ParentSiteMetadata.Length; i++)
            {
                var pMeta = ParentSiteMetadata[i];
                if (pMeta.Value < -0.5f) 
                {
                    if (currentIndex < Sites.Length)
                    {
                        Sites[currentIndex] = ParentSites[i];
                        SiteMetadata[currentIndex] = new VoronoiSite
                        {
                            Position = ParentSites[i],
                            Index = currentIndex,
                            Level = level,
                            ParentIndex = i,
                            Value = -1.0f 
                        };
                        currentIndex++;
                    }
                }
            }

            // 4. ГЕНЕРИРУЕМ ДЕТЕЙ
            int childrenPerParent = settings.SiteCount; 
            
            for (int pIdx = 0; pIdx < ParentCells.Length; pIdx++)
            {
                if (ParentSiteMetadata[pIdx].Value < -0.5f) continue;
                if (ParentCells[pIdx].Level != ParentLevel) continue;

                float2 parentCentroid = ParentCells[pIdx].Centroid;
                
                for (int c = 0; c < childrenPerParent; c++)
                {
                    if (currentIndex >= Sites.Length) break;

                    float2 childPos = GeneratePointInCell(parentCentroid, pIdx, c, settings, avgParentRadius);
                    childPos = math.clamp(childPos, new float2(0.1f), MapSize - new float2(0.1f));

                    float parentVal = ParentSiteMetadata[pIdx].Value;
                    float childVal = parentVal * 0.9f + SimplexNoise(childPos * 0.1f, BaseSeed + currentIndex) * 0.1f;

                    Sites[currentIndex] = childPos;
                    SiteMetadata[currentIndex] = new VoronoiSite
                    {
                        Position = childPos,
                        Index = currentIndex,
                        Level = level,
                        ParentIndex = pIdx,
                        Value = math.saturate(childVal)
                    };
                    currentIndex++;
                }
            }
            
            for (int i = currentIndex; i < Sites.Length; i++)
            {
                Sites[i] = new float2(-10000, -10000);
                SiteMetadata[i] = new VoronoiSite { Index = i, Value = -1 };
            }
        }

        private void AddSite(int index, float2 position, int level, LevelSettings settings, bool isGhost)
        {
            if (index >= Sites.Length) return;
            float value = isGhost ? 0 : (settings.ValueBias + SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale);
            Sites[index] = position;
            SiteMetadata[index] = new VoronoiSite { 
                Position = position, 
                Index = index, 
                Level = level, 
                ParentIndex = -1, 
                Value = isGhost ? -1.0f : math.saturate(value) 
            };
        }

        private float2 GeneratePointInCell(float2 parentPosition, int parentIndex, int index, LevelSettings settings, float baseRadius)
        {
             uint randomSeed = (uint)(BaseSeed ^ (parentIndex * 73856093) ^ (index * 19349663));
             Random random = new Random(randomSeed);
             
             // Исправленный разброс:
             // baseRadius теперь правильный (большой).
             // Умножаем на 1.5, чтобы точки слегка заходили на соседние ячейки (создавая перекрытие).
             float radius = baseRadius * settings.ScaleFactor * 1.5f; 
             
             float angle = random.NextFloat(0, math.PI * 2);
             float dist = math.sqrt(random.NextFloat(0, 1)) * radius; 
             
             return parentPosition + new float2(math.cos(angle), math.sin(angle)) * dist;
        }

        private float SimplexNoise(float2 pos, int seed) => noise.snoise(pos + new float2(seed));
    }
}