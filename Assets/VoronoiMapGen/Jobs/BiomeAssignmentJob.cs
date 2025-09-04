using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

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
        if (randomSeed == 0) randomSeed = 1;
        var random = new Unity.Mathematics.Random(randomSeed);
        
        // ИСПРАВЛЕНО: добавлено ограничение на elevation
        float elevation = math.saturate(1.0f - normalizedDistance + random.NextFloat(-0.2f, 0.2f));
        float moisture = random.NextFloat(0.0f, 1.0f);
        float temperature = math.saturate(1.0f - normalizedDistance * 0.5f + random.NextFloat(-0.1f, 0.1f));

        // ИСПРАВЛЕНО: переработанная логика определения биома
        BiomeType biome;
        
        // 1. Сначала определяем по высоте
        if (elevation < 0.2f)
        {
            biome = BiomeType.Ocean;
        }
        else if (elevation < 0.3f)
        {
            biome = BiomeType.Coast;
        }
        else if (elevation > 0.8f)
        {
            // Для гор учитываем влажность
            if (moisture > 0.7f)
                biome = BiomeType.Forest;
            else if (moisture < 0.3f)
                biome = BiomeType.Desert;
            else
                biome = BiomeType.Mountain;
        }
        else
        {
            // Для средних высот учитываем комбинацию параметров
            if (moisture > 0.7f)
            {
                if (temperature > 0.5f)
                    biome = BiomeType.Forest;
                else
                    biome = BiomeType.Grassland;
            }
            else if (moisture < 0.3f)
            {
                if (temperature > 0.7f)
                    biome = BiomeType.Desert;
                else
                    biome = BiomeType.Grassland;
            }
            else
            {
                if (temperature < 0.2f)
                    biome = BiomeType.Ice;
                else
                    biome = BiomeType.Grassland;
            }
        }

        Biomes[index] = new CellBiome
        {
            Type = biome,
            Elevation = elevation,
            Moisture = moisture,
            Temperature = temperature
        };
    }
}