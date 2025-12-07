using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class RiverMeshBuilder
    {
        public static void Build(
            EntityManager em, 
            Material material, 
            MapSettings settings, 
            NativeArray<TerrainVisualData> styles, 
            List<Mesh> meshesToTrack)
        {
            // 1. Сбор данных
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

            // Кэш соседей
            var siteMap = new NativeParallelHashMap<int, int>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++) 
            {
                int lvl = (int)levels[i].Level;
                int uniqueKey = (lvl << 24) + cells[i].SiteIndex;
                siteMap.TryAdd(uniqueKey, i);
            }

            List<Vector3> cVerts = new List<Vector3>(RiverBuilderUtils.CHUNK_LIMIT);
            List<int> cTris = new List<int>(RiverBuilderUtils.CHUNK_LIMIT * 3);
            List<Vector2> cUVs = new List<Vector2>(RiverBuilderUtils.CHUNK_LIMIT);

            List<Vector3> sVerts = new List<Vector3>();
            List<int> sTris = new List<int>();
            List<Vector2> sUVs = new List<Vector2>();

            int renderMask = settings.RiverRenderMask;

            // ------------------------------------------
            // ГЛАВНЫЙ ЦИКЛ
            // ------------------------------------------
            for (int i = 0; i < entities.Length; i++)
            {
                var h = hydro[i];
                if (!h.IsRiver || h.FlowTargetIndex == -1) continue;
                if (biomes[i].Type == BiomeType.Ocean) continue;

                int currentLvl = (int)levels[i].Level;

                // 1. Проверка видимости по маске РЕК
                if ((renderMask & (1 << currentLvl)) == 0) continue;

                // 2. Поиск соседа
                int targetUniqueKey = (currentLvl << 24) + h.FlowTargetIndex;
                if (!siteMap.TryGetValue(targetUniqueKey, out int nIdx)) continue;


                // === ПОЛУЧЕНИЕ НАСТРОЕК (ШАГ 4) ===
                // Берем стиль для текущего уровня реки (L0/L1/L2...)
                int safeStyleIdx = RiverBuilderUtils.GetSafeStyleIndex((DetailLevel)currentLvl, styles.Length);
                TerrainVisualData myStyle = styles[safeStyleIdx];

                // === РАСЧЕТ ВЫСОТЫ ===
                float gA = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[i], myStyle.HeightScale);
                float gB = RiverBuilderUtils.CalculateBaseTerrainHeightSafe(biomes[nIdx], myStyle.HeightScale);

                if (biomes[i].Type == BiomeType.Ocean) gA = 0.2f;
                if (biomes[nIdx].Type == BiomeType.Ocean) gB = 0.2f;

                float yOffset = RiverBuilderUtils.Z_FIGHT_BIAS;

                float yA = gA + yOffset;
                float yB = gB + yOffset;

                // Координаты
                float3 start = new float3(cells[i].Centroid.x, yA, cells[i].Centroid.y);
                float3 end   = new float3(cells[nIdx].Centroid.x, yB, cells[nIdx].Centroid.y);

                // Валидация
                if (!RiverBuilderUtils.IsFinite(start) || !RiverBuilderUtils.IsFinite(end)) continue;
                if (math.distancesq(start, end) < 0.1f) continue;
                if (math.abs(yA - yB) > RiverBuilderUtils.MAX_HEIGHT_DIFF) continue;

                // === РАСЧЕТ ШИРИНЫ (ШАГ 4) ===
                float fluxA = math.max(0, h.Flux);
                float fluxB = math.max(0, hydro[nIdx].Flux);
                
                // Бонус ширины от иерархии (старые реки шире)
                float hierarchyBonus = 1.0f + (math.max(0, 3 - currentLvl) * 0.2f);
                
                // Главный множитель из инспектора
                float configScale = myStyle.RiverWidthScale;

                // Комбинируем
                float widthScale = hierarchyBonus * configScale;

                float wA = math.clamp(math.sqrt(fluxA) * widthScale, 2.5f, 150.0f);
                float wB = math.clamp(math.sqrt(fluxB) * widthScale, 2.5f, 150.0f);
                if (biomes[nIdx].Type == BiomeType.Ocean) wB *= 3.0f;

                // === ГЕНЕРАЦИЯ (ШАГ 4) ===
                sVerts.Clear(); sTris.Clear(); sUVs.Clear();

                // Передаем myStyle, внутри которого лежат MeanderAmp, Frequency и т.д.
                RiverGeometry.BuildCascadeSegment(
                    start, end, wA, wB, 
                    myStyle, // <--- Передаем настройки
                    myStyle.TopNoiseAmplitude, myStyle.TopNoiseAmplitude, 
                    h.LocalSlope, 
                    sVerts, sTris, sUVs, settings.Seed
                );

                if (sVerts.Count == 0 || !RiverBuilderUtils.ValidateVertices(sVerts)) continue;

                // Батчинг
                if (cVerts.Count + sVerts.Count > RiverBuilderUtils.CHUNK_LIMIT)
                {
                    RiverBatcher.FlushChunk(em, material, cVerts, cTris, cUVs, meshesToTrack);
                }

                int baseIndex = cVerts.Count;
                cVerts.AddRange(sVerts);
                cUVs.AddRange(sUVs);
                for(int t=0; t<sTris.Count; t++) cTris.Add(sTris[t] + baseIndex);
            }

            // Финальный сброс
            RiverBatcher.FlushChunk(em, material, cVerts, cTris, cUVs, meshesToTrack);

            siteMap.Dispose();
        }
    }
}