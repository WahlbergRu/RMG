// ============================================================
// FILE: Assets\VoronoiMapGen\Features\MapGeneration\Jobs\HydrologyJobs.cs
// ============================================================
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components; // HydrologyConfig definition
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration.Jobs
{
    [BurstCompile]
    public struct CalculateHydrologyJob : IJob
    {
        [ReadOnly] public NativeArray<VoronoiCell> Cells;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        [ReadOnly] public NativeArray<ClimateData> Climate;
        [ReadOnly] public NativeParallelMultiHashMap<int, NeighborInfo> NeighborsMap;
        
        // Config injected
        public HydrologyConfig Config;

        public NativeArray<HydrologyData> Hydrology;

        public void Execute()
        {
            int count = Cells.Length;

            // --- ЭТАП 1: ПОИСК СТОКА (Куда течет?) ---
            for (int i = 0; i < count; i++)
            {
                TectonicPlateData tectonic = Tectonics[i];
                ClimateData climate = Climate[i];

                if (tectonic.IsOcean || tectonic.BaseHeight <= 0.001f)
                {
                    Hydrology[i] = new HydrologyData { IsOcean = true, FlowTargetIndex = -1, Flux = 0 };
                    continue;
                }

                // Flux based on Rain Intensity config
                float initialFlux = climate.Moisture * Config.RainIntensity; 
                if (tectonic.BaseHeight > 0.8f && climate.Temperature < 0.35f) initialFlux += 2.0f; 
                if (tectonic.BaseHeight < 0.3f && climate.Moisture > 0.7f) initialFlux += 1.0f;

                int bestNeighbor = -1;
                float maxSlope = -1.0f;
                int lowestNeighborIndex = -1;
                float lowestNeighborHeight = float.MaxValue;

                if (NeighborsMap.TryGetFirstValue(i, out NeighborInfo nInfo, out NativeParallelMultiHashMapIterator<int> it))
                    do
                    {
                        if (nInfo.Index >= count) continue;
                        float nHeight = Tectonics[nInfo.Index].BaseHeight;
                        if (Tectonics[nInfo.Index].IsOcean) nHeight = 0;

                        if (nHeight < tectonic.BaseHeight)
                        {
                            float drop = tectonic.BaseHeight - nHeight;
                            float slope = drop / math.max(0.1f, nInfo.Distance);
                            if (slope > maxSlope) { maxSlope = slope; bestNeighbor = nInfo.Index; }
                        }
                        if (nHeight < lowestNeighborHeight) { lowestNeighborHeight = nHeight; lowestNeighborIndex = nInfo.Index; }

                    } while (NeighborsMap.TryGetNextValue(out nInfo, ref it));

                bool isStuck = bestNeighbor == -1;
                if (isStuck && lowestNeighborIndex != -1)
                {
                    bestNeighbor = lowestNeighborIndex;
                    maxSlope = 0;
                }

                Hydrology[i] = new HydrologyData
                {
                    FlowTargetIndex = bestNeighbor,
                    Flux = initialFlux,
                    IsOcean = false,
                    IsLake = isStuck, 
                    LocalSlope = maxSlope,
                    Type = RiverMorphology.Meandering
                };
            }

            // --- ЭТАП 2: НАКОПЛЕНИЕ ВОДЫ ---
            NativeArray<int> sortedIndices = new NativeArray<int>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) sortedIndices[i] = i;
            sortedIndices.Sort(new HeightComparer { Tectonics = Tectonics });

            for (int k = 0; k < count; k++)
            {
                int i = sortedIndices[k];
                HydrologyData hSource = Hydrology[i];
                if (hSource.IsOcean) continue;

                int targetIdx = hSource.FlowTargetIndex;
                if (targetIdx != -1)
                {
                    HydrologyData hTarget = Hydrology[targetIdx];
                    if (hTarget.FlowTargetIndex == i) continue;

                    if (!hTarget.IsOcean)
                    {
                        hTarget.Flux += hSource.Flux;
                        hTarget.StreamPower = hTarget.Flux * hTarget.LocalSlope;
                        if (hTarget.LocalSlope > 0.08f) hTarget.Type = RiverMorphology.MountainStream;
                        else hTarget.Type = RiverMorphology.Meandering;
                        Hydrology[targetIdx] = hTarget;
                    }
                    else
                    {
                        if (hSource.Flux > 20f) { hSource.Type = RiverMorphology.Delta; Hydrology[i] = hSource; }
                    }
                }
            }

            // --- ЭТАП 3: ФЛАГИ VISIBILITY ---
            for (int i = 0; i < count; i++)
            {
                HydrologyData h = Hydrology[i];
                if (!h.IsOcean)
                {
                    // ИСПОЛЬЗУЕМ КОНФИГ ПРИ РЕШЕНИИ "ЯВЛЯЕТСЯ ЛИ РЕКОЙ"
                    if (h.Flux > Config.RiverFluxThreshold) h.IsRiver = true;
                    Hydrology[i] = h;
                }
            }
            sortedIndices.Dispose();
        }

        private struct HeightComparer : IComparer<int>
        {
            [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
            public int Compare(int x, int y) { return Tectonics[y].BaseHeight.CompareTo(Tectonics[x].BaseHeight); }
        }
    }
}