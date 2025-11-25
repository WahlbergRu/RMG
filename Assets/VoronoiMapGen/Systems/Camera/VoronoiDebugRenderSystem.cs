using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiDebugRenderSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Получаем настройки, чтобы знать, сколько всего уровней
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            
            // Рисуем только ПОСЛЕДНИЙ уровень (самый детальный)
            // Если LevelsCount = 2 (L0, L1), то MaxLevel = 1.
            int maxLevel = settings.LevelsCount - 1;

            foreach (var (edge, levelData) in SystemAPI.Query<RefRO<VoronoiEdge>, RefRO<DetailLevelData>>())
            {
                // Фильтр: Рисуем только если уровень совпадает с максимальным
                if ((int)levelData.ValueRO.Level != maxLevel) continue;

                float3 start = new float3(edge.ValueRO.VertexA.x, 0, edge.ValueRO.VertexA.y);
                float3 end = new float3(edge.ValueRO.VertexB.x, 0, edge.ValueRO.VertexB.y);
                
                // Рисуем чуть выше (Y=1), чтобы было видно над мешем
                Debug.DrawLine(start + new float3(0,1,0), end + new float3(0,1,0), Color.red);
            }
        }
    }
}
