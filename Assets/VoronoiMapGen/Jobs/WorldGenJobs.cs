using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    // --- L0: INITIAL TECTONICS (Форма острова + Вектора) ---
    [BurstCompile]
    public struct TectonicGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        [ReadOnly] public NativeArray<float2> Sites; 
        
        public NativeArray<TectonicPlateData> TectonicData;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            
            // 1. Warped Island Shape (Глобальная форма)
            float2 uv = (pos - MapSize * 0.5f) / (math.min(MapSize.x, MapSize.y) * 0.5f);
            
            // Основное искажение (Крупные формы)
            float warpX = noise.snoise(uv * 1.2f + new float2(Seed * 0.1f));
            float warpY = noise.snoise(uv * 1.2f + new float2(Seed * 0.2f));
            float2 warpedUV = uv + new float2(warpX, warpY) * 0.3f;
            
            // Изрезанность берегов (Fractal Coast)
            float coastalNoise = noise.snoise(uv * 6.0f + new float2(Seed * 0.9f)) * 0.15f;
            float dist = math.length(warpedUV) + coastalNoise; 

            float islandMask = 1.0f - math.smoothstep(0.45f, 1.0f, dist);

            // 2. Базовая высота (холмы и горы)
            float ridgeNoise = 1.0f - math.abs(noise.snoise(uv * 3.0f + new float2(Seed * 0.5f)));
            ridgeNoise = math.pow(math.max(0, ridgeNoise), 2.0f);
            
            float baseHeight = islandMask * (0.15f + ridgeNoise * 0.6f);
            
            // Сглаживание дна океана
            if (baseHeight < 0.12f) baseHeight = math.lerp(-0.4f, 0.1f, baseHeight / 0.12f);
            bool isOcean = baseHeight < 0.05f;

            // 3. Вектора движения плит (для столкновений)
            float rotAngle = noise.snoise(pos * 0.0004f + new float2(Seed * 13.0f)) * math.PI * 2;
            float2 velocity = new float2(math.cos(rotAngle), math.sin(rotAngle));

            TectonicData[i] = new TectonicPlateData
            {
                IsOcean = isOcean,
                Velocity = velocity,
                BaseHeight = baseHeight,
                CrustAge = 0
            };
        }
    }

    // --- TECTONIC INTERACTION (Столкновения плит и Берега) ---
    [BurstCompile]
    public struct TectonicInteractionJob : IJob
    {
        [ReadOnly] public NativeList<VoronoiEdge> Edges;
        public NativeArray<TectonicPlateData> TectonicData;

        public void Execute()
        {
            for (int i = 0; i < Edges.Length; i++)
            {
                var edge = Edges[i];
                int idxA = edge.SiteA;
                int idxB = edge.SiteB;

                // Проверка границ
                if (idxA < 0 || idxB < 0 || idxA >= TectonicData.Length || idxB >= TectonicData.Length) continue;

                var dataA = TectonicData[idxA];
                var dataB = TectonicData[idxB];

                // 1. Берега: Делаем переход плавнее (Шельф)
                if (dataA.IsOcean != dataB.IsOcean)
                {
                    int landIdx = dataA.IsOcean ? idxB : idxA;
                    var landData = TectonicData[landIdx];
                    
                    // Смягчаем высоту у берега
                    landData.BaseHeight = math.lerp(landData.BaseHeight, 0.08f, 0.5f); 
                    TectonicData[landIdx] = landData;
                    continue;
                }

                // 2. Горы: Столкновение плит
                if (!dataA.IsOcean && !dataB.IsOcean)
                {
                    // Скалярное произведение векторов скорости
                    float collision = math.dot(dataA.Velocity, -dataB.Velocity);

                    if (collision > 0.2f) // Если плиты движутся навстречу
                    {
                        // Чем сильнее удар, тем выше горы
                        float mountainFactor = (collision - 0.2f) * 2.5f; 
                        
                        dataA.BaseHeight += mountainFactor * 0.6f; 
                        dataB.BaseHeight += mountainFactor * 0.6f;
                        
                        // Помечаем кору как "старую/каменистую"
                        dataA.CrustAge = 1.0f;
                        dataB.CrustAge = 1.0f;
                        
                        TectonicData[idxA] = dataA;
                        TectonicData[idxB] = dataB;
                    }
                }
            }
        }
    }

    // --- L1: CLIMATE (Ветра + Реки) ---
    [BurstCompile]
    public struct ClimateGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        [ReadOnly] public NativeArray<HydrologyData> Hydrology; // Данные о воде
        
        public NativeArray<ClimateData> Climate;
        public NativeArray<BiomeData> Biomes;

        public void Execute(int i)
        {
            var plate = Tectonics[i];
            float height = plate.BaseHeight; 
            float2 pos = Sites[i];
            
            float2 uv = (pos - MapSize * 0.5f) / (math.max(MapSize.x, MapSize.y) * 0.5f);

            // 1. Температура
            float temp = 0.95f; 
            if (!plate.IsOcean) temp -= height * 1.1f; 
            temp += noise.snoise(pos * 0.0015f + new float2(Seed)) * 0.15f;
            temp = math.clamp(temp, 0, 1);

            // 2. Влажность + Ветра
            float moisture = 0.5f;
            float2 globalWindDir = math.normalize(new float2(1.0f, 0.5f)); 
            float windExposure = math.dot(math.normalize(uv), -globalWindDir); 
            float windNoise = noise.snoise(pos * 0.0008f + new float2(Seed + 77));
            
            if (plate.IsOcean)
            {
                moisture = 1.0f;
            }
            else
            {
                moisture += windNoise * 0.2f;
                moisture += windExposure * 0.25f; // Наветренная/Подветренная сторона
                if (height > 0.6f) moisture += 0.2f; 
                if (height < 0.15f) moisture += 0.2f;

                // --- ИРРИГАЦИЯ (Оазисы) ---
                // Проверяем, есть ли гидрология (массив может быть пустым на первом проходе)
                if (Hydrology.IsCreated && i < Hydrology.Length)
                {
                    var hydro = Hydrology[i];
                    if (hydro.IsRiver || hydro.IsLake)
                    {
                        moisture = math.max(moisture, 0.75f); // Река создает оазис
                    }
                    else if (hydro.Flux > 0.5f)
                    {
                        moisture += 0.2f;
                    }
                }
            }

            moisture = math.clamp(moisture, 0, 1);

            Climate[i] = new ClimateData { Temperature = temp, Moisture = moisture };
            Biomes[i] = new BiomeData { Type = CalculateBiome(plate.IsOcean, height, temp, moisture) };
        }

        private BiomeType CalculateBiome(bool isOcean, float height, float temp, float moisture)
        {
            if (isOcean) return BiomeType.Ocean;
            if (height < 0.1f) return BiomeType.Coast;
            if (height > 0.85f) return BiomeType.Snow; 
            if (height > 0.7f && temp < 0.4f) return BiomeType.Mountain;

            if (temp < 0.3f) return BiomeType.Snow; 
            
            if (temp < 0.6f) 
            {
                if (moisture < 0.3f) return BiomeType.Grassland; 
                if (moisture < 0.6f) return BiomeType.Forest;    
                return BiomeType.Forest;
            }
            else 
            {
                if (moisture < 0.25f) return BiomeType.Desert;
                if (moisture < 0.5f) return BiomeType.Grassland; 
                return BiomeType.Forest; 
            }
        }
    }
}