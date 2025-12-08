using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering.Rivers
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RiverRenderingSystem : SystemBase
    {
        private int _lastRiverMask = -1;
        private bool _lastShowRivers;
        private int _lastTerrainMask = -1;

        protected override void OnCreate()
        {
            RequireForUpdate<MapGeneratedTag>();
            if (!EntityManager.CreateEntityQuery(typeof(UnifiedRenderTag)).HasSingleton<UnifiedRenderTag>())
                EntityManager.CreateEntity(typeof(UnifiedRenderTag));
        }

        public void CleanupResources(bool unused = false)
        {
            var q = EntityManager.CreateEntityQuery(typeof(RiverChunkTag));
            if (!q.IsEmpty) EntityManager.DestroyEntity(q);
        }

        protected override void OnDestroy()
        {
            CleanupResources();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;

            var changed = settings.RiverRenderMask != _lastRiverMask ||
                          settings.RenderLevelMask != _lastTerrainMask ||
                          settings.ShowRivers != _lastShowRivers;

            _lastRiverMask = settings.RiverRenderMask;
            _lastTerrainMask = settings.RenderLevelMask;
            _lastShowRivers = settings.ShowRivers;

            // Если настройки не менялись и рек нет, не делаем ничего
            if (!changed && !SystemAPI.QueryBuilder().WithAll<RiverChunkTag>().Build().IsEmpty) return;

            // Если настройки изменились, удаляем старые чанки
            if (changed || !settings.ShowRivers) CleanupResources();

            if (!settings.ShowRivers) return;

            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;
            var styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity).ToNativeArray(Allocator.TempJob);

            try
            {
                // Генерируем геометрию
                var vList = new NativeList<ProceduralVertex>(Allocator.Temp);
                var iList = new NativeList<ProceduralIndex>(Allocator.Temp);

                RiverMeshBuilder_ECS.BuildToNativeList(EntityManager, settings, styles, vList, iList);

                if (vList.Length > 0)
                {
                    // Создаем сущность реки с нужными компонентами
                    var riverArchetype = EntityManager.CreateArchetype(
                        typeof(RiverChunkTag),
                        typeof(ProceduralMeshReference),
                        typeof(ProceduralVertex),
                        typeof(ProceduralIndex),
                        typeof(MeshDirtyTag),       // <-- Ключевой компонент для оптимизации
                        typeof(ProceduralMeshRequest),
                        typeof(LocalToWorld),
                        typeof(RenderBounds),
                        typeof(UnifiedRenderTag)
                    );

                    var riverChunk = EntityManager.CreateEntity(riverArchetype);
                    EntityManager.SetName(riverChunk, "Global_River_Network");

                    var vBuf = EntityManager.GetBuffer<ProceduralVertex>(riverChunk);
                    var iBuf = EntityManager.GetBuffer<ProceduralIndex>(riverChunk);

                    vBuf.AddRange(vList.AsArray());
                    iBuf.AddRange(iList.AsArray());

                    // Включаем Dirty Tag
                    EntityManager.SetComponentEnabled<MeshDirtyTag>(riverChunk, true);

                    EntityManager.SetComponentData(riverChunk, new ProceduralMeshRequest
                    {
                        MaterialName = "Universal Render Pipeline/Lit",
                        Color = new float4(0.0f, 0.5f, 1.0f, 0.8f),
                        Smoothness = 0.9f
                    });

                    EntityManager.SetComponentData(riverChunk, new LocalToWorld { Value = float4x4.identity });
                    EntityManager.SetComponentData(riverChunk, new RenderBounds { Value = new AABB { Extents = new float3(50000, 5000, 50000) } });
                }

                vList.Dispose();
                iList.Dispose();
            }
            finally
            {
                styles.Dispose();
            }
        }
    }
}