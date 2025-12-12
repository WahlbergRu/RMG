using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using VoronoiMapGen.Components; // Для CivilizationConfig
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Civilization.Components; 

namespace VoronoiMapGen.Features.MapGeneration.Utils
{
    public static class SettlementBuilder
    {
        private struct Candidate
        {
            public int Index;
            public float Score;
        }

        public static void CalculateSettlements(
            NativeArray<VoronoiCell> cells,
            NativeArray<HydrologyData> hydro,
            NativeArray<BiomeData> biomes,
            NativeArray<TectonicPlateData> tectonics,
            NativeParallelMultiHashMap<int, NeighborInfo> neighborGraph,
            ref NativeArray<SettlementData> outSettlements,
            int targetMetropolisCount,
            // Добавили конфиг
            CivilizationConfig config,
            int seed)
        {
            int count = cells.Length;
            List<Candidate> candidates = new List<Candidate>(count);
            Random rng = new Random((uint)seed + 777); 

            // 1. SCORING
            for (int i = 0; i < count; i++)
            {
                outSettlements[i] = new SettlementData
                {
                    Type = SettlementType.Wilderness, 
                    MetropolisIndex = -1,
                    SuitabilityScore = 0,
                    Tier = 0,
                    TradePower = 0,
                    IsRoadNode = false
                };

                if (hydro[i].IsOcean || tectonics[i].IsOcean) continue;

                float score = 0.3f; 

                // --- ПАРАМЕТРЫ СКОРИНГА (Можно тоже вынести, но пока оставим хардкод) ---
                if (hydro[i].IsRiver) score += 0.4f;
                if (hydro[i].IsLake) score += 0.3f;
                if (hydro[i].Type == RiverMorphology.Delta) score += 0.5f;

                var b = biomes[i].Type;
                if (b == BiomeType.Grassland) score += 0.2f;
                if (b == BiomeType.Coast) score += 0.3f;
                if (b == BiomeType.Desert) score -= 0.3f;
                if (b == BiomeType.Snow || b == BiomeType.Ice) score -= 0.8f;
                if (b == BiomeType.Mountain) score -= 0.5f;
                if (hydro[i].LocalSlope > 0.15f) score -= 0.3f;
                if (tectonics[i].BaseHeight > 0.7f) score -= 0.3f; 

                score += rng.NextFloat(-0.05f, 0.05f);
                score = math.clamp(score, 0f, 1f);

                // Сохраняем
                var data = outSettlements[i];
                data.SuitabilityScore = score; 
                outSettlements[i] = data;

                // Для столицы порог оставим высоким и жестким, они - основа сетки
                if (score > 0.65f) candidates.Add(new Candidate { Index = i, Score = score });
            }

            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            // 2. METROPOLIS SELECTION
            List<float2> metroPositions = new List<float2>();
            float minDistanceSq = 400f * 400f; 

            foreach (var cand in candidates)
            {
                if (metroPositions.Count >= targetMetropolisCount) break;

                float2 pos = cells[cand.Index].Centroid;
                bool tooClose = false;
                foreach (var mp in metroPositions)
                {
                    if (math.distancesq(pos, mp) < minDistanceSq) { tooClose = true; break; }
                }

                if (!tooClose)
                {
                    var d = outSettlements[cand.Index];
                    d.Type = SettlementType.Metropolis;
                    d.MetropolisIndex = cand.Index;
                    d.Tier = 5;
                    d.IsRoadNode = true;
                    d.TradePower = 1.0f;
                    outSettlements[cand.Index] = d;

                    metroPositions.Add(pos);
                    // Передаем конфиг дальше
                    SpreadInfluence(cand.Index, neighborGraph, ref outSettlements, config, ref rng);
                }
            }
        }

        private static void SpreadInfluence(
            int centerIdx,
            NativeParallelMultiHashMap<int, NeighborInfo> graph,
            ref NativeArray<SettlementData> settlements,
            CivilizationConfig config,
            ref Random rng)
        {
            if (graph.TryGetFirstValue(centerIdx, out NeighborInfo n, out var it))
            {
                do
                {
                    int idx = n.Index;
                    if (idx >= settlements.Length) continue;
                    var current = settlements[idx];
                    
                    if (current.Type == SettlementType.Metropolis) continue;

                    // --- ЖЕСТКИЕ ФИЛЬТРЫ ИЗ ЭДИТОРА ---
                    
                    // 1. Порог качества: если земля хуже, чем в слайдере - пропускаем
                    if (current.SuitabilityScore < config.MinSuitability) continue;
                    
                    // 2. Вероятность: Слайдер "TownSpawnChance" (0.0 - 1.0)
                    if (rng.NextFloat() > config.TownSpawnChance) 
                    {
                        // Не прокнуло на Город -> пробуем Аванпост
                        SpreadOutpost(idx, centerIdx, graph, ref settlements, config, ref rng);
                        continue; 
                    }
                    // ------------------------------------

                    current.Type = SettlementType.Town;
                    current.MetropolisIndex = centerIdx;
                    current.Tier = 3;
                    current.IsRoadNode = true;
                    current.TradePower = 0.6f;
                    settlements[idx] = current;

                    // Рекурсия (уже для пригорода города)
                    SpreadOutpost(idx, centerIdx, graph, ref settlements, config, ref rng);

                } while (graph.TryGetNextValue(out n, ref it));
            }
        }

        private static void SpreadOutpost(
            int parentTownIdx,
            int metroIdx,
            NativeParallelMultiHashMap<int, NeighborInfo> graph,
            ref NativeArray<SettlementData> settlements,
            CivilizationConfig config,
            ref Random rng)
        {
            if (graph.TryGetFirstValue(parentTownIdx, out NeighborInfo n2, out var it2))
            {
                do
                {
                    int subIdx = n2.Index;
                    if (subIdx >= settlements.Length) continue;

                    var subCurrent = settlements[subIdx];
                    if (subCurrent.Type != SettlementType.Wilderness) continue;
                    
                    // Аванпосты менее требовательны к качеству, чем города.
                    // Берем порог из слайдера, но с коэффициентом 0.7 (немного мягче)
                    if (subCurrent.SuitabilityScore < (config.MinSuitability * 0.7f)) continue;

                    // Вероятность: Слайдер "OutpostSpawnChance"
                    if (rng.NextFloat() > config.OutpostSpawnChance) continue;

                    subCurrent.Type = SettlementType.Outpost;
                    subCurrent.MetropolisIndex = metroIdx;
                    subCurrent.Tier = 1;
                    settlements[subIdx] = subCurrent;

                } while (graph.TryGetNextValue(out n2, ref it2));
            }
        }
    }
}