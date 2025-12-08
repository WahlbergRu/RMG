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
            NativeList<ProceduralVertex> vList,
            NativeList<ProceduralIndex> iList
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
            
            // Цвет рек (Синий)
            var riverColor = new float4(0.0f, 0.4f, 0.9f, 0.9f);

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

                var safeStyleIdx = RiverBuilderUtils.GetSafeStyleIndex((DetailLevel)currentLvl, styles.Length);
                var myStyle = styles[safeStyleIdx];

                // Heights
                var gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                var gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                bool targetIsOcean = biomes[nIdx].Type == BiomeType.Ocean;

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (targetIsOcean) gB = 0.2f;

                var yOffset = 0.15f + myStyle.TopNoiseAmplitude * 0.6f;
                var yA = gA + yOffset;
                var yB = gB + yOffset;

                // --- COORDINATES CALCULATION ---
                var pStart2D = cells[i].Centroid;
                var pEnd2D = cells[nIdx].Centroid;

                // <--- COASTLINE FIX: Остановка реки на границе берега --->
                // В Вороном граница (ребро) всегда лежит ровно посередине между двумя сайтами.
                // Если мы впадаем в океан, нам нужно остановиться на берегу, а не плыть к центру океана.
                if (targetIsOcean)
                {
                    pEnd2D = (pStart2D + pEnd2D) * 0.5f;
                }

                var start = new float3(pStart2D.x, yA, pStart2D.y);
                var end = new float3(pEnd2D.x, yB, pEnd2D.y);

                // --- VALIDATION ---
                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                // Widths
                var fluxA = math.max(0, h.Flux);
                var fluxB = math.max(0, hydro[nIdx].Flux);
                var hierarchyBonus = 1.0f + math.max(0, 3 - currentLvl) * 0.2f;
                var configWidthScale = myStyle.RiverWidthScale;
                var wA = math.clamp(math.sqrt(fluxA) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                var wB = math.clamp(math.sqrt(fluxB) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                
                // Делаем дельту широкой при впадении в море
                if (targetIsOcean) wB *= 4.0f;

                // Gen
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

                if (tempVerts.Length == 0 || !RiverBuilderUtils.ValidateVertices(tempVerts)) continue;

                var baseVertexIndex = vList.Length;

                for (var v = 0; v < tempVerts.Length; v++)
                {
                    vList.Add(new ProceduralVertex
                    {
                        Position = tempVerts[v],
                        Normal = math.up(),
                        Color = riverColor,
                        UV = tempUVs[v]
                    });
                }

                for (var t = 0; t < tempTris.Length; t++)
                {
                    iList.Add(new ProceduralIndex { Value = tempTris[t] + baseVertexIndex });
                }
            }

            tempVerts.Dispose();
            tempTris.Dispose();
            tempUVs.Dispose();
            siteMap.Dispose();
        }
    }
}