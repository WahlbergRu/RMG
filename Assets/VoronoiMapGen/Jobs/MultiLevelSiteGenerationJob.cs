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

        // --- L0 GENERATION (Как и было) ---
        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            int currentIndex = 0;
            int totalSites = Sites.Length; // Size calculated in SiteGenerator

            // 1. GHOST WALL
            float wallDistance = math.min(MapSize.x, MapSize.y) * 0.5f; 
            int ghostsPerSide = (int)(math.sqrt(settings.SiteCount) * 0.8f); 
            ghostsPerSide = math.clamp(ghostsPerSide, 5, 30);

            // Corners
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

            // Sides
            int ghostLimit = currentIndex + (ghostsPerSide * 4);
            if (ghostLimit < totalSites)
            {
                for (int i = 0; i < ghostsPerSide; i++)
                {
                    float t = (float)i / (ghostsPerSide - 1);
                    AddSite(currentIndex++, new float2(math.lerp(0, MapSize.x, t), -wallDistance), level, settings, true);
                    AddSite(currentIndex++, new float2(math.lerp(0, MapSize.x, t), MapSize.y + wallDistance), level, settings, true);
                    AddSite(currentIndex++, new float2(-wallDistance, math.lerp(0, MapSize.y, t)), level, settings, true);
                    AddSite(currentIndex++, new float2(MapSize.x + wallDistance, math.lerp(0, MapSize.y, t)), level, settings, true);
                }
            }

            // 2. INTERNAL FILL
            float padding = math.min(MapSize.x, MapSize.y) * 0.02f;
            for (int i = currentIndex; i < totalSites; i++)
            {
                uint randomSeed = (uint)(BaseSeed + i * 92834);
                Random random = new Random(randomSeed);
                float2 position = new float2(
                    random.NextFloat(padding, MapSize.x - padding),
                    random.NextFloat(padding, MapSize.y - padding)
                );
                AddSite(i, position, level, settings, false);
            }
        }

        // --- L1+ GENERATION (Исправлено) ---
        private void GenerateChildSites(int level, LevelSettings settings)
        {
            int currentIndex = 0;
            
            // 1. КОПИРУЕМ ПРИЗРАКОВ ОТ РОДИТЕЛЯ
            // Это критически важно, чтобы сохранить квадратную форму карты
            int realParentCount = 0;
            
            for (int i = 0; i < ParentSiteMetadata.Length; i++)
            {
                var pMeta = ParentSiteMetadata[i];
                
                if (pMeta.Value < -0.5f) // Это призрак
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
                            Value = -1.0f // Остается призраком
                        };
                        currentIndex++;
                    }
                }
                else
                {
                    realParentCount++;
                }
            }

            // 2. ГЕНЕРИРУЕМ ДЕТЕЙ ВНУТРИ РЕАЛЬНЫХ ЯЧЕЕК
            // Мы делим запрошенное количество точек (например 50) на количество реальных родителей.
            if (realParentCount == 0) realParentCount = 1;
            
            int childrenPerParent = settings.SiteCount / realParentCount;
            // Гарантируем хотя бы 1 ребенка, если настройки кривые, но не меньше 0
            if (childrenPerParent < 1 && settings.SiteCount > 0) childrenPerParent = 1;

            int sitesGenerated = currentIndex; // Начинаем после призраков
            
            for (int pIdx = 0; pIdx < ParentCells.Length; pIdx++)
            {
                // Пропускаем призраков
                if (ParentSiteMetadata[pIdx].Value < -0.5f) continue;
                
                // Пропускаем если ячейка не того уровня (на всякий случай)
                if (ParentCells[pIdx].Level != ParentLevel) continue;

                // Генерируем N детей для этого родителя
                for (int c = 0; c < childrenPerParent && sitesGenerated < Sites.Length; c++)
                {
                    float2 parentPos = ParentSites[pIdx];
                    float2 childPos = GeneratePointInCell(parentPos, pIdx, c, settings);
                    
                    // Кламп обязателен
                    childPos = math.clamp(childPos, new float2(0.1f), MapSize - new float2(0.1f));

                    // Наследуем Value от родителя + шум
                    float parentVal = ParentSiteMetadata[pIdx].Value;
                    float childVal = parentVal * 0.8f + SimplexNoise(childPos * 0.05f, BaseSeed + sitesGenerated) * 0.2f;

                    Sites[sitesGenerated] = childPos;
                    SiteMetadata[sitesGenerated] = new VoronoiSite
                    {
                        Position = childPos,
                        Index = sitesGenerated,
                        Level = level,
                        ParentIndex = pIdx,
                        Value = math.saturate(childVal)
                    };
                    sitesGenerated++;
                }
            }

            // 3. ДОБИВКА (если из-за округления осталось место)
            while (sitesGenerated < Sites.Length)
            {
                uint seed = (uint)(BaseSeed + level * 555 + sitesGenerated);
                Random rnd = new Random(seed);
                float2 pos = new float2(rnd.NextFloat(0, MapSize.x), rnd.NextFloat(0, MapSize.y));
                
                Sites[sitesGenerated] = pos;
                SiteMetadata[sitesGenerated] = new VoronoiSite
                {
                    Position = pos,
                    Index = sitesGenerated,
                    Level = level,
                    ParentIndex = -1,
                    Value = 0.5f
                };
                sitesGenerated++;
            }
        }

        // --- Helpers ---
        private void AddSite(int index, float2 position, int level, LevelSettings settings, bool isGhost)
        {
            if (index >= Sites.Length) return;
            float value = isGhost ? 0 : (settings.ValueBias + SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale);
            Sites[index] = position;
            SiteMetadata[index] = new VoronoiSite { Position = position, Index = index, Level = level, ParentIndex = -1, Value = isGhost ? -1.0f : math.saturate(value) };
        }

        private float2 GeneratePointInCell(float2 parentPosition, int parentIndex, int index, LevelSettings settings)
        {
             uint randomSeed = (uint)(BaseSeed ^ (parentIndex * 397) ^ index);
             if (randomSeed == 0) randomSeed = 1;
             Random random = new Random(randomSeed);
             
             // Радиус разброса зависит от ScaleFactor (уменьшаем его для дочерних)
             float mapScale = math.min(MapSize.x, MapSize.y);
             float scale = mapScale * settings.ScaleFactor * 0.04f; 
             
             float angle = random.NextFloat(0, math.PI * 2);
             float dist = math.sqrt(random.NextFloat(0, 1)) * scale;
             return parentPosition + new float2(math.cos(angle), math.sin(angle)) * dist;
        }

        
        private float SimplexNoise(float2 pos, int seed) => noise.snoise(pos + new float2(seed));
    }
}