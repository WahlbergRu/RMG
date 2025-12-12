// ============================================================
// FILE: Assets\VoronoiMapGen\Features\MapGeneration\MapGenerationSystem.cs
// ============================================================
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.MapGeneration.Jobs;
using VoronoiMapGen.Features.Utils;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Utils; 
using VoronoiMapGen.Utils;
using Unity.Transforms;

namespace VoronoiMapGen.Features.MapGeneration.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private enum GenPhase
        {
            Init,
            LoadOrGenerate,
            Gen_Sites,
            Gen_Relaxation_Iter,
            Gen_Simulation,
            Gen_FinalizeGeo,
            Batch_Cells_Init, Batch_Cells_Run,
            Batch_Edges_Init, Batch_Edges_Run,
            FinishLevel,
            AllComplete,
            ErrorState
        }

        private GenPhase _phase = GenPhase.Init;
        private MapHistoryData _history;
        private MapGenSession _session;

        private NativeArray<LevelSettings> _levelSettings;
        private MapSettings _settings;
        private int _currentLevel = 0;
        private int _relaxIterCurrent = 0;
        private int _spawnedCount = 0;
        
        // Batch limits
        private const int CELL_BATCH = 200; 
        private const int EDGE_BATCH = 5000; // Дороги спавнятся большими пачками, т.к. они простые

        private EntityArchetype _cellArchetype;
        private EntityArchetype _edgeArchetype;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
            RequireForUpdate<GenerationStatus>();
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            if (_levelSettings.IsCreated) _levelSettings.Dispose();
            if (_history != null) _history.Dispose();
            if (_session != null) _session.Dispose();
        }

        protected override void OnUpdate()
        {
            var statusEntity = SystemAPI.GetSingletonEntity<GenerationStatus>();
            var status = SystemAPI.GetComponent<GenerationStatus>(statusEntity);

            if (_phase == GenPhase.AllComplete || status.IsCompleted) return;
            if (_phase == GenPhase.ErrorState) return;

            try 
            {
                switch (_phase)
                {
                    case GenPhase.Init:
                        DoInitialize();
                        UpdateUI(ref status, 0, "System Initialization...");
                        _phase = GenPhase.LoadOrGenerate;
                        break;

                    case GenPhase.LoadOrGenerate:
                        if (_currentLevel >= _levelSettings.Length) {
                            _phase = GenPhase.AllComplete;
                            DoComplete(ref status);
                            break; 
                        }
                        
                        _session = new MapGenSession(_currentLevel);
                        _phase = GenPhase.Gen_Sites;
                        UpdateUI(ref status, GetGlobalP(0), $"L{_currentLevel}: Sites...");
                        break;

                    case GenPhase.Gen_Sites:
                        MapGenAlgorithms.GenerateSites(_session, _settings, _levelSettings[_currentLevel], _history);
                        MapGenAlgorithms.BuildGeometry(_session, _settings.MapSize);
                        _relaxIterCurrent = 0;
                        _phase = GenPhase.Gen_Relaxation_Iter;
                        break;

                    case GenPhase.Gen_Relaxation_Iter:
                        int maxIter = _levelSettings[_currentLevel].RelaxationIterations;
                        if (_relaxIterCurrent < maxIter) {
                            MapGenAlgorithms.RelaxSites(_session, _settings.MapSize);
                            MapGenAlgorithms.BuildGeometry(_session, _settings.MapSize);
                            _relaxIterCurrent++;
                            float p = (float)_relaxIterCurrent / (maxIter + 1);
                            UpdateUI(ref status, GetGlobalP(p * 0.1f), $"L{_currentLevel}: Shaping ({_relaxIterCurrent})");
                        } else {
                            _phase = GenPhase.Gen_Simulation;
                        }
                        break;

                    case GenPhase.Gen_Simulation:
                        UpdateUI(ref status, GetGlobalP(0.15f), $"L{_currentLevel}: Eco Simulation...");
                        MapGenAlgorithms.RunSimulation(_session, _settings, _history);
                        _phase = GenPhase.Gen_FinalizeGeo;
                        break;

                    case GenPhase.Gen_FinalizeGeo:
                        MapGenAlgorithms.FinalizeEdgesAndCells(_session);
                        _session.PrepareForBatching();
                        SetupParentSearch(_currentLevel - 1);
                        _spawnedCount = 0;
                        _phase = GenPhase.Batch_Cells_Init;
                        break;

                    case GenPhase.Batch_Cells_Init:
                        _phase = GenPhase.Batch_Cells_Run;
                        UpdateUI(ref status, GetGlobalP(0.2f), $"L{_currentLevel}: Cells Spawning...");
                        break;

                    case GenPhase.Batch_Cells_Run:
                        int processed = SpawnCellsBatch(CELL_BATCH);
                        float progress = (float)processed / math.max(1, _session.FinalCells.Length);
                        UpdateUI(ref status, GetGlobalP(0.2f + progress * 0.6f), $"L{_currentLevel}: Cells {(int)(progress*100)}%");
                        if (processed >= _session.FinalCells.Length) {
                            _spawnedCount = 0;
                            _phase = GenPhase.Batch_Edges_Init;
                        }
                        break;

                    case GenPhase.Batch_Edges_Init:
                        // === ПРОВЕРКА НАСТРОЕК (L1, L2 ON; L3, L4 OFF) ===
                        if (_levelSettings[_currentLevel].GenerateRoads == 0)
                        {
                            UpdateUI(ref status, GetGlobalP(0.9f), $"L{_currentLevel}: Roads Skipped");
                            _phase = GenPhase.FinishLevel;
                        }
                        else
                        {
                            _spawnedCount = 0;
                            _phase = GenPhase.Batch_Edges_Run;
                            UpdateUI(ref status, GetGlobalP(0.85f), $"L{_currentLevel}: Linking Roads...");
                        }
                        break;

                    case GenPhase.Batch_Edges_Run:
                        // Теперь SpawnEdgesBatch умный и отфильтрует ненужное
                        int eProcessed = SpawnEdgesBatch(EDGE_BATCH);
                        if (eProcessed >= _session.Edges.Length) {
                            _phase = GenPhase.FinishLevel;
                        }
                        break;

                    case GenPhase.FinishLevel:
                        _history.StoreLevel(_session.ToLevelData());
                        _session.ReleaseSimulationOwnership();
                        _session.Dispose(); 
                        _session = null;

                        // Очистка мусора чтобы память не текла между уровнями
                        System.GC.Collect(); 

                        _currentLevel++;
                        _phase = GenPhase.LoadOrGenerate; 
                        break;
                }

                if(status.ProcessedLevels != _currentLevel && _phase != GenPhase.AllComplete)
                    status.ProcessedLevels = _currentLevel;
                
                SystemAPI.SetComponent(statusEntity, status);
            }
            catch(System.Exception ex)
            {
                Debug.LogException(ex);
                _phase = GenPhase.ErrorState;
                UpdateUI(ref status, 0, "ERROR: See Console");
                SystemAPI.SetComponent(statusEntity, status);
            }
        }

        private float GetGlobalP(float localP) {
            float w = 1.0f / math.max(1, _levelSettings.Length);
            return (_currentLevel * w) + (localP * w);
        }

        private int SpawnCellsBatch(int limit)
        {
            if(!_session.FinalCells.IsCreated) return _spawnedCount; 
            int total = _session.FinalCells.Length;
            int count = math.min(limit, total - _spawnedCount);
            if (count <= 0) return _spawnedCount;

            using var ents = EntityManager.CreateEntity(_cellArchetype, count, Allocator.Temp);
            var settings = _levelSettings[_currentLevel];
            var mSize = _settings.MapSize;

            for (int k = 0; k < count; k++)
            {
                int i = _spawnedCount + k;
                Entity e = ents[k];
                var cell = _session.FinalCells[i];
                var meta = _session.Meta[i];
                if (_session.ParentEntityMap.TryGetValue(meta.ParentIndex, out Entity pE)) cell.ParentEntity = pE;

                EntityManager.SetComponentData(e, cell);
                EntityManager.SetComponentData(e, meta);
                EntityManager.SetComponentData(e, new DetailLevelData { Level=(DetailLevel)_currentLevel, LODThreshold=settings.LODThreshold, RenderThreshold=settings.RenderThreshold });
                
                EntityManager.SetComponentData(e, _session.Tectonics[i]);
                EntityManager.SetComponentData(e, _session.Climate[i]);
                EntityManager.SetComponentData(e, _session.Hydrology[i]);
                EntityManager.SetComponentData(e, _session.Biomes[i]);
                if(_session.Settlements.IsCreated) EntityManager.SetComponentData(e, _session.Settlements[i]);
                if(_session.Districts.IsCreated) EntityManager.SetComponentData(e, _session.Districts[i]);
                
                EntityManager.SetComponentData(e, new DemographicsData());
                EntityManager.SetComponentData(e, new CellBiome { 
                    Type=_session.Biomes[i].Type, Elevation=_session.Tectonics[i].BaseHeight,
                    Moisture=_session.Climate[i].Moisture, Temperature=_session.Climate[i].Temperature
                });
                EntityManager.SetComponentData(e, LocalTransform.FromPosition(meta.Position.x, 0, meta.Position.y));

                var vb = EntityManager.GetBuffer<CellPolygonVertex>(e);
                var tb = EntityManager.GetBuffer<CellTriIndex>(e);
                CellGeometryBuilder.BuildPolygonForCell(vb, tb, cell, _session.PolyMap, mSize);
            }
            _spawnedCount += count;
            return _spawnedCount;
        }

        private int SpawnEdgesBatch(int limit)
        {
            if(!_session.Edges.IsCreated) return _spawnedCount;
            int total = _session.Edges.Length;
            
            // Если все проверили - выходим
            if (_spawnedCount >= total) return total; 

            // Считаем сколько проверить за кадр
            int count = math.min(limit, total - _spawnedCount);
            if (count <= 0) return _spawnedCount;

            // --- 1. ФИЛЬТРАЦИЯ СПИСКА (L2 LIMIT) ---
            NativeList<VoronoiEdge> validEdges = new NativeList<VoronoiEdge>(count, Allocator.Temp);
            
            for (int k = 0; k < count; k++)
            {
                var edge = _session.Edges[_spawnedCount + k];
                
                bool isGood = false;
                
                // Проверка валидности геометрии
                if(math.lengthsq(edge.VertexA) > 0.01f)
                {
                    // Длина квадрата ребра
                    float distSq = math.distancesq(edge.VertexA, edge.VertexB);
                    
                    if (_currentLevel == 1) // L1 (Region) - Берем почти все
                    {
                        if(distSq > 10f) isGood = true;
                    }
                    else if (_currentLevel == 2) // L2 (Settlement) - ФИЛЬТР ПО ДЛИНЕ
                    {
                        // Не слишком длинные и не слишком короткие
                        // 1500 это около 38 юнитов (sqrt(1500) = 38)
                        if (distSq > 10f && distSq < 1500f) isGood = true; 
                    }
                    else
                    {
                        // Для L0, L3+ - пускаем все валидные (если они не отключены флагом)
                        // Но так как L3 выключен флагом, мы сюда не попадем
                        // isGood = true; 
                    }
                }

                if(isGood) validEdges.Add(edge);
            }

            // --- 2. МАССОВЫЙ СПАВН ВАЛИДНЫХ ---
            if(validEdges.Length > 0)
            {
                using var ents = EntityManager.CreateEntity(_edgeArchetype, validEdges.Length, Allocator.Temp);
                for (int i=0; i<validEdges.Length; i++) 
                {
                    var ent = ents[i];
                    var edge = validEdges[i];
                    edge.CellA = Entity.Null; edge.CellB = Entity.Null;
                    
                    EntityManager.SetComponentData(ent, edge);
                    EntityManager.SetComponentData(ent, new DetailLevelData{Level=(DetailLevel)_currentLevel});
                }
            }
            
            // Двигаем курсор
            _spawnedCount += count;
            
            validEdges.Dispose();
            return _spawnedCount;
        }

        private void SetupParentSearch(int prevLevel)
        {
            if (prevLevel < 0) return;
            if(!_session.ParentEntityMap.IsCreated) 
                 _session.ParentEntityMap = new NativeParallelHashMap<int, Entity>(1000, Allocator.Persistent);

            using var q = EntityManager.CreateEntityQuery(typeof(VoronoiSite));
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var sites = q.ToComponentDataArray<VoronoiSite>(Allocator.Temp);
            for(int i=0; i<ents.Length; i++) 
                if(sites[i].Level == prevLevel) _session.ParentEntityMap.TryAdd(sites[i].Index, ents[i]);
        }

        private void DoInitialize() 
        { 
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            
            if(EntityManager.HasComponent<MapGeneratedTag>(settingsEntity))
                EntityManager.RemoveComponent<MapGeneratedTag>(settingsEntity);

            _settings = SystemAPI.GetSingleton<MapSettings>();
            var buf = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            _levelSettings = buf.ToNativeArray(Allocator.Persistent);
            
            _history = new MapHistoryData(_levelSettings.Length);
            
            if(!EntityManager.HasComponent<MapGenerationInProgress>(settingsEntity))
                EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            
            _currentLevel = 0; 
            _phase = GenPhase.Init;

            _cellArchetype = EntityManager.CreateArchetype(
                typeof(VoronoiCell), typeof(VoronoiSite), typeof(DetailLevelData), typeof(LocalTransform), typeof(LocalToWorld),
                typeof(CellPolygonVertex), typeof(CellTriIndex), 
                typeof(TectonicPlateData), typeof(ClimateData), typeof(BiomeData),
                typeof(CellBiome), typeof(HydrologyData), typeof(CellNeighbor), 
                typeof(DemographicsData), typeof(SettlementData), typeof(CalcDemographicsTag), typeof(DistrictData) 
            );
            _edgeArchetype = EntityManager.CreateArchetype(typeof(VoronoiEdge), typeof(DetailLevelData), typeof(LocalToWorld), typeof(BorderEntityTag));
        }
        
        private void DoComplete(ref GenerationStatus s) {
            s.IsCompleted=true; s.TotalProgress=1f; s.CurrentStepName="Done";
            EntityManager.AddComponent<MapGeneratedTag>(SystemAPI.GetSingletonEntity<MapSettings>());
            EntityManager.RemoveComponent<MapGenerationInProgress>(SystemAPI.GetSingletonEntity<MapSettings>());
            
            Debug.Log($"Generation Complete! Created Levels: {_currentLevel}");
        }
        
        private void UpdateUI(ref GenerationStatus s, float progress, string txt) {
            s.TotalProgress = math.clamp(progress, 0, 1); s.CurrentStepName = txt;
        }
    }
}