using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

[BurstCompile]
public struct L4FinalHeightJob : IJobFor
{
    [ReadOnly] public NativeArray<VoronoiSite> Sites;
    [ReadOnly] public NativeArray<TerrainData> ParentHeights;
    [ReadOnly] public NativeArray<DetailLevelData> LevelData;
    
    public NativeArray<FinalHeightData> FinalHeights;
    public NativeArray<TerrainData> Heights;

    public void Execute(int index)
    {

        VoronoiSite site = Sites[index];
        DetailLevelData levelData = LevelData[index];
        
        // +++ МИНИМАЛЬНОЕ ИЗМЕНЕНИЕ: БЕЗОПАСНЫЙ ДОСТУП +++
        TerrainData parentHeight;
        if (site.ParentIndex >= 0 && site.ParentIndex < ParentHeights.Length)
        {
            parentHeight = ParentHeights[site.ParentIndex];
        }
        else
        {
            // Значения по умолчанию для корневого уровня
            parentHeight = new TerrainData 
            { 
                Elevation = 0.5f, 
                Slope = 0.0f, 
                Roughness = 0.1f, 
                ElevationVariation = 0.0f 
            };
        }
        
        // +++ ИСПРАВЛЕНИЕ: КОРРЕКТНОЕ СРАВНЕНИЕ С ENUM +++
        bool isInfrastructureLevel = levelData.Level == DetailLevel.Infrastructure;
        
        float finalElevation = parentHeight.Elevation;
        float heightVariation = 0f;
        bool isUrban = false;

        if (isInfrastructureLevel) // L4
        {
            isUrban = IsUrbanArea(site.Position);

            if (isUrban)
            {
                heightVariation = Unity.Mathematics.noise.snoise(site.Position * 0.02f) * 0.01f;
            }
            else
            {
                heightVariation = Unity.Mathematics.noise.snoise(site.Position * 0.01f) * 0.05f;
            }
        }

        finalElevation += heightVariation;

        FinalHeights[index] = new FinalHeightData
        {
            FinalElevation = finalElevation,
            IsUrban = isUrban,
            HeightVariation = heightVariation
        };

        Heights[index] = new TerrainData
        {
            Elevation = finalElevation,
            Slope = parentHeight.Slope,
            Roughness = parentHeight.Roughness,
            ElevationVariation = heightVariation
        };
    }

    private bool IsUrbanArea(float2 position)
    {
        float value = Unity.Mathematics.noise.snoise(position * 0.001f);
        return value > 0.6f;
    }
}