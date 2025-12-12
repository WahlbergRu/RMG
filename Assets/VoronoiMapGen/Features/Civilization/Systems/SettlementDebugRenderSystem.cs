using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components; 
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Civilization.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SettlementDebugRenderSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out MapSettings settings)) return;
            if (!settings.ShowSettlements) return; 

            Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if(!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;
            var styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity);

            foreach ((RefRO<SettlementData> settlement, RefRO<DemographicsData> demo, RefRO<VoronoiCell> cell, RefRO<CellBiome> bio, RefRO<DetailLevelData> level) in 
                     SystemAPI.Query<RefRO<SettlementData>, RefRO<DemographicsData>, RefRO<VoronoiCell>, RefRO<CellBiome>, RefRO<DetailLevelData>>())
            {
                if (settlement.ValueRO.Type == SettlementType.Wilderness) continue;

                int lvlIdx = (int)level.ValueRO.Level;
                if (lvlIdx >= styles.Length) lvlIdx = 0;
                var style = styles[lvlIdx];
                
                float elevation = bio.ValueRO.Elevation;
                float groundY = bio.ValueRO.Type == BiomeType.Ocean ? 0.2f : (1.0f + math.max(0, elevation) * style.HeightScale);

                float3 center = new float3(cell.ValueRO.Centroid.x, groundY, cell.ValueRO.Centroid.y);

                Color c = Color.white;
                float radius = 5f;
                float popHeight = math.max(2f, demo.ValueRO.EstimatedPopulation / 500.0f); 

                // --- ЗДЕСЬ БЫЛИ ОШИБКИ - ИСПРАВЛЕНО ---
                switch (settlement.ValueRO.Type)
                {
                    case SettlementType.Metropolis: 
                        c = new Color(1f, 0.1f, 0.1f); // Красный
                        radius = 25f; 
                        popHeight += 10f; 
                        break;
                    case SettlementType.Town: 
                        c = new Color(1f, 0.5f, 0f); // Оранжевый
                        radius = 15f; 
                        break;
                    case SettlementType.Outpost: 
                        c = new Color(0.2f, 1f, 0.2f); // Зеленый
                        radius = 8f; 
                        break;
                }

                DrawCylinder(center, radius, popHeight, c);
            }
        }

        private void DrawCylinder(float3 pos, float r, float h, Color c)
        {
            float3 bot = pos;
            float3 top = pos + new float3(0, h, 0);
            int segments = 8;
            float angleStep = (math.PI * 2) / segments;

            for (int i = 0; i < segments; i++)
            {
                float a1 = i * angleStep;
                float a2 = (i + 1) * angleStep;
                float3 p1 = new float3(math.cos(a1) * r, 0, math.sin(a1) * r);
                float3 p2 = new float3(math.cos(a2) * r, 0, math.sin(a2) * r);

                Debug.DrawLine(bot + p1, bot + p2, c);
                Debug.DrawLine(top + p1, top + p2, c);
                if (i % 2 == 0) Debug.DrawLine(bot + p1, top + p1, c);
            }
        }
    }
}