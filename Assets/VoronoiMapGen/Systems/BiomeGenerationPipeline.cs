using Unity.Entities;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Contexts;

namespace VoronoiMapGen.Systems
{
    public static class BiomeGenerationPipeline
    {
        public static void GenerateBiomes(EntityManager em, MapSettings settings)
        {
            var level1Cells = CellQueryHelper.GetLevel1Cells(em);
            if (level1Cells.Length == 0)
            {
                Debug.LogWarning("No Level 1 cells found for biome generation!");
                return;
            }

            var maxSiteIndex = CellQueryHelper.FindMaxSiteIndex(em, DetailLevel.Regional);
            if (maxSiteIndex < 0)
            {
                Debug.LogWarning("No Level 1 sites found for biome generation!");
                level1Cells.Dispose();
                return;
            }

            using var biomeContext = new BiomeGenerationContext(em, settings, level1Cells, maxSiteIndex);
            if (!biomeContext.Initialize())
            {
                level1Cells.Dispose();
                return;
            }

            biomeContext.GenerateBiomes();
            biomeContext.ApplyBiomes();

            level1Cells.Dispose();
        }
    }
}