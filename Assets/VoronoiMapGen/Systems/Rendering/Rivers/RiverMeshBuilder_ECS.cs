using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils; 

namespace VoronoiMapGen.Systems.Rendering.Rivers
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
            for (int i = 0; i < cells.Length; i++) 
            {
                int lvl = (int)levels[i].Level;
                int uniqueKey = (lvl << 24) + cells[i].SiteIndex;
                siteMap.TryAdd(uniqueKey, i);
            }

            int renderMask = settings.RiverRenderMask;
            
            List<Vector3> tempVerts = new List<Vector3>();
            List<int> tempTris = new List<int>();
            List<Vector2> tempUVs = new List<Vector2>();

            for (int i = 0; i < entities.Length; i++)
            {
                var h = hydro[i];
                if (!h.IsRiver || h.FlowTargetIndex == -1) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                int currentLvl = (int)levels[i].Level;
                if ((renderMask & (1 << currentLvl)) == 0) continue;

                int targetUniqueKey = (currentLvl << 24) + h.FlowTargetIndex;
                if (!siteMap.TryGetValue(targetUniqueKey, out int nIdx)) continue;

                int safeStyleIdx = RiverBuilderUtils.GetSafeStyleIndex((DetailLevel)currentLvl, styles.Length);
                TerrainVisualData myStyle = styles[safeStyleIdx];

                // 1. ВЫСОТА
                float gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                float gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (biomes[nIdx].Type == BiomeType.Ocean) gB = 0.2f;

                // Мягкий оффсет для устранения Z-fighting и левитации
                float yOffset = 0.15f + (myStyle.TopNoiseAmplitude * 0.6f);

                float yA = gA + yOffset;
                float yB = gB + yOffset;

                float3 start = new float3(cells[i].Centroid.x, yA, cells[i].Centroid.y);
                float3 end   = new float3(cells[nIdx].Centroid.x, yB, cells[nIdx].Centroid.y);

                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                // 2. ШИРИНА
                float fluxA = math.max(0, h.Flux);
                float fluxB = math.max(0, hydro[nIdx].Flux);
                float hierarchyBonus = 1.0f + (math.max(0, 3 - currentLvl) * 0.2f);
                float configWidthScale = myStyle.RiverWidthScale; 

                float wA = math.clamp(math.sqrt(fluxA) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                float wB = math.clamp(math.sqrt(fluxB) * hierarchyBonus * configWidthScale, 2.0f, 120.0f);
                if (biomes[nIdx].Type == BiomeType.Ocean) wB *= 3.0f;

                // 3. ГЕНЕРАЦИЯ
                tempVerts.Clear(); tempTris.Clear(); tempUVs.Clear();

                RiverGeometry.BuildCascadeSegment(
                    start, end, wA, wB, 
                    myStyle, 
                    myStyle.TopNoiseAmplitude * 0.6f, myStyle.TopNoiseAmplitude * 0.6f,
                    h.LocalSlope, 
                    tempVerts, tempTris, tempUVs, settings.Seed
                );

                if (tempVerts.Count == 0 || !RiverBuilderUtils.ValidateVertices(tempVerts)) continue;

                // 4. ЗАПИСЬ
                int baseVertexIndex = vList.Length;

                for (int v = 0; v < tempVerts.Count; v++)
                {
                    vList.Add(new ProceduralVertex 
                    { 
                        Position = tempVerts[v], 
                        Normal = math.up(), 
                        UV = tempUVs[v] 
                    });
                }

                for (int t = 0; t < tempTris.Count; t++)
                {
                    iList.Add(new ProceduralIndex { Value = tempTris[t] + baseVertexIndex });
                }
            }

            siteMap.Dispose();
        }
    }
}