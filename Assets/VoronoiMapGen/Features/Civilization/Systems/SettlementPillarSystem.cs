// ============================================================
// FILE: Assets\VoronoiMapGen\Features\Civilization\Systems\SettlementPillarSystem.cs
// ============================================================
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components; 
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Civilization.Systems
{
    public struct SettlementMarkerTag : IComponentData { }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SettlementPillarSystem : SystemBase
    {
        private Mesh _mesh;
        private Material _matMetropolis, _matTown, _matOutpost;
        private RenderMeshArray _renderArrMetropolis, _renderArrTown, _renderArrOutpost;

        private struct SpawnData {
            public Entity SourceEntity; public float3 Position; public float Width; public float Height; public int MatIndex;
        }

        protected override void OnCreate()
        {
            // Создаем Куб
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(temp);
            _mesh.name = "CivMarkerCube";
            
            // Создаем 3 материала
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            
            _matMetropolis = new Material(s) { color = new Color(1f, 0.2f, 0.2f), enableInstancing = true }; // Red
            _matTown = new Material(s) { color = new Color(1f, 0.6f, 0f), enableInstancing = true };       // Orange
            _matOutpost = new Material(s) { color = new Color(0.4f, 1f, 0.4f), enableInstancing = true };  // Green

            _renderArrMetropolis = new RenderMeshArray(new[] { _matMetropolis }, new[] { _mesh });
            _renderArrTown = new RenderMeshArray(new[] { _matTown }, new[] { _mesh });
            _renderArrOutpost = new RenderMeshArray(new[] { _matOutpost }, new[] { _mesh });
        }

        protected override void OnDestroy()
        {
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_matMetropolis) Object.DestroyImmediate(_matMetropolis);
            if (_matTown) Object.DestroyImmediate(_matTown);
            if (_matOutpost) Object.DestroyImmediate(_matOutpost);
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (!settings.ShowSettlements) return;

            // Нужно найти существующие маркеры и проверить, не скрыл ли пользователь поселения
            // Но в данном простом примере мы просто спавним один раз
            // (Логика удаления реализована через ShowSettlements check в MapGeneratorBootstrap.ResetVisualization)

            var spawnList = new List<SpawnData>();

            Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            var styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity).ToNativeArray(Allocator.Temp);

            foreach (var (settlement, cell, biome, level, entity) in 
                     SystemAPI.Query<RefRO<SettlementData>, RefRO<VoronoiCell>, RefRO<CellBiome>, RefRO<DetailLevelData>>()
                     .WithNone<SettlementMarkerTag>() 
                     .WithEntityAccess())
            {
                var type = settlement.ValueRO.Type;
                if (type == SettlementType.Wilderness) continue;

                // --- НАСТРОЙКА РАЗМЕРОВ (УМЕНЬШЕНО) ---
                float width = 2f; 
                float height = 5f; 
                int matType = 0; 

                switch (type)
                {
                    case SettlementType.Metropolis: 
                        // Было: 40, 150
                        width = 14f; 
                        height = 60f; 
                        matType = 0; 
                        break;

                    case SettlementType.Town:       
                        // Было: 25, 80
                        width = 8f; 
                        height = 25f;  
                        matType = 1; 
                        break;

                    case SettlementType.Outpost:    
                        // Было: 15, 30
                        width = 4f; 
                        height = 8f;   
                        matType = 2; 
                        break;
                }

                // Расчет высоты земли
                int lvlIdx = Mathf.Clamp((int)level.ValueRO.Level, 0, styles.Length - 1);
                var style = styles[lvlIdx];
                
                float groundY;
                if (biome.ValueRO.Type == BiomeType.Ocean) 
                    groundY = 0.5f; 
                else
                    groundY = 1.0f + math.pow(math.max(0, biome.ValueRO.Elevation), 1.5f) * style.HeightScale;

                // Центр куба = земля + половина высоты
                float3 pos = new float3(cell.ValueRO.Centroid.x, groundY + (height * 0.5f), cell.ValueRO.Centroid.y);

                spawnList.Add(new SpawnData { SourceEntity = entity, Position = pos, Width = width, Height = height, MatIndex = matType });
            }
            styles.Dispose();

            if (spawnList.Count > 0)
            {
                foreach (var data in spawnList)
                {
                    Entity instance = EntityManager.CreateEntity();
                    
                    var renderArr = data.MatIndex == 0 ? _renderArrMetropolis : 
                                    data.MatIndex == 1 ? _renderArrTown : _renderArrOutpost;

                    RenderMeshUtility.AddComponents(instance, EntityManager, 
                        new RenderMeshDescription(ShadowCastingMode.On), 
                        renderArr, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

                    EntityManager.AddComponentData(instance, LocalTransform.FromPositionRotation(data.Position, quaternion.identity));
                    
                    // Non-Uniform Scale для создания формы "столбика"
                    float4x4 m = float4x4.Scale(data.Width, data.Height, data.Width);
                    EntityManager.AddComponentData(instance, new PostTransformMatrix { Value = m });

                    EntityManager.SetComponentData(instance, new RenderBounds { Value = new AABB { Center = float3.zero, Extents = new float3(100, 100, 100) } });

                    if (EntityManager.Exists(data.SourceEntity)) EntityManager.AddComponent<SettlementMarkerTag>(data.SourceEntity);
                }
            }
        }
    }
}