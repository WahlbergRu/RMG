using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.Civilization.Jobs;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Civilization.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMapGen.Features.MapGeneration.Systems.MapGenerationSystem))] 
    public partial struct CivilizationSimulationSystem : ISystem
    {
        private EntityQuery _newCellsQuery;

        public void OnCreate(ref SystemState state)
        {
            _newCellsQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ClimateData>(),
                ComponentType.ReadOnly<HydrologyData>(),
                ComponentType.ReadOnly<TectonicPlateData>(),
                ComponentType.ReadOnly<CellBiome>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<DetailLevelData>(), 
                ComponentType.ReadWrite<DemographicsData>(),
                ComponentType.ReadWrite<SettlementData>(),
                ComponentType.ReadWrite<CalcDemographicsTag>()
            );
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_newCellsQuery.IsEmpty) return;

            state.EntityManager.CompleteDependencyBeforeRO<CellNeighbor>(); 

            int count = _newCellsQuery.CalculateEntityCount();
            
            var clim = _newCellsQuery.ToComponentDataArray<ClimateData>(Allocator.TempJob);
            var hydro = _newCellsQuery.ToComponentDataArray<HydrologyData>(Allocator.TempJob);
            var tect = _newCellsQuery.ToComponentDataArray<TectonicPlateData>(Allocator.TempJob);
            var bio = _newCellsQuery.ToComponentDataArray<CellBiome>(Allocator.TempJob);
            var levels = _newCellsQuery.ToComponentDataArray<DetailLevelData>(Allocator.TempJob);
            var voronoiCells = _newCellsQuery.ToComponentDataArray<VoronoiCell>(Allocator.TempJob);
            
            var demoData = new NativeArray<DemographicsData>(count, Allocator.TempJob);
            var settlementData = new NativeArray<SettlementData>(count, Allocator.TempJob);

            var calcJob = new DemographicsCalculationJob
            {
                Climate = clim,
                Hydrology = hydro,
                Tectonics = tect,
                Biomes = bio,
                Demographics = demoData,
                GlobalPopulationScalar = 3500f
            };
            
            JobHandle demoHandle = calcJob.Schedule(count, 64, state.Dependency);

            // --- ЗДЕСЬ БЫЛИ ОШИБКИ - ИСПРАВЛЕНО ---
            var placeJob = new SettlementPlacementJob
            {
                Demographics = demoData,
                Levels = levels,
                Cells = voronoiCells, 
                Settlements = settlementData,
                
                // Используем правильные имена полей:
                // Резко подняли пороги, чтобы убрать "ковёр" из деревень
                MinPopForOutpost = 3000,    // Было 200/800
                MinPopForTown = 10000,      // Было 2500
                MinPopForMetropolis = 25000, // Было 8000
                
                MetroExclusionRadius = 150f,  
                TownExclusionRadius = 60f    
            };

            JobHandle settleHandle = placeJob.Schedule(demoHandle);
            settleHandle.Complete(); 

            _newCellsQuery.CopyFromComponentDataArray(demoData);
            _newCellsQuery.CopyFromComponentDataArray(settlementData);

            var ents = _newCellsQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
                state.EntityManager.SetComponentEnabled<CalcDemographicsTag>(e, false);

            clim.Dispose(); hydro.Dispose(); tect.Dispose(); bio.Dispose(); levels.Dispose(); 
            voronoiCells.Dispose(); demoData.Dispose(); settlementData.Dispose(); ents.Dispose();
            
            state.Dependency = default;
        }
    }
}