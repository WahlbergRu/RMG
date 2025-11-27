using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct BuildNeighborGraphJob : IJob
    {
        [ReadOnly] public NativeList<VoronoiEdge> Edges;
        public int SiteCount;
        public NativeParallelMultiHashMap<int, int> NeighborsMap;

        public void Execute()
        {
            NeighborsMap.Clear();
            for (int i = 0; i < Edges.Length; i++)
            {
                var edge = Edges[i];
                if (edge.SiteA >= 0 && edge.SiteB >= 0)
                {
                    NeighborsMap.Add(edge.SiteA, edge.SiteB);
                    NeighborsMap.Add(edge.SiteB, edge.SiteA);
                }
            }
        }
    }

    [BurstCompile]
    public struct CalculateHydrologyJob : IJob
    {
        [ReadOnly] public NativeArray<VoronoiCell> Cells;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics; 
        [ReadOnly] public NativeArray<ClimateData> Climate;         
        [ReadOnly] public NativeParallelMultiHashMap<int, int> NeighborsMap;
        
        public NativeArray<HydrologyData> Hydrology;

        public void Execute()
        {
            int count = Cells.Length;
            float seaLevel = 0.2f; 
            float maxFlowDistSq = 200.0f * 200.0f; // Чуть увеличил дистанцию

            // А. СТОК
            for (int i = 0; i < count; i++)
            {
                float myHeight = Tectonics[i].BaseHeight;
                bool isUnderwater = Tectonics[i].IsOcean || myHeight < seaLevel;

                if (isUnderwater)
                {
                    Hydrology[i] = new HydrologyData { IsOcean = true, FlowTargetIndex = -1, Flux = 0 };
                    continue;
                }

                int myIndex = Cells[i].SiteIndex;
                float2 myPos = Cells[i].Centroid;
                
                int lowestNeighbor = -1;
                float lowestHeight = myHeight;

                if (NeighborsMap.TryGetFirstValue(myIndex, out int neighborIdx, out var it))
                {
                    do
                    {
                        if (neighborIdx >= count) continue;
                        
                        // Проверка на слишком длинные прыжки
                        float2 nPos = Cells[neighborIdx].Centroid;
                        if (math.distancesq(myPos, nPos) > maxFlowDistSq) continue;

                        float nHeight = Tectonics[neighborIdx].BaseHeight;
                        
                        if (nHeight < lowestHeight)
                        {
                            lowestHeight = nHeight;
                            lowestNeighbor = neighborIdx;
                        }
                    } 
                    while (NeighborsMap.TryGetNextValue(out neighborIdx, ref it));
                }

                Hydrology[i] = new HydrologyData 
                { 
                    FlowTargetIndex = lowestNeighbor,
                    Flux = Climate[i].Moisture, // Стартуем с дождевой воды
                    IsOcean = false,
                    IsLake = (lowestNeighbor == -1)
                };
            }

            // Б. НАКОПЛЕНИЕ (Сортировка от гор к морю)
            var sortedIndices = new NativeArray<int>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) sortedIndices[i] = i;
            sortedIndices.Sort(new HeightComparer { Tectonics = Tectonics });

            for (int k = 0; k < count; k++)
            {
                int i = sortedIndices[k];
                var hydro = Hydrology[i];

                if (hydro.IsOcean) continue;
                
                if (hydro.FlowTargetIndex != -1)
                {
                    var targetHydro = Hydrology[hydro.FlowTargetIndex];
                    if (!targetHydro.IsOcean)
                    {
                        // Вся вода сверху + своя передается вниз
                        targetHydro.Flux += hydro.Flux;
                        Hydrology[hydro.FlowTargetIndex] = targetHydro;
                    }
                }
            }

            // В. ОПРЕДЕЛЕНИЕ РЕК
            for (int i = 0; i < count; i++)
            {
                var h = Hydrology[i];
                
                // --- ИЗМЕНЕНИЕ: Снижаем порог с 5.0 до 0.8 ---
                // Теперь даже мелкие ручьи считаются реками, но будут тонкими
                if (!h.IsOcean && h.Flux > 0.8f) 
                {
                    h.IsRiver = true;
                }
                Hydrology[i] = h;
            }
            
            sortedIndices.Dispose();
        }
        
        struct HeightComparer : System.Collections.Generic.IComparer<int>
        {
            [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
            public int Compare(int x, int y)
            {
                // Сортировка по убыванию высоты
                return Tectonics[y].BaseHeight.CompareTo(Tectonics[x].BaseHeight);
            }
        }
    }
    
    // Добавьте эту структуру в конец файла HydrologyJobs.cs
    [BurstCompile]
    public struct ApplyRiverBiomesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<HydrologyData> Hydrology;
        public NativeArray<BiomeData> Biomes; // Мы будем менять биомы

        public void Execute(int i)
        {
            var h = Hydrology[i];
            var b = Biomes[i];

            // Не трогаем океан и побережье
            if (b.Type == BiomeType.Ocean || b.Type == BiomeType.Coast) return;

            // Если здесь течет река или озеро
            if (h.IsRiver || h.IsLake)
            {
                // Превращаем пустыню и степь в Лес или Траву
                if (b.Type == BiomeType.Desert) b.Type = BiomeType.Grassland;
                else if (b.Type == BiomeType.Grassland) b.Type = BiomeType.Forest;
            }

            Biomes[i] = b;
        }
    }
}