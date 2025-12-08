using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Rendering.Terrain
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TerrainVisibilitySystem : SystemBase
    {
        private int _lastMask = -1;

        protected override void OnCreate()
        {
            RequireForUpdate<MapGeneratedTag>();
        }

        protected override void OnUpdate()
        {
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            var settings = SystemAPI.GetComponent<MapSettings>(settingsEntity);
            int currentMask = settings.RenderLevelMask;

            // Если маска не менялась, ничего не делаем
            if (currentMask == _lastMask) return;
            _lastMask = currentMask;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 1. ПОКАЗАТЬ (Удалить DisableRendering)
            // Ищем скрытые сущности, чей уровень теперь включен
            Entities
                .WithAll<VoronoiCellMeshTag, DetailLevelData, DisableRendering>() // Только скрытые
                .ForEach((Entity e, in DetailLevelData lvl) =>
                {
                    if ((currentMask & (1 << (int)lvl.Level)) != 0)
                    {
                        ecb.RemoveComponent<DisableRendering>(e);
                    }
                }).Run();

            // 2. СКРЫТЬ (Добавить DisableRendering)
            // Ищем видимые сущности, чей уровень теперь выключен
            Entities
                .WithAll<VoronoiCellMeshTag, DetailLevelData>()
                .WithNone<DisableRendering>() // Только видимые
                .ForEach((Entity e, in DetailLevelData lvl) =>
                {
                    if ((currentMask & (1 << (int)lvl.Level)) == 0)
                    {
                        ecb.AddComponent<DisableRendering>(e);
                    }
                }).Run();

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}