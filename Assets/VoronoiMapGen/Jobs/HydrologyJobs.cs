using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
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
            int count = Cells.Length;
            
            // --- ЭТАП 1: ПОИСК СТОКА (Куда течет?) + ОПРЕДЕЛЕНИЕ ИСТОКОВ ---
            for (int i = 0; i < count; i++)
            {
                var tectonic = Tectonics[i];
                var climate = Climate[i];
                
                // --- Правило 1: Реки не начинаются ниже нуля ---
                if (tectonic.IsOcean || tectonic.BaseHeight <= 0.001f) 
                {
                    Hydrology[i] = new HydrologyData { IsOcean = true, FlowTargetIndex = -1, Flux = 0 };
                    continue;
                }

                // --- Аналитика Источников (откуда берется начальная вода/Flux) ---
                float initialFlux = 0f;

                // а) Дождь (Обычный сток)
                initialFlux += climate.Moisture * 0.5f;

                // б) Ледник (Высоко и Холодно -> таяние дает мощный исток)
                if (tectonic.BaseHeight > 0.8f && climate.Temperature < 0.35f)
                {
                    initialFlux += 2.0f; // Мощный старт
                }
                
                // в) Болото (Низко, влажно, не холодно) -> аккумулирует воду
                if (tectonic.BaseHeight < 0.3f && climate.Moisture > 0.7f)
                {
                    initialFlux += 1.0f;
                }

                // --- Поиск соседа ---
                int bestNeighbor = -1;
                float maxSlope = -1.0f; 

                // Читаем УЖЕ отфильтрованный граф (в котором нет далеких соседей)
                if (NeighborsMap.TryGetFirstValue(i, out NeighborInfo nInfo, out var it))
                {
                    do
                    {
                        // Не течем в самого себя или в глючные индексы
                        if (nInfo.Index >= count) continue;

                        float nHeight = Tectonics[nInfo.Index].BaseHeight;

                        // Если сосед океан - уровень берем 0 (чтобы гарантировать сток в море)
                        if (Tectonics[nInfo.Index].IsOcean) nHeight = 0;

                        if (nHeight < tectonic.BaseHeight)
                        {
                            float drop = tectonic.BaseHeight - nHeight;
                            // Дистанция уже есть в графе
                            float slope = drop / math.max(0.1f, nInfo.Distance); 

                            if (slope > maxSlope)
                            {
                                maxSlope = slope;
                                bestNeighbor = nInfo.Index;
                            }
                        }
                    } 
                    while (NeighborsMap.TryGetNextValue(out nInfo, ref it));
                }

                bool isLake = (bestNeighbor == -1); // Яма на суше

                Hydrology[i] = new HydrologyData 
                { 
                    FlowTargetIndex = bestNeighbor,
                    Flux = initialFlux,
                    IsOcean = false,
                    IsLake = isLake, 
                    LocalSlope = isLake ? 0 : maxSlope,
                    Type = RiverMorphology.Meandering 
                };
            }

            // --- ЭТАП 2: НАКОПЛЕНИЕ ВОДЫ ---
            var sortedIndices = new NativeArray<int>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) sortedIndices[i] = i;
            sortedIndices.Sort(new HeightComparer { Tectonics = Tectonics });

            for (int k = 0; k < count; k++)
            {
                int i = sortedIndices[k];
                var hSource = Hydrology[i];
                if (hSource.IsOcean) continue; // Океан никуда не течет
                
                // Если попали в озеро - вода останавливается (испаряется или образует озеро)
                if (hSource.IsLake) continue; 

                int targetIdx = hSource.FlowTargetIndex;
                if (targetIdx != -1)
                {
                    var hTarget = Hydrology[targetIdx];
                    // Если цель суша или озеро - передаем воду.
                    // Если цель океан - просто сбрасываем, но поток (Flux) источника сохраняется большим (чтобы нарисовать устье).
                    if (!hTarget.IsOcean)
                    {
                        hTarget.Flux += hSource.Flux;
                        
                        // Определяем тип русла на основе мощности
                        hTarget.StreamPower = hTarget.Flux * hTarget.LocalSlope; 
                        
                        if (hTarget.LocalSlope > 0.08f) hTarget.Type = RiverMorphology.MountainStream;
                        else if (hTarget.Flux > 30f && hTarget.LocalSlope < 0.015f) hTarget.Type = RiverMorphology.Meandering;
                        else hTarget.Type = RiverMorphology.Meandering;

                        Hydrology[targetIdx] = hTarget;
                    }
                    else
                    {
                        // Впадение в море -> это Дельта (условно)
                        if (hSource.Flux > 20f) 
                        {
                            hSource.Type = RiverMorphology.Delta;
                            Hydrology[i] = hSource; // Обновляем сам источник, т.к. цель (океан) мы не меняем
                        }
                    }
                }
            }
            
            // --- ЭТАП 3: ФЛАГИ VISIBILITY ---
            for(int i=0; i<count; i++)
            {
                var h = Hydrology[i];
                if (!h.IsOcean && !h.IsLake)
                {
                    // Рекой считается поток больше порогового
                    // Маленькие ручейки не рисуем
                    if (h.Flux > 2.0f) h.IsRiver = true; 
                    Hydrology[i] = h;
                }
            }
            sortedIndices.Dispose();
        }
        
        struct HeightComparer : System.Collections.Generic.IComparer<int>
        {
            [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
            public int Compare(int x, int y) => Tectonics[y].BaseHeight.CompareTo(Tectonics[x].BaseHeight);
        }
    }
}