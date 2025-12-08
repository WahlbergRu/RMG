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
            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<HydrologyData>(),
                ComponentType.ReadOnly<DetailLevelData>(),
                ComponentType.ReadOnly<CellBiome>()
            );

            if (query.IsEmpty) return;

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<VoronoiCell> cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            using NativeArray<HydrologyData> hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);
            using NativeArray<CellBiome> biomes = query.ToComponentDataArray<CellBiome>(Allocator.Temp);
            using NativeArray<DetailLevelData> levels = query.ToComponentDataArray<DetailLevelData>(Allocator.Temp);

            NativeParallelHashMap<int, int> siteMap = new NativeParallelHashMap<int, int>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++)
            {
                int lvl = (int)levels[i].Level;
                int uniqueKey = (lvl << 24) + cells[i].SiteIndex;
                siteMap.TryAdd(uniqueKey, i);
            }

            int renderMask = settings.RiverRenderMask;
            
            // Цвет рек (Синий)
            float4 riverColor = new float4(0.0f, 0.4f, 0.9f, 0.9f);

            NativeList<float3> tempVerts = new NativeList<float3>(256, Allocator.Temp);
            NativeList<int> tempTris = new NativeList<int>(1024, Allocator.Temp);
            NativeList<float2> tempUVs = new NativeList<float2>(256, Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                HydrologyData h = hydro[i];
                if (!h.IsRiver || h.FlowTargetIndex == -1) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                int currentLvl = (int)levels[i].Level;
                if ((renderMask & (1 << currentLvl)) == 0) continue;

                int targetUniqueKey = (currentLvl << 24) + h.FlowTargetIndex;
                if (!siteMap.TryGetValue(targetUniqueKey, out int nIdx)) continue;

                int safeStyleIdx = RiverBuilderUtils.GetSafeStyleIndex((DetailLevel)currentLvl, styles.Length);
                TerrainVisualData myStyle = styles[safeStyleIdx];

                // Heights
                float gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                float gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                bool targetIsOcean = biomes[nIdx].Type == BiomeType.Ocean;

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (targetIsOcean) gB = 0.2f;

                float yOffset = 0.15f + myStyle.TopNoiseAmplitude * 0.6f;
                float yA = gA + yOffset;
                float yB = gB + yOffset;

                // --- COORDINATES CALCULATION ---
                float2 pStart2D = cells[i].Centroid;
                float2 pEnd2D = cells[nIdx].Centroid;

                // <--- COASTLINE FIX: Остановка реки на границе берега --->
                // В Вороном граница (ребро) всегда лежит ровно посередине между двумя сайтами.
                // Если мы впадаем в океан, нам нужно остановиться на берегу, а не плыть к центру океана.
                if (targetIsOcean)
                {
                    pEnd2D = (pStart2D + pEnd2D) * 0.5f;
                }

                float3 start = new float3(pStart2D.x, yA, pStart2D.y);
                float3 end = new float3(pEnd2D.x, yB, pEnd2D.y);

                // --- VALIDATION ---
                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                // Widths
                float fluxA = math.max(0, h.Flux);
                float fluxB = math.max(0, hydro[nIdx].Flux);
                float hierarchyBonus = 1.0f + math.max(0, 3 - currentLvl) * 0.2f;
                float configWidthScale = myStyle.RiverWidthScale;
                float wA = math.clamp(math.sqrt(fluxA) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                float wB = math.clamp(math.sqrt(fluxB) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                
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

                int baseVertexIndex = vList.Length;

                for (int v = 0; v < tempVerts.Length; v++)
                {
                    vList.Add(new ProceduralVertex
                    {
                        Position = tempVerts[v],
                        Normal = math.up(),
                        Color = riverColor,
                        UV = tempUVs[v]
                    });
                }

                for (int t = 0; t < tempTris.Length; t++)
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