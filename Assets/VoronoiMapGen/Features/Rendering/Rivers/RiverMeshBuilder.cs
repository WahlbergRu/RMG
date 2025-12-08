using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering.Rivers
{
    public static class RiverMeshBuilder
    {
        public static void Build(
            EntityManager em,
            Material material,
            MapSettings settings,
            NativeArray<TerrainVisualData> styles,
            List<Mesh> meshesToTrack) // Legacy GameObject Output
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

            // === 1. Switch buffers to NativeList (Adapter Pattern) ===
            // This enables us to use the new Burst-optimized Geometry pipeline
            NativeList<float3> sVerts = new NativeList<float3>(256, Allocator.Temp);
            NativeList<int> sTris = new NativeList<int>(1024, Allocator.Temp);
            NativeList<float2> sUVs = new NativeList<float2>(256, Allocator.Temp);

            // Accumulators for Unity Mesh creation
            List<Vector3> cVerts = new List<Vector3>(RiverBuilderUtils.CHUNK_LIMIT);
            List<int> cTris = new List<int>(RiverBuilderUtils.CHUNK_LIMIT * 3);
            List<Vector2> cUVs = new List<Vector2>(RiverBuilderUtils.CHUNK_LIMIT);

            int renderMask = settings.RiverRenderMask;

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

                float gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                float gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (biomes[nIdx].Type == BiomeType.Ocean) gB = 0.2f;

                float yOffset = 0.2f;
                float yA = gA + yOffset;
                float yB = gB + yOffset;

                float3 start = new float3(cells[i].Centroid.x, yA, cells[i].Centroid.y);
                float3 end = new float3(cells[nIdx].Centroid.x, yB, cells[nIdx].Centroid.y);

                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                float fluxA = math.max(0, h.Flux);
                float fluxB = math.max(0, hydro[nIdx].Flux);
                float hierarchyBonus = 1.0f + math.max(0, 3 - currentLvl) * 0.2f;
                float configScale = myStyle.RiverWidthScale;
                
                float widthScale = hierarchyBonus * configScale;
                float wA = math.clamp(math.sqrt(fluxA) * widthScale, 2.5f, 150.0f);
                float wB = math.clamp(math.sqrt(fluxB) * widthScale, 2.5f, 150.0f);
                if (biomes[nIdx].Type == BiomeType.Ocean) wB *= 3.0f;

                // === 2. GENERATE USING NATIVE LISTS ===
                sVerts.Clear();
                sTris.Clear();
                sUVs.Clear();

                // Call the updated method signature using REF
                RiverGeometry.BuildCascadeSegment(
                    start, end, wA, wB,
                    myStyle, 
                    myStyle.TopNoiseAmplitude, myStyle.TopNoiseAmplitude,
                    h.LocalSlope,
                    ref sVerts, ref sTris, ref sUVs, settings.Seed
                );

                if (sVerts.Length == 0 || !RiverBuilderUtils.ValidateVertices(sVerts)) continue;

                // === 3. MANUAL COPY (ADAPTER) ===
                // Copy Native data to Managed Lists for flushing to legacy Mesh
                if (cVerts.Count + sVerts.Length > RiverBuilderUtils.CHUNK_LIMIT)
                    RiverBatcher.FlushChunk(em, material, cVerts, cTris, cUVs, meshesToTrack);

                int baseIndex = cVerts.Count;
                
                for(int v=0; v<sVerts.Length; v++) cVerts.Add(sVerts[v]); // float3 -> Vector3 implicit
                for(int u=0; u<sUVs.Length; u++) cUVs.Add(sUVs[u]);       // float2 -> Vector2 implicit
                for(int t=0; t<sTris.Length; t++) cTris.Add(sTris[t] + baseIndex);
            }

            // Flush remaining
            RiverBatcher.FlushChunk(em, material, cVerts, cTris, cUVs, meshesToTrack);

            // Cleanup local buffers
            sVerts.Dispose();
            sTris.Dispose();
            sUVs.Dispose();
            siteMap.Dispose();
        }
    }
}