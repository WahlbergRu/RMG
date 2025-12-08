using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering.Terrain
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TerrainVisibilitySystem : SystemBase
    {
        private int _lastMask = -1;

        protected override void OnCreate()
        {
            RequireForUpdate<MapGeneratedTag>();
            RequireForUpdate<UnifiedRenderTag>(); // Ждем появления чанков
        }

        protected override void OnUpdate()
        {
            Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            MapSettings settings = SystemAPI.GetComponent<MapSettings>(settingsEntity);
            int currentMask = settings.RenderLevelMask;

            // Оптимизация: Если маска не менялась с прошлого кадра, ничего не делаем
            if (currentMask == _lastMask) return;
            _lastMask = currentMask;

            // Создаем CommandBuffer для структурных изменений (Add/Remove DisableRendering)
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            // === 1. ПОКАЗАТЬ (Удалить DisableRendering) ===
            // Ищем Чанки (UnifiedRenderTag), которые сейчас СКРЫТЫ (имеют DisableRendering),
            // но согласно маске должны быть ВИДИМЫ.
            foreach ((RefRO<DetailLevelData> levelData, Entity entity) in SystemAPI.Query<RefRO<DetailLevelData>>()
                         .WithAll<UnifiedRenderTag, DisableRendering>() // Ищем среди скрытых чанков
                         .WithEntityAccess())
            {
                int lvl = (int)levelData.ValueRO.Level;
                
                // Проверяем бит в маске. Если бит 1, значит уровень включен.
                if ((currentMask & (1 << lvl)) != 0)
                {
                    ecb.RemoveComponent<DisableRendering>(entity);
                }
            }

            // === 2. СКРЫТЬ (Добавить DisableRendering) ===
            // Ищем Чанки, которые сейчас ВИДИМЫ (НЕТ DisableRendering),
            // но согласно маске должны быть СКРЫТЫ.
            foreach ((RefRO<DetailLevelData> levelData, Entity entity) in SystemAPI.Query<RefRO<DetailLevelData>>()
                         .WithAll<UnifiedRenderTag>()
                         .WithNone<DisableRendering>() // Ищем среди видимых чанков
                         .WithEntityAccess())
            {
                int lvl = (int)levelData.ValueRO.Level;

                // Если бит 0, значит уровень выключен.
                if ((currentMask & (1 << lvl)) == 0)
                {
                    ecb.AddComponent<DisableRendering>(entity);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}