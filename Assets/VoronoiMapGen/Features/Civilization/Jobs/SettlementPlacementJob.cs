using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Civilization.Jobs
{
    public struct PopRank : IComparable<PopRank>
    {
        public int Index;
        public float Population;
        public int CompareTo(PopRank other) => other.Population.CompareTo(Population);
    }

    public struct PlacedSettlement
    {
        public float2 Position;
        public SettlementType Type;
    }

    [BurstCompile]
    public struct SettlementPlacementJob : IJob
    {
        [ReadOnly] public NativeArray<DemographicsData> Demographics;
        [ReadOnly] public NativeArray<DetailLevelData> Levels;
        [ReadOnly] public NativeArray<VoronoiCell> Cells; 
        
        public NativeArray<SettlementData> Settlements;

        // ИСПРАВЛЕНЫ ИМЕНА ПЕРЕМЕННЫХ
        public int MinPopForOutpost; 
        public int MinPopForTown;    
        public int MinPopForMetropolis;    

        public float MetroExclusionRadius;
        public float TownExclusionRadius; 
        
        public void Execute()
        {
            int count = Demographics.Length;
            var ranks = new NativeArray<PopRank>(count, Allocator.Temp);

            // 1. Собираем кандидатов
            for (int i = 0; i < count; i++)
            {
                Settlements[i] = new SettlementData { Type = SettlementType.Wilderness }; 

                if (Levels[i].Level == DetailLevel.Global || Demographics[i].EstimatedPopulation < MinPopForOutpost)
                {
                    ranks[i] = new PopRank { Index = i, Population = -1 };
                }
                else
                {
                    ranks[i] = new PopRank { Index = i, Population = Demographics[i].EstimatedPopulation };
                }
            }

            ranks.Sort();

            var existing = new NativeList<PlacedSettlement>(128, Allocator.Temp);

            // 3. Жадная выборка
            for (int k = 0; k < count; k++)
            {
                int idx = ranks[k].Index;
                float pop = ranks[k].Population;

                if (pop < MinPopForOutpost) break; 

                float2 myPos = Cells[idx].Centroid;

                SettlementType desiredType = SettlementType.Outpost;
                if (pop >= MinPopForMetropolis) desiredType = SettlementType.Metropolis;
                else if (pop >= MinPopForTown) desiredType = SettlementType.Town;

                // --- SPATIAL FILTER ---
                bool blocked = false;

                for (int j = 0; j < existing.Length; j++)
                {
                    float dist = math.distance(myPos, existing[j].Position);
                    SettlementType neighborType = existing[j].Type;

                    if (neighborType == SettlementType.Metropolis)
                    {
                        if (dist < MetroExclusionRadius)
                        {
                            if (desiredType > SettlementType.Outpost) desiredType = SettlementType.Outpost;
                            if (dist < MetroExclusionRadius * 0.4f) blocked = true;
                        }
                    }
                    else if (neighborType == SettlementType.Town)
                    {
                        if (dist < TownExclusionRadius)
                        {
                            if (desiredType > SettlementType.Outpost) desiredType = SettlementType.Outpost;
                            if (dist < TownExclusionRadius * 0.5f) blocked = true;
                        }
                    }
                }

                if (blocked) continue;

                // Запись
                var s = Settlements[idx];
                s.Type = desiredType;
                s.Tier = (int)desiredType;
                s.IsRoadNode = desiredType >= SettlementType.Town;
                Settlements[idx] = s;

                if (desiredType >= SettlementType.Town)
                {
                    existing.Add(new PlacedSettlement { Position = myPos, Type = desiredType });
                }
            }

            ranks.Dispose();
            existing.Dispose();
        }
    }
}