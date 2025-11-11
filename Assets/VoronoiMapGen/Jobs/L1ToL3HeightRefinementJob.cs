using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct L1ToL3HeightRefinementJob : IJobFor
    {
        [ReadOnly] public NativeArray<VoronoiSite> Sites;
        [ReadOnly] public NativeArray<TerrainData> ParentHeights;
        [ReadOnly] public int ParentLevel;
        [ReadOnly] public int CurrentLevel;
        
        public NativeArray<TerrainData> Heights;

        public void Execute(int index)
        {
            VoronoiSite site = Sites[index];
            
            // +++ МИНИМАЛЬНОЕ ИЗМЕНЕНИЕ: БЕЗОПАСНЫЙ ДОСТУП +++
            TerrainData parentHeight = new TerrainData();
            bool isRootLevel = site.ParentIndex == -1 || ParentLevel == -1;
            
            if (!isRootLevel && site.ParentIndex >= 0 && site.ParentIndex < ParentHeights.Length)
            {
                parentHeight = ParentHeights[site.ParentIndex];
            }
            else
            {
                parentHeight = new TerrainData 
                { 
                    Elevation = 0.5f, 
                    Slope = 0.0f, 
                    Roughness = 0.1f, 
                    ElevationVariation = 0.0f 
                };
            }

            float elevation = parentHeight.Elevation;
            float slope = 0f;
            float roughness = 0f;
            float variation = 0f;

            if (CurrentLevel == 1) // L1: крупный рельеф
            {
                float noise = Unity.Mathematics.noise.snoise(site.Position * 0.002f);
                elevation += noise * 0.2f;
                slope = math.abs(noise) * 0.1f;
                roughness = math.saturate(noise + 0.5f);
            }
            else if (CurrentLevel == 2) // L2: средний рельеф
            {
                float noise = Unity.Mathematics.noise.snoise(site.Position * 0.005f);
                elevation += noise * 0.1f;
                slope += math.abs(noise) * 0.05f;
                variation = noise * 0.05f;
            }
            else if (CurrentLevel == 3) // L3: локальный рельеф
            {
                float noise = Unity.Mathematics.noise.snoise(site.Position * 0.01f);
                elevation += noise * 0.05f;
                slope += math.abs(noise) * 0.02f;
                variation += noise * 0.02f;
            }

            Heights[index] = new TerrainData
            {
                Elevation = elevation,
                Slope = slope,
                Roughness = roughness,
                ElevationVariation = variation
            };
        }
    }
}