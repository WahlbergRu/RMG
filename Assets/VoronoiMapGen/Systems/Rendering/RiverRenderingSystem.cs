using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering; // Нужен для RenderBounds, RenderMeshArray и т.д.
using Unity.Transforms; // Нужен для LocalToWorld
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils; // Для UnifiedResourceManager
using VoronoiMapGen.Systems.Rendering.Rivers; 

namespace VoronoiMapGen.Systems.Rendering
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RiverRenderingSystem : SystemBase
    {
        private int _lastRiverMask = -1;
        private int _lastTerrainMask = -1;
        private bool _lastShowRivers = false;

        protected override void OnCreate()
        {
            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>();
            
            // Включаем Unified System (если она работает по этому тегу)
            var entity = EntityManager.CreateEntity(typeof(UnifiedRenderTag)); 
        }

        // Этот метод нужен для совместимости с Bootstrap, который может его дергать
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

            bool changed = (settings.RiverRenderMask != _lastRiverMask) ||
                           (settings.RenderLevelMask != _lastTerrainMask) ||
                           (settings.ShowRivers != _lastShowRivers);

            _lastRiverMask = settings.RiverRenderMask;
            _lastTerrainMask = settings.RenderLevelMask;
            _lastShowRivers = settings.ShowRivers;

            // Если ничего не изменилось и реки уже построены - выходим
            if (!changed && !SystemAPI.QueryBuilder().WithAll<RiverChunkTag>().Build().IsEmpty) return;
            
            // Если изменилось или выключили - удаляем старое
            if (changed || !settings.ShowRivers)
            {
                CleanupResources();
            }

            if (!settings.ShowRivers) return;

            // Проверяем наличие данных террейна
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;
            
            var styles = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity).ToNativeArray(Allocator.TempJob);

            try
            {
                // 1. Считаем геометрию во временные списки (без ECS)
                var vList = new NativeList<ProceduralVertex>(Allocator.Temp);
                var iList = new NativeList<ProceduralIndex>(Allocator.Temp);

                RiverMeshBuilder_ECS.BuildToNativeList(EntityManager, settings, styles, vList, iList);

                if (vList.Length > 0)
                {
                    // 2. Создаем сущность СРАЗУ со всеми компонентами (Архетип)
                    // Это предотвращает ошибку ObjectDisposedException, так как не происходит сдвига памяти
                    var riverArchetype = EntityManager.CreateArchetype(
                        typeof(RiverChunkTag),
                        typeof(ProceduralMeshReference), // Для UnifiedRenderSystem
                        typeof(ProceduralVertex),        // Буфер вершин
                        typeof(ProceduralIndex),         // Буфер индексов
                        typeof(ProceduralMeshRequest),   // Запрос на рендер
                        typeof(LocalToWorld),
                        typeof(RenderBounds),
                        typeof(UnifiedRenderTag)
                    );

                    Entity riverChunk = EntityManager.CreateEntity(riverArchetype);
                    EntityManager.SetName(riverChunk, "Global_River_Network");

                    // 3. Получаем буферы уже созданной сущности
                    var vBuf = EntityManager.GetBuffer<ProceduralVertex>(riverChunk);
                    var iBuf = EntityManager.GetBuffer<ProceduralIndex>(riverChunk);
                    
                    // 4. Заливаем данные
                    vBuf.AddRange(vList.AsArray());
                    iBuf.AddRange(iList.AsArray());

                    // 5. Настраиваем материал
                    EntityManager.SetComponentData(riverChunk, new ProceduralMeshRequest
                    {
                        IsDirty = true,
                        MaterialName = "Universal Render Pipeline/Lit",
                        Color = new float4(0.0f, 0.5f, 1.0f, 0.8f),
                        Smoothness = 0.9f
                    });
                    
                    EntityManager.SetComponentData(riverChunk, new LocalToWorld { Value = float4x4.identity });
                    // Большие границы, чтобы не мерцало при повороте камеры
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