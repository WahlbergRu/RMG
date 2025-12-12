// ============================================================
// FILE: Assets\VoronoiMapGen\Features\MapGeneration\Jobs\WorldGenJobs.cs
// ============================================================
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components; // Для доступа к MapSettings и конфигам
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

            if (Level == 0)
            {
                float2 center = MapSize * 0.5f;
                float dist = math.distance(pos, center);
                float maxRadius = math.min(MapSize.x, MapSize.y) * 0.45f;
                float distPercent = math.clamp(dist / maxRadius, 0f, 1f);

                float islandShape = (1.0f - math.pow(distPercent, 1.5f)) * 1.5f - 0.3f;
                float baseNoise = noise.snoise(pos * 0.0004f + new float2(Seed * 0.1f));
                
                float mountainFreq = 0.0012f;
                float mountainRaw = noise.snoise(pos * mountainFreq + new float2(Seed * 0.5f));
                float ridgedMountains = 1.0f - math.abs(mountainRaw); 
                ridgedMountains = math.pow(ridgedMountains, 2.5f);

                float mountainMask = math.smoothstep(0.2f, 0.8f, islandShape);
                baseHeight = islandShape + baseNoise * 0.3f + ridgedMountains * 0.9f * mountainMask;

                float valleyFreq = 0.002f;
                float valleyRaw = noise.snoise(pos * valleyFreq + new float2(Seed * 0.9f));
                float valleyFactor = 1.0f - math.abs(valleyRaw);
                valleyFactor = math.pow(valleyFactor, 4.0f);
                float carveMask = math.smoothstep(0.1f, 0.6f, baseHeight);
                float carveStrength = 0.7f;
                
                if (baseHeight > 0.05f) baseHeight -= valleyFactor * carveStrength * carveMask;
                if (distPercent < 0.7f && baseNoise < -0.2f) baseHeight *= 0.8f;
                isOcean = baseHeight < 0.08f;
            }
            else
            {
                int parentIdx = SiteMeta[i].ParentIndex;
                if (ParentTectonics.Length > 0 && parentIdx >= 0 && parentIdx < ParentTectonics.Length)
                {
                    TectonicPlateData parentData = ParentTectonics[parentIdx];
                    if (parentData.IsOcean) { isOcean = true; baseHeight = -0.5f; }
                    else {
                        float freq = 0.002f * math.pow(3.0f, Level);
                        float detail = noise.snoise(pos * freq + new float2(Seed * 0.3f));
                        float amp = 0.2f / Level;
                        float localValley = 1.0f - math.abs(noise.snoise(pos * (freq * 1.5f) + new float2(Seed * 0.8f)));
                        localValley = math.pow(localValley, 3.0f) * (0.25f / Level);

                        baseHeight = parentData.BaseHeight + detail * amp - localValley;
                        if (baseHeight < 0.05f) {
                            if (parentData.BaseHeight > 0.4f) {
                                if (baseHeight < 0.01f) baseHeight = 0.01f;
                            } else {
                                isOcean = baseHeight <= 0.001f;
                            }
                        } else {
                            isOcean = false;
                        }
                    }
                } else { baseHeight = 0; isOcean = true; }
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
        
        // Передаем КОНФИГУРАЦИЮ
        public ClimateConfig Config; 

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

            // Используем значения из CONFIG
            float temp = Config.BaseTemperature;
            float moisture = Config.BaseMoisture;

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
                temp = Config.BaseTemperature + (0.5f - math.abs(latitude - 0.5f)); 

                // Cooling with height (Lapse Rate from config)
                if (height > 0.4f) temp -= (height - 0.4f) * Config.TemperatureLapseRate; 

                // Moisture calculation
                moisture = Config.BaseMoisture;
                // Mountains effect
                if (height > 0.3f && height < 0.8f) moisture += 0.3f;
                // Noise variation from config freq
                moisture += noise.snoise(pos * Config.MoistureNoiseFreq + new float2(Seed * 0.2f)) * 0.2f;
            }

            if (plate.IsOcean) { moisture = 1.0f; temp = math.max(temp, 0.4f); } // Океан аккумулирует тепло

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

            if (height > 0.9f) return BiomeType.Snow;
            if (height > 0.6f) return BiomeType.Mountain; 

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