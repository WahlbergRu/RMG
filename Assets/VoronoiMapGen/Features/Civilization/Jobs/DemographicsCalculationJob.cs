using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components; // Для Climate/Hydro
using VoronoiMapGen.Features.Civilization.Components;

namespace VoronoiMapGen.Features.Civilization.Jobs
{
    [BurstCompile]
    public struct DemographicsCalculationJob : IJobParallelFor
    {
        // Входные данные
        [ReadOnly] public NativeArray<ClimateData> Climate;
        [ReadOnly] public NativeArray<HydrologyData> Hydrology;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        [ReadOnly] public NativeArray<CellBiome> Biomes;
        
        // Выход
        public NativeArray<DemographicsData> Demographics;

        public float GlobalPopulationScalar; // Глобальный множитель населения (из конфига)

        public void Execute(int i)
        {
            var clim = Climate[i];
            var hydro = Hydrology[i];
            var tech = Tectonics[i];
            var biome = Biomes[i];

            // 1. Agrarian Yield (Еда)
            // Идеал: Температура 0.6-0.8 (Умеренный/Теплый), Влага 0.5-0.8.
            // Штрафы за экстремальный холод, засуху или высокогорье.
            
            float tempScore = 1.0f - math.abs(clim.Temperature - 0.65f) * 2.5f; // Пик на 0.65
            float moistScore = math.smoothstep(0.2f, 0.6f, clim.Moisture);      // Нужна вода
            
            float fertility = math.clamp(tempScore * moistScore, 0, 1);

            // Штраф за горы
            if (tech.BaseHeight > 0.6f) fertility *= 0.1f; // На скалах не растет
            if (tech.BaseHeight > 0.4f) fertility *= 0.5f; // На холмах хуже

            // Биомные оверрайды
            if (biome.Type == BiomeType.Desert) fertility *= 0.1f;
            if (biome.Type == BiomeType.Ice || biome.Type == BiomeType.Snow) fertility = 0;
            if (biome.Type == BiomeType.Grassland) fertility *= 1.2f;

            // 2. Water Score (Вода)
            float waterAccess = 0.1f; // Базовая (дождевая)
            
            if (hydro.IsOcean) 
            {
                waterAccess = 0.0f; // В океане жить нельзя (пока не Атлантида)
                fertility = 0.0f;
            }
            else
            {
                if (hydro.IsRiver) waterAccess = 1.0f;
                else if (hydro.IsLake) waterAccess = 1.0f;
                else
                {
                    // Если нет реки, смотрим на влажность почвы
                    waterAccess = math.lerp(0.0f, 0.5f, clim.Moisture); 
                }
                
                // Бонус побережья (рыбалка + торговля)
                if (biome.Type == BiomeType.Coast) waterAccess += 0.3f;
            }
            waterAccess = math.clamp(waterAccess, 0, 1);

            // 3. Hazard Rating (Опасность)
            float hazard = 0.0f;
            
            // Джунгли (Болезни): Высокая температура + Высокая влага
            if (clim.Temperature > 0.75f && clim.Moisture > 0.7f) hazard += 0.4f;
            
            // Ледники
            if (biome.Type == BiomeType.Ice) hazard += 0.8f;
            
            // Шум (аномалии, монстры - пока заглушка)
            // hazard += noise.snoise...

            // 4. Final Calculation
            float rawCapacity = (fertility * 2.0f + waterAccess) * (1.0f - hazard);
            if (rawCapacity < 0) rawCapacity = 0;

            int pop = (int)(rawCapacity * GlobalPopulationScalar);
            
            // Горы и Океаны пустые
            if (tech.IsOcean || biome.Type == BiomeType.Mountain || biome.Type == BiomeType.Snow) pop = 0;

            Demographics[i] = new DemographicsData
            {
                FoodYield = fertility,
                WaterScore = waterAccess,
                HazardRating = hazard,
                HousingCapacity = rawCapacity,
                EstimatedPopulation = pop
            };
        }
    }
}