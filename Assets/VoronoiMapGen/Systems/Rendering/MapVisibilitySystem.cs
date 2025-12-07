using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct MapVisibilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MapSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<MapSettings>();
            
            // Маски уровней
            int terrainMask = settings.RenderLevelMask;
            int riverMask = settings.RiverRenderMask; // <-- ИСПРАВЛЕНО (Render Mask)

            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            // 1. TERRAIN
            foreach (var (levelData, entity) in SystemAPI.Query<RefRO<DetailLevelData>>()
                         .WithAll<VoronoiCellMeshTag>()
                         .WithNone<RiverChunkTag>()
                         .WithEntityAccess())
            {
                ProcessEntityVisibility(ecb, entity, (int)levelData.ValueRO.Level, terrainMask);
            }

            // 2. RIVERS
            foreach (var (levelData, entity) in SystemAPI.Query<RefRO<DetailLevelData>>()
                         .WithAll<RiverChunkTag>()
                         .WithEntityAccess())
            {
                if (!settings.ShowRivers)
                {
                    ecb.AddComponent<DisableRendering>(entity);
                }
                else
                {
                    ProcessEntityVisibility(ecb, entity, (int)levelData.ValueRO.Level, riverMask);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        static void ProcessEntityVisibility(EntityCommandBuffer ecb, Entity entity, int level, int mask)
        {
            bool isVisible = (mask & (1 << level)) != 0;

            if (isVisible)
            {
                ecb.RemoveComponent<DisableRendering>(entity);
            }
            else
            {
                ecb.AddComponent<DisableRendering>(entity);
            }
        }
    }
}