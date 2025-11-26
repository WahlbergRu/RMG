using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    // --- L0: TECTONICS ---
    [BurstCompile]
    public struct TectonicGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        [ReadOnly] public NativeArray<float2> Sites; // Центры ячеек
        
        public NativeArray<TectonicPlateData> TectonicData;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            
            // 1. Океан или Суша? (Используем низкочастотный шум)
            // Масштаб 0.0005 означает очень крупные пятна (континенты)
            float continentNoise = noise.snoise(pos * 0.0005f + new float2(Seed));
            
            // Порог: > 0.1 = Суша, иначе Океан
            bool isOcean = continentNoise < 0.1f;

            // 2. Вектор движения (Шум Перлина для направления)
            float moveNoiseX = noise.snoise(pos * 0.0003f + new float2(Seed + 100));
            float moveNoiseY = noise.snoise(pos * 0.0003f + new float2(Seed + 200));
            float2 velocity = math.normalize(new float2(moveNoiseX, moveNoiseY));

            // 3. Базовая высота
            float baseHeight = isOcean ? -1.0f : 0.5f;
            // Добавляем вариативность внутри плит
            baseHeight += noise.snoise(pos * 0.002f) * 0.2f; 

            TectonicData[i] = new TectonicPlateData
            {
                IsOcean = isOcean,
                Velocity = velocity,
                BaseHeight = baseHeight,
                CrustAge = math.abs(continentNoise) // Чем дальше от 0, тем "стабильнее" плита
            };
        }
    }

    // --- L1: CLIMATE & BIOMES ---
    [BurstCompile]
    public struct ClimateGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        
        public NativeArray<ClimateData> Climate;
        public NativeArray<BiomeData> Biomes;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            var plate = Tectonics[i];

            // 1. ТЕМПЕРАТУРА
            // База от широты (Latitude): Экватор (середина Z) теплый, края холодные
            // Нормализуем координату Y (Z в 3D) от 0 до 1
            float normalizedY = pos.y / MapSize.y; 
            float latitude = math.abs(normalizedY - 0.5f) * 2.0f; // 0 на экваторе, 1 на полюсах
            
            float temp = 1.0f - latitude; 
            
            // Коррекция высотой (чем выше, тем холоднее)
            if (plate.BaseHeight > 0) temp -= plate.BaseHeight * 0.5f;
            
            // Немного шума для локальных вариаций
            temp += noise.snoise(pos * 0.005f + new float2(Seed + 500)) * 0.1f;
            temp = math.saturate(temp);

            // 2. ВЛАЖНОСТЬ
            // В океане влажно (1.0), на суше зависит от шума
            float moisture = plate.IsOcean ? 1.0f : 0.0f;
            
            if (!plate.IsOcean)
            {
                // Ветер дует шум (облака)
                float rainNoise = noise.snoise(pos * 0.003f + new float2(Seed + 800));
                moisture = math.saturate((rainNoise + 0.5f) * 0.8f);
                
                // Можно добавить логику "Близость к океану", но это требует графа соседей.
                // Пока используем простой шум.
            }

            Climate[i] = new ClimateData
            {
                Temperature = temp,
                Moisture = moisture,
                WindDirection = 0 // Пока заглушка
            };

            // 3. ОПРЕДЕЛЕНИЕ БИОМА
            Biomes[i] = new BiomeData
            {
                Type = CalculateBiome(plate.BaseHeight, temp, moisture)
            };
        }

        private BiomeType CalculateBiome(float height, float temp, float moisture)
        {
            if (height < 0) return BiomeType.Ocean;
            if (height < 0.05f) return BiomeType.Coast; // Пляж

            // Высокогорье
            if (height > 0.8f)
            {
                if (temp < 0.2f) return BiomeType.Snow; // Снежная вершина
                return BiomeType.Mountain; // Голые скалы
            }

            // Матрица Биомов (Whittaker diagram упрощенная)
            if (temp < 0.2f) return BiomeType.Ice; // Тундра/Ледник
            
            if (temp > 0.6f) // Жарко
            {
                if (moisture < 0.3f) return BiomeType.Desert;
                if (moisture < 0.6f) return BiomeType.Grassland; // Саванна
                return BiomeType.Forest; // Джунгли
            }
            else // Умеренно
            {
                if (moisture < 0.3f) return BiomeType.Grassland; // Степь
                if (moisture < 0.6f) return BiomeType.Forest;
                return BiomeType.Forest; // Тайга (если холоднее)
            }
        }
    }
}