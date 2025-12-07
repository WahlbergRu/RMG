using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
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
            float noiseVal = 0;

            // --- L0 (GLOBAL) ---
            if (Level == 0)
            {
                float2 center = MapSize * 0.5f;
                float dist = math.distance(pos, center);
                float maxRadius = math.min(MapSize.x, MapSize.y) * 0.45f;
                float distPercent = math.clamp(dist / maxRadius, 0f, 1f);

                // Остров
                float islandShape = (1.0f - (distPercent * distPercent)) * 1.2f - 0.2f;
                noiseVal = noise.snoise(pos * 0.00025f + new float2(Seed * 0.1f)); 
                
                if (distPercent < 0.6f && noiseVal < 0) noiseVal *= 0.25f; 

                baseHeight = islandShape + (noiseVal * 0.4f);
                isOcean = baseHeight < 0.05f;
            }
            // --- L1+ (CHILDREN) ---
            else
            {
                int parentIdx = SiteMeta[i].ParentIndex;
                
                if (ParentTectonics.Length > 0 && parentIdx >= 0 && parentIdx < ParentTectonics.Length)
                {
                    var parentData = ParentTectonics[parentIdx];
                    
                    // 1. Строгое наследование Океана
                    if (parentData.IsOcean)
                    {
                        isOcean = true;
                        baseHeight = -0.5f; 
                    }
                    else
                    {
                        // 2. Наследование Суши
                        // Добавляем детализацию, но не даем уйти под воду глобально
                        float freq = 0.002f * math.pow(3.0f, Level); 
                        float detail = noise.snoise(pos * freq + new float2(Seed * 0.3f));
                        float amp = 0.15f / Level; 

                        baseHeight = parentData.BaseHeight + (detail * amp);
                        
                        // Если родитель был сушей, ребенок не может стать морем (только озером в гидрологии)
                        // Поэтому ставим минимум 0.01 (чуть выше воды)
                        if (baseHeight < 0.01f) baseHeight = 0.01f;
                        
                        isOcean = false;
                    }
                }
                else
                {
                    // Fallback
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
            var plate = Tectonics[i];
            float height = plate.BaseHeight; 

            float temp = 0.5f;
            float moisture = 0.5f;

            if (Level > 0 && ParentClimate.Length > 0)
            {
                int pIdx = SiteMeta[i].ParentIndex;
                if (pIdx >= 0 && pIdx < ParentClimate.Length)
                {
                    var pc = ParentClimate[pIdx];
                    // Наследуем климат с вариацией
                    temp = pc.Temperature + noise.snoise(pos * 0.01f) * 0.05f;
                    moisture = pc.Moisture + noise.snoise(pos * 0.01f + new float2(100)) * 0.05f;
                }
            }
            else 
            {
                float latitude = pos.y / MapSize.y; 
                temp = 1.0f - math.abs(latitude - 0.5f) * 2.0f; 
                if (height > 0.4f) temp -= (height - 0.4f) * 0.5f;
                
                moisture = 0.5f;
                if (height < 0.2f) moisture += 0.3f;
                if (height > 0.6f) moisture -= 0.3f; 
            }

            if (plate.IsOcean) { moisture = 1.0f; temp = 0.5f; }

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
            
            // Если суша очень низкая - это Пляж
            if (height < 0.05f) return BiomeType.Coast; 

            if (height > 0.75f) { if (temp < 0.3f) return BiomeType.Snow; return BiomeType.Mountain; }
            if (temp < 0.25f) return BiomeType.Ice;
            if (temp > 0.65f) {
                if (moisture < 0.3f) return BiomeType.Desert;
                if (moisture < 0.6f) return BiomeType.Grassland;
                return BiomeType.Forest; 
            } else {
                if (moisture < 0.35f) return BiomeType.Grassland;
                return BiomeType.Forest; 
            }
        }
    }
}