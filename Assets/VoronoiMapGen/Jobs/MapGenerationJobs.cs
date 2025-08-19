using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    public struct SiteGenerationJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<float2> Sites;
        public int Seed;
        public float2 MapSize;

        public void Execute(int index)
        {
            // Используем детерминированный рандом с гарантией ненулевого сида
            uint randomSeed = (uint)(Seed + index * 397);
            if (randomSeed == 0) randomSeed = 1; // Гарантируем ненулевой сид
            var random = new Unity.Mathematics.Random(randomSeed);
            float2 position = new float2(
                random.NextFloat(0, MapSize.x),
                random.NextFloat(0, MapSize.y)
            );
            Sites[index] = position;
        }
    }

    public struct BiomeAssignmentJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoronoiCell> Cells;
        [ReadOnly] public NativeArray<float2> Sites;
        public NativeArray<CellBiome> Biomes;
        public float2 MapCenter;
        public float MapRadius;

        public void Execute(int index)
        {
            if (index >= Sites.Length || index >= Biomes.Length)
                return;

            var site = Sites[index];
            var distanceToCenter = math.distance(site, MapCenter);
            var normalizedDistance = distanceToCenter / (MapRadius > 0 ? MapRadius : 1.0f);

            // Простая генерация биомов на основе расстояния от центра
            uint randomSeed = (uint)(index * 137 + 1);
            if (randomSeed == 0) randomSeed = 1; // Гарантируем ненулевой сид
            var random = new Unity.Mathematics.Random(randomSeed);
            float elevation = 1.0f - normalizedDistance + random.NextFloat(-0.2f, 0.2f);
            float moisture = random.NextFloat(0.0f, 1.0f);
            float temperature = 1.0f - normalizedDistance * 0.5f + random.NextFloat(-0.1f, 0.1f);

            BiomeType biome = BiomeType.Grassland;
            if (elevation < 0.2f) biome = BiomeType.Ocean;
            else if (elevation < 0.3f) biome = BiomeType.Coast;
            else if (elevation > 0.8f) biome = BiomeType.Mountain;
            else if (moisture < 0.3f) biome = BiomeType.Desert;
            else if (temperature < 0.2f) biome = BiomeType.Ice;
            else if (moisture > 0.7f) biome = BiomeType.Forest;

            Biomes[index] = new CellBiome
            {
                Type = biome,
                Elevation = math.saturate(elevation),
                Moisture = math.saturate(moisture),
                Temperature = math.saturate(temperature)
            };
        }
    }
}