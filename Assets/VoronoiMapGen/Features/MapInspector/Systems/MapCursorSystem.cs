using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components; 
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.MapInspector.Components;

namespace VoronoiMapGen.Features.MapInspector.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MapCursorSystem : SystemBase
    {
        private EntityQuery _cellQuery;

        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<MapCursorData>())
            {
                var e = EntityManager.CreateEntity(typeof(MapCursorData));
                EntityManager.SetComponentData(e, new MapCursorData { HoveredCellIndex = -1 });
            }
            _cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
        }

        protected override void OnUpdate()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            float terrainScale = 50f; 
            if (SystemAPI.TryGetSingleton<MapSettings>(out var settings)) 
                terrainScale = settings.TerrainHeightScale;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.up, Vector3.zero); 

            if (!SystemAPI.TryGetSingletonRW<MapCursorData>(out RefRW<MapCursorData> cursorDataRW)) return;
            ref var cursorData = ref cursorDataRW.ValueRW;

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hitPointFloor = ray.GetPoint(enter);
                float2 cursorPos2D = new float2(hitPointFloor.x, hitPointFloor.z);
                
                // Сброс флага "Грязный" только если мышка не двигалась? 
                // Нет, лучше сбрасывать всегда, и ставить true если поменялась ячейка.
                cursorData.IsDirty = false;

                if (_cellQuery.IsEmpty) return;

                var cells = _cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.TempJob);
                var entities = _cellQuery.ToEntityArray(Allocator.TempJob); 
                var resultIndex = new NativeArray<int>(1, Allocator.TempJob);
                resultIndex[0] = -1;

                var job = new FindHoveredCellJob
                {
                    Cells = cells,
                    CursorPos = cursorPos2D,
                    MaxDistSq = 5000f * 5000f, 
                    Result = resultIndex
                };
                job.Schedule().Complete(); 

                int idx = resultIndex[0];
                resultIndex.Dispose();
                cells.Dispose();

                if (idx != -1)
                {
                    Entity targetEntity = entities[idx];
                    if (EntityManager.Exists(targetEntity))
                    {
                        // Рисуем дебаг линию (удобно оставить)
                        var targetCell = EntityManager.GetComponentData<VoronoiCell>(targetEntity);
                        
                        float elevation = 0;
                        if (EntityManager.HasComponent<CellBiome>(targetEntity))
                            elevation = EntityManager.GetComponentData<CellBiome>(targetEntity).Elevation;

                        float visualY = 1.0f + math.pow(math.max(0, elevation), 1.5f) * terrainScale;
                        Vector3 cellTop = new Vector3(targetCell.Centroid.x, visualY, targetCell.Centroid.y);
                        
                        // Дебаг: зеленая линия к мышке, красная - центр ячейки
                        Debug.DrawLine(hitPointFloor, cellTop, new Color(0,1,0,0.5f)); 
                        Debug.DrawRay(cellTop, Vector3.up * 10f, Color.red);

                        // ОБНОВЛЕНИЕ ДАННЫХ
                        if (cursorData.HoveredCellIndex != idx)
                        {
                            cursorData.IsHovering = true;
                            cursorData.IsDirty = true; // Триггер для UI
                            cursorData.HoveredCellIndex = idx;
                            
                            // Сохраняем 3D позицию для крепления UI в будущем (если захочешь Floating UI)
                            cursorData.HoveredPosition = new float3(cellTop.x, cellTop.y, cellTop.z); 

                            // ЗАПОЛНЕНИЕ ПОЛЕЙ
                            UpdateCursorData(ref cursorData, targetEntity);
                        }
                    }
                }
                else
                {
                    if (cursorData.IsHovering) {
                        cursorData.IsDirty = true;
                        cursorData.IsHovering = false;
                    }
                }
                entities.Dispose();
            }
        }


        private void UpdateCursorData(ref MapCursorData data, Entity e)
        {
            // 1. БАЗОВЫЕ ДАННЫЕ
            if (EntityManager.HasComponent<VoronoiCell>(e)) {
                var cell = EntityManager.GetComponentData<VoronoiCell>(e);
                data.CellID = cell.SiteIndex;
            }
            if (EntityManager.HasComponent<VoronoiSite>(e)) {
                var site = EntityManager.GetComponentData<VoronoiSite>(e);
                data.ParentID = site.ParentIndex;
                data.LevelIndex = site.Level;
            }

            // 2. ГЕОГРАФИЯ
            if (EntityManager.HasComponent<CellBiome>(e)) {
                var b = EntityManager.GetComponentData<CellBiome>(e);
                data.CachedBiome = b.Type;
                data.CachedElevation = b.Elevation;
            }
            if (EntityManager.HasComponent<HydrologyData>(e)) {
                var h = EntityManager.GetComponentData<HydrologyData>(e);
                data.IsRiver = h.IsRiver;
                data.IsOcean = h.IsOcean;
            }

            // --- НОВОЕ: Читаем климат ---
            if (EntityManager.HasComponent<ClimateData>(e))
            {
                var c = EntityManager.GetComponentData<ClimateData>(e);
                data.Temperature = c.Temperature; // храним 0..1
                data.Moisture = c.Moisture;       // храним 0..1
            }
            else
            {
                data.Temperature = 0.5f;
                data.Moisture = 0.5f;
            }
            // ---------------------------

            // 3. ЦИВИЛИЗАЦИЯ
            if (EntityManager.HasComponent<SettlementData>(e)) {
                var s = EntityManager.GetComponentData<SettlementData>(e);
                data.CachedSettlement = s.Type;
                data.CachedScore = s.SuitabilityScore;
            } else {
                data.CachedSettlement = SettlementType.Wilderness;
            }

            // 4. НАСЕЛЕНИЕ
            if(EntityManager.HasComponent<DemographicsData>(e)) { 
                var d = EntityManager.GetComponentData<DemographicsData>(e);
                data.CachedPopulation = d.EstimatedPopulation;
                data.CachedFertility = d.FoodYield;
            } else {
                data.CachedPopulation = 0;
            }
        }
    }

    [BurstCompile]
    public struct FindHoveredCellJob : IJob
    {
        [ReadOnly] public NativeArray<VoronoiCell> Cells;
        public float2 CursorPos;
        public float MaxDistSq;
        public NativeArray<int> Result; 

        public void Execute()
        {
            float minDist = MaxDistSq;
            int bestIdx = -1;
            for (int i = 0; i < Cells.Length; i++) {
                float d = math.distancesq(CursorPos, Cells[i].Centroid);
                if (d < minDist) { minDist = d; bestIdx = i; }
            }
            Result[0] = bestIdx;
        }
    }
}