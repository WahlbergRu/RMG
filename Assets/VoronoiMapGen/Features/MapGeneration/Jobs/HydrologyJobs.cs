using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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

        public NativeArray<HydrologyData> Hydrology;

        public void Execute()
        {
            var count = Cells.Length;

            // --- ЭТАП 1: ПОИСК СТОКА (Куда течет?) ---
            for (var i = 0; i < count; i++)
            {
                var tectonic = Tectonics[i];
                var climate = Climate[i];

                if (tectonic.IsOcean || tectonic.BaseHeight <= 0.001f)
                {
                    Hydrology[i] = new HydrologyData { IsOcean = true, FlowTargetIndex = -1, Flux = 0 };
                    continue;
                }

                // Базовый поток от осадков
                var initialFlux = climate.Moisture * 0.5f;
                if (tectonic.BaseHeight > 0.8f && climate.Temperature < 0.35f) initialFlux += 2.0f; // Ледник
                if (tectonic.BaseHeight < 0.3f && climate.Moisture > 0.7f) initialFlux += 1.0f; // Болото

                // --- 1. Поиск строгого спуска (Downhill) ---
                var bestNeighbor = -1;
                var maxSlope = -1.0f;

                // Переменные для запасного плана (Spillover)
                var lowestNeighborIndex = -1;
                var lowestNeighborHeight = float.MaxValue;

                if (NeighborsMap.TryGetFirstValue(i, out var nInfo, out var it))
                    do
                    {
                        if (nInfo.Index >= count) continue;

                        var nHeight = Tectonics[nInfo.Index].BaseHeight;
                        if (Tectonics[nInfo.Index].IsOcean) nHeight = 0;

                        // Поиск обычного спуска
                        if (nHeight < tectonic.BaseHeight)
                        {
                            var drop = tectonic.BaseHeight - nHeight;
                            var slope = drop / math.max(0.1f, nInfo.Distance);

                            if (slope > maxSlope)
                            {
                                maxSlope = slope;
                                bestNeighbor = nInfo.Index;
                            }
                        }

                        // Поиск самого низкого соседа (на случай ямы)
                        if (nHeight < lowestNeighborHeight)
                        {
                            lowestNeighborHeight = nHeight;
                            lowestNeighborIndex = nInfo.Index;
                        }
                    } while (NeighborsMap.TryGetNextValue(out nInfo, ref it));

                var isStuck = bestNeighbor == -1;

                // --- 2. Логика Перелива (Spillover) ---
                // Если мы застряли (нет спуска), но у нас есть соседи — течем в самого низкого из них.
                // Это симулирует наполнение озера и перелив через край.
                if (isStuck && lowestNeighborIndex != -1)
                {
                    bestNeighbor = lowestNeighborIndex;
                    // Уклон считаем нулевым или отрицательным, это "озерный" поток
                    maxSlope = 0;
                }

                Hydrology[i] = new HydrologyData
                {
                    FlowTargetIndex = bestNeighbor,
                    Flux = initialFlux,
                    IsOcean = false,
                    IsLake = isStuck, // Флаг, что здесь была яма
                    LocalSlope = maxSlope,
                    Type = RiverMorphology.Meandering
                };
            }

            // --- ЭТАП 2: НАКОПЛЕНИЕ ВОДЫ (FLUX) ---

            // Сортируем ячейки по высоте, чтобы вода текла сверху вниз.
            // Примечание: Spillover (течение вверх) ломает идеальную сортировку, 
            // поэтому вода в озерах может не накопить полный объем за один проход, 
            // но для визуализации связности этого достаточно.

            var sortedIndices = new NativeArray<int>(count, Allocator.Temp);
            for (var i = 0; i < count; i++) sortedIndices[i] = i;
            sortedIndices.Sort(new HeightComparer { Tectonics = Tectonics });

            for (var k = 0; k < count; k++)
            {
                var i = sortedIndices[k];
                var hSource = Hydrology[i];
                if (hSource.IsOcean) continue;

                var targetIdx = hSource.FlowTargetIndex;

                // --- ЗАЩИТА ОТ ЦИКЛОВ (Ping-Pong) ---
                // Если ячейка А течет в Б, а Б течет в А — это бесконечный цикл.
                // Просто не передаем flux, чтобы не было переполнения, но связь оставляем.
                if (targetIdx != -1)
                {
                    var hTarget = Hydrology[targetIdx];
                    if (hTarget.FlowTargetIndex == i)
                        // Обнаружен цикл с соседом! Прерываем накопление Flux, но оставляем геометрию.
                        continue;

                    if (!hTarget.IsOcean)
                    {
                        hTarget.Flux += hSource.Flux;

                        // Определяем тип русла
                        hTarget.StreamPower = hTarget.Flux * hTarget.LocalSlope;

                        if (hTarget.LocalSlope > 0.08f) hTarget.Type = RiverMorphology.MountainStream;
                        else hTarget.Type = RiverMorphology.Meandering;

                        Hydrology[targetIdx] = hTarget;
                    }
                    else
                    {
                        // Впадение в море
                        if (hSource.Flux > 20f)
                        {
                            hSource.Type = RiverMorphology.Delta;
                            Hydrology[i] = hSource;
                        }
                    }
                }
            }

            // --- ЭТАП 3: ФЛАГИ VISIBILITY ---
            for (var i = 0; i < count; i++)
            {
                var h = Hydrology[i];
                if (!h.IsOcean)
                {
                    // ПОРОГ РЕКИ
                    // Можно уменьшить до 1.0f или 0.5f, чтобы видеть больше мелких рек
                    if (h.Flux > 1.0f) h.IsRiver = true;
                    Hydrology[i] = h;
                }
            }

            sortedIndices.Dispose();
        }

        private struct HeightComparer : IComparer<int>
        {
            [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;

            public int Compare(int x, int y)
            {
                return Tectonics[y].BaseHeight.CompareTo(Tectonics[x].BaseHeight);
            }
        }
    }
}