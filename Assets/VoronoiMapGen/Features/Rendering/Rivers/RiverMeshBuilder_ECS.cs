using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering.Rivers
{
    public static class RiverMeshBuilder_ECS
    {
        public static void BuildToNativeList(
            EntityManager em,
            MapSettings settings,
            NativeArray<TerrainVisualData> styles,
            NativeList<ProceduralVertex> vList, // Destination
            NativeList<ProceduralIndex> iList   // Destination
        )
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<HydrologyData>(),
                ComponentType.ReadOnly<DetailLevelData>(),
                ComponentType.ReadOnly<CellBiome>()
            );

            if (query.IsEmpty) return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            using var hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);
            using var biomes = query.ToComponentDataArray<CellBiome>(Allocator.Temp);
            using var levels = query.ToComponentDataArray<DetailLevelData>(Allocator.Temp);

            var siteMap = new NativeParallelHashMap<int, int>(cells.Length, Allocator.Temp);
            for (var i = 0; i < cells.Length; i++)
            {
                var lvl = (int)levels[i].Level;
                var uniqueKey = (lvl << 24) + cells[i].SiteIndex;
                siteMap.TryAdd(uniqueKey, i);
            }

            var renderMask = settings.RiverRenderMask;

            // === OPTIMIZATION: Reuse Re-usable buffers to avoid GC Allocations per river segment ===
            var tempVerts = new NativeList<float3>(256, Allocator.Temp);
            var tempTris = new NativeList<int>(1024, Allocator.Temp);
            var tempUVs = new NativeList<float2>(256, Allocator.Temp);

            for (var i = 0; i < entities.Length; i++)
            {
                var h = hydro[i];
                if (!h.IsRiver || h.FlowTargetIndex == -1) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                var currentLvl = (int)levels[i].Level;
                if ((renderMask & (1 << currentLvl)) == 0) continue;

                var targetUniqueKey = (currentLvl << 24) + h.FlowTargetIndex;
                if (!siteMap.TryGetValue(targetUniqueKey, out var nIdx)) continue;

                // Calculate Config
                var safeStyleIdx = RiverBuilderUtils.GetSafeStyleIndex((DetailLevel)currentLvl, styles.Length);
                var myStyle = styles[safeStyleIdx];

                // Calculate Heights
                var gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                var gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (biomes[nIdx].Type == BiomeType.Ocean) gB = 0.2f;

                var yOffset = 0.15f + myStyle.TopNoiseAmplitude * 0.6f;
                var yA = gA + yOffset;
                var yB = gB + yOffset;

                var start = new float3(cells[i].Centroid.x, yA, cells[i].Centroid.y);
                var end = new float3(cells[nIdx].Centroid.x, yB, cells[nIdx].Centroid.y);

                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                // Calculate Width
                var fluxA = math.max(0, h.Flux);
                var fluxB = math.max(0, hydro[nIdx].Flux);
                var hierarchyBonus = 1.0f + math.max(0, 3 - currentLvl) * 0.2f;
                var configWidthScale = myStyle.RiverWidthScale;

                var wA = math.clamp(math.sqrt(fluxA) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                var wB = math.clamp(math.sqrt(fluxB) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                if (biomes[nIdx].Type == BiomeType.Ocean) wB *= 3.0f;

                // === GENERATION (HOT PATH) ===
                tempVerts.Clear();
                tempTris.Clear();
                tempUVs.Clear();

                RiverGeometry.BuildCascadeSegment(
                    start, end, wA, wB,
                    myStyle,
                    myStyle.TopNoiseAmplitude * 0.6f, myStyle.TopNoiseAmplitude * 0.6f,
                    h.LocalSlope,
                    ref tempVerts, ref tempTris, ref tempUVs, settings.Seed
                );

                // Skip invalid or empty results
                if (tempVerts.Length == 0 || !RiverBuilderUtils.ValidateVertices(tempVerts)) continue;

                // === AGGREGATE TO GLOBAL LIST ===
                var baseVertexIndex = vList.Length;

                for (var v = 0; v < tempVerts.Length; v++)
                {
                    vList.Add(new ProceduralVertex
                    {
                        Position = tempVerts[v],
                        Normal = math.up(), // We'll assume UP for simplicity, or recalc later
                        UV = tempUVs[v]
                    });
                }

                for (var t = 0; t < tempTris.Length; t++)
                {
                    iList.Add(new ProceduralIndex { Value = tempTris[t] + baseVertexIndex });
                }
            }
            
            // Clean up temporary local buffers
            tempVerts.Dispose();
            tempTris.Dispose();
            tempUVs.Dispose();
            
            siteMap.Dispose();
        }
    }
}