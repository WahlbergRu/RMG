using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration
{
    // --- L0-L3: TECTONICS & INHERITANCE ---
    [BurstCompile]
    public struct TectonicGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        public int Level;

        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMeta;
        [ReadOnly] public NativeArray<TectonicPlateData> ParentTectonics;

        public NativeArray<TectonicPlateData> TectonicData;

        public void Execute(int i)
        {
            float2 pos = Sites[i];

            float baseHeight = 0;
            bool isOcean = false;

            // --- L0 (GLOBAL) ---
            if (Level == 0)
            {
                float2 center = MapSize * 0.5f;
                float dist = math.distance(pos, center);
                float maxRadius = math.min(MapSize.x, MapSize.y) * 0.45f;
                float distPercent = math.clamp(dist / maxRadius, 0f, 1f);

                // 1. Форма острова (Купол)
                // Делаем купол чуть выше, чтобы был запас для гор
                float islandShape = (1.0f - math.pow(distPercent, 1.5f)) * 1.5f - 0.3f;

                // 2. Базовый ландшафт (Низкочастотные холмы)
                float baseNoise = noise.snoise(pos * 0.0004f + new float2(Seed * 0.1f));

                // 3. --- ГОРЫ (Ridged Noise) ---
                // Создаем острые пики. "1 - abs(noise)" делает острые верхушки.
                float mountainFreq = 0.0012f;
                float mountainRaw = noise.snoise(pos * mountainFreq + new float2(Seed * 0.5f));
                float ridgedMountains = 1.0f - math.abs(mountainRaw); // Острые гребни
                ridgedMountains = math.pow(ridgedMountains, 2.5f); // Делаем пики более выраженными

                // Добавляем горы только ближе к центру (где islandShape высокий)
                // smoothstep(0.2, 0.8, ...) означает, что горы растут, начиная с 20% высоты острова.
                float mountainMask = math.smoothstep(0.2f, 0.8f, islandShape);

                // Складываем базу и горы.
                // 0.6f силы гор + 0.3f базового шума + форма острова
                baseHeight = islandShape + baseNoise * 0.3f + ridgedMountains * 0.9f * mountainMask;

                // 4. --- ЭРОЗИЯ / ПРОЛИВЫ (Carving) ---

                // Шум для каньонов
                float valleyFreq = 0.002f;
                float valleyRaw = noise.snoise(pos * valleyFreq + new float2(Seed * 0.9f));
                float valleyFactor = 1.0f - math.abs(valleyRaw);
                valleyFactor = math.pow(valleyFactor, 4.0f); // Узкие глубокие ущелья

                // Режем только там, где уже высоко (чтобы не дырявить берега)
                float carveMask = math.smoothstep(0.1f, 0.6f, baseHeight);
                float carveStrength = 0.7f; // Глубокий разрез

                if (baseHeight > 0.05f)
                    // Вычитаем каньон.
                    // Т.к. мы подняли горы (в шаге 3), теперь вычитание не убьет их полностью,
                    // а просто создаст разрыв между двумя высокими пиками.
                    baseHeight -= valleyFactor * carveStrength * carveMask;

                // 5. Обработка берегов и океана
                if (distPercent < 0.7f && baseNoise < -0.2f) baseHeight *= 0.8f;

                // Океан
                isOcean = baseHeight < 0.08f;
            }
            // --- L1+ (CHILDREN) ---
            else
            {
                int parentIdx = SiteMeta[i].ParentIndex;

                if (ParentTectonics.Length > 0 && parentIdx >= 0 && parentIdx < ParentTectonics.Length)
                {
                    TectonicPlateData parentData = ParentTectonics[parentIdx];

                    if (parentData.IsOcean)
                    {
                        isOcean = true;
                        baseHeight = -0.5f;
                    }
                    else
                    {
                        // Детализация
                        float freq = 0.002f * math.pow(3.0f, Level);
                        float detail = noise.snoise(pos * freq + new float2(Seed * 0.3f));
                        float amp = 0.2f / Level; // Чуть больше деталей

                        // Локальная эрозия (мелкие овраги на склонах)
                        float localValley = 1.0f - math.abs(noise.snoise(pos * (freq * 1.5f) + new float2(Seed * 0.8f)));
                        localValley = math.pow(localValley, 3.0f) * (0.25f / Level);

                        // Наследуем высоту родителя + детали
                        baseHeight = parentData.BaseHeight + detail * amp - localValley;

                        // Фьорды / Горные озера
                        if (baseHeight < 0.05f)
                        {
                            // Если родитель был очень высоким (>0.4), это глубокое ущелье (озеро), а не море
                            if (parentData.BaseHeight > 0.4f)
                            {
                                if (baseHeight < 0.01f) baseHeight = 0.01f;
                            }
                            else
                            {
                                isOcean = baseHeight <= 0.001f;
                            }
                        }
                        else
                        {
                            isOcean = false;
                        }
                    }
                }
                else
                {
                    baseHeight = 0;
                    isOcean = true;
                }
            }

            TectonicData[i] = new TectonicPlateData
            {
                IsOcean = isOcean,
                Velocity = float2.zero,
                BaseHeight = baseHeight,
                CrustAge = 0
            };
        }
    }

    // --- L1: CLIMATE ---
    [BurstCompile]
    public struct ClimateGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        public int Level;

        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMeta;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        [ReadOnly] public NativeArray<ClimateData> ParentClimate;

        public NativeArray<ClimateData> Climate;
        public NativeArray<BiomeData> Biomes;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            TectonicPlateData plate = Tectonics[i];
            float height = plate.BaseHeight;

            float temp = 0.5f;
            float moisture = 0.5f;

            if (Level > 0 && ParentClimate.Length > 0)
            {
                int pIdx = SiteMeta[i].ParentIndex;
                if (pIdx >= 0 && pIdx < ParentClimate.Length)
                {
                    ClimateData pc = ParentClimate[pIdx];
                    temp = pc.Temperature + noise.snoise(pos * 0.01f) * 0.05f;
                    moisture = pc.Moisture + noise.snoise(pos * 0.01f + new float2(100)) * 0.05f;
                }
            }
            else
            {
                float latitude = pos.y / MapSize.y;
                temp = 1.0f - math.abs(latitude - 0.5f) * 2.0f;
                if (height > 0.4f) temp -= (height - 0.4f) * 0.9f; // Температура сильнее падает с высотой

                moisture = 0.5f;
                // Горы задерживают влагу (орографический эффект - упрощенно)
                // Сделаем вершины суше (снег), подножья влажнее (лес)
                if (height > 0.3f && height < 0.8f) moisture += 0.3f;

                moisture += noise.snoise(pos * 0.0005f + new float2(Seed * 0.2f)) * 0.2f;
            }

            if (plate.IsOcean)
            {
                moisture = 1.0f;
                temp = 0.5f;
            }

            temp = math.clamp(temp, 0, 1);
            moisture = math.clamp(moisture, 0, 1);

            Climate[i] = new ClimateData { Temperature = temp, Moisture = moisture, WindDirection = 0 };

            Biomes[i] = new BiomeData
            {
                Type = CalculateBiome(plate.IsOcean, height, temp, moisture)
            };
        }

        private BiomeType CalculateBiome(bool isOcean, float height, float temp, float moisture)
        {
            if (isOcean) return BiomeType.Ocean;
            if (height < 0.07f) return BiomeType.Coast;

            // Снежные шапки - теперь строго по высоте и температуре
            // Поскольку горы стали выше (до 1.5), порог 0.9 обеспечит снег только на пиках
            if (height > 0.9f) return BiomeType.Snow;
            if (height > 0.6f) return BiomeType.Mountain; // Скалы

            if (temp < 0.25f) return BiomeType.Ice;

            if (temp > 0.6f)
            {
                if (moisture < 0.3f) return BiomeType.Desert;
                if (moisture < 0.6f) return BiomeType.Grassland;
                return BiomeType.Forest;
            }

            if (moisture < 0.35f) return BiomeType.Grassland;
            return BiomeType.Forest;
        }
    }
}