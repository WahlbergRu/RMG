using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering;
using VoronoiMapGen.Features.Rendering.Components;
using VoronoiMapGen.Features.Rendering.Utils;

namespace VoronoiMapGen.Features.Rendering.Terrain
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshCreateSystem : SystemBase
    {
        private const int BATCH_SIZE = 1000;

        protected override void OnCreate()
        {
            RequireForUpdate<MapGeneratedTag>();
            if (!EntityManager.CreateEntityQuery(typeof(UnifiedRenderTag)).HasSingleton<UnifiedRenderTag>())
                EntityManager.CreateEntity(typeof(UnifiedRenderTag));
        }

        // --- ИСПРАВЛЕНИЕ: Добавлен отсутствующий метод ---
        public void CleanupResources(bool unused = false)
        {
            // Bootstrap удаляет компоненты самостоятельно через Query.
            // Здесь можно оставить пустую реализацию или сбросить внутренние кеши, если они появятся.
        }
        // -------------------------------------------------

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;

            // Ищем новые сущности, у которых еще нет тега меша
            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            if (query.IsEmpty) return;

            var visBuffer = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity);
            var styles = visBuffer.ToNativeArray(Allocator.TempJob);

            try
            {
                using var entities = query.ToEntityArray(Allocator.TempJob);
                var batchEntities = new NativeList<Entity>(BATCH_SIZE, Allocator.Temp);

                // 1. Собираем пачку сущностей
                for (int i = 0; i < entities.Length; i++)
                {
                    if (batchEntities.Length >= BATCH_SIZE) break;

                    Entity e = entities[i];
                    // Валидация
                    if (EntityManager.GetBuffer<CellPolygonVertex>(e).Length < 3)
                    {
                        // Помечаем обработанным, чтобы не застревать, но меш не создаем
                        EntityManager.AddComponent<VoronoiCellMeshTag>(e);
                        continue;
                    }
                    batchEntities.Add(e);
                }

                if (batchEntities.Length > 0)
                {
                    var entitiesArray = batchEntities.AsArray();

                    // 2. Добавляем компоненты рендеринга
                    EntityManager.AddComponent<VoronoiCellMeshTag>(entitiesArray);
                    EntityManager.AddComponent<ProceduralMeshReference>(entitiesArray);
                    
                    // --- OPTIMIZATION START ---
                    // Добавляем Dirty Tag и Включаем его (IEnableableComponent)
                    EntityManager.AddComponent<MeshDirtyTag>(entitiesArray); 
                    
                    EntityManager.AddComponent<ProceduralMeshRequest>(entitiesArray);
                    EntityManager.AddComponent<ProceduralVertex>(entitiesArray);
                    EntityManager.AddComponent<ProceduralIndex>(entitiesArray);
                    EntityManager.AddComponent<RenderBounds>(entitiesArray);

                    // 3. Генерируем данные
                    GenerateBuffersAndSetVisibility(batchEntities, styles, settings.RenderLevelMask);
                    
                    // Убеждаемся, что тег включен
                    for (int i = 0; i < entitiesArray.Length; i++)
                    {
                        EntityManager.SetComponentEnabled<MeshDirtyTag>(entitiesArray[i], true);
                    }
                    // --- OPTIMIZATION END ---
                }

                batchEntities.Dispose();
            }
            finally
            {
                styles.Dispose();
            }
        }

        private void GenerateBuffersAndSetVisibility(NativeList<Entity> entities, NativeArray<TerrainVisualData> styles, int renderMask)
        {
            var ring0 = new NativeList<float3>(64, Allocator.Temp);
            var ring1 = new NativeList<float3>(64, Allocator.Temp);
            var tempVerts = new NativeArray<MeshGenerationUtils.SimpleVertex>(4096, Allocator.Temp);
            var tempInds = new NativeArray<int>(8192, Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];

                var vBuf = EntityManager.GetBuffer<ProceduralVertex>(e);
                var iBuf = EntityManager.GetBuffer<ProceduralIndex>(e);

                var ctx = PrepareContext(e, styles, out float4 color);
                var inputVerts = EntityManager.GetBuffer<CellPolygonVertex>(e);

                TerrainGeometryBuilder.CalculateLayout(inputVerts.Length, ctx.Style, ctx.IsWater, out int totalVerts, out int totalIndices);

                TerrainGeometryBuilder.FillMesh(
                    tempVerts.GetSubArray(0, totalVerts),
                    tempInds.GetSubArray(0, totalIndices),
                    inputVerts, ctx, ring0, ring1
                );

                vBuf.ResizeUninitialized(totalVerts);
                var vArray = vBuf.AsNativeArray();
                for (int k = 0; k < totalVerts; k++)
                {
                    var sv = tempVerts[k];
                    vArray[k] = new ProceduralVertex { Position = sv.Position, Normal = sv.Normal, UV = sv.UV };
                }

                iBuf.ResizeUninitialized(totalIndices);
                var iArray = iBuf.AsNativeArray();
                for (int k = 0; k < totalIndices; k++)
                {
                    iArray[k] = new ProceduralIndex { Value = tempInds[k] };
                }

                // Заполняем реквест без флага IsDirty (теперь он в тэге)
                EntityManager.SetComponentData(e, new ProceduralMeshRequest
                {
                    MaterialName = "Universal Render Pipeline/Lit",
                    Color = color,
                    Smoothness = ctx.IsWater ? 0.9f : 0.0f
                });

                EntityManager.SetComponentData(e, new LocalTransform { Position = ctx.CenterPos, Rotation = quaternion.identity, Scale = 1f });
                EntityManager.SetComponentData(e, new RenderBounds { Value = new AABB { Extents = new float3(100, 100, 100) } });

                // Логика видимости LOD (DisableRendering для скрытых уровней)
                var lvl = EntityManager.GetComponentData<DetailLevelData>(e).Level;
                if ((renderMask & (1 << (int)lvl)) == 0)
                {
                    EntityManager.AddComponent<DisableRendering>(e);
                }
            }

            tempVerts.Dispose();
            tempInds.Dispose();
            ring0.Dispose();
            ring1.Dispose();
        }

        private GenerationContext PrepareContext(Entity e, NativeArray<TerrainVisualData> styles, out float4 color)
        {
            var cell = EntityManager.GetComponentData<VoronoiCell>(e);
            var lvlData = EntityManager.GetComponentData<DetailLevelData>(e);

            int lvlIdx = (int)lvlData.Level;
            if (lvlIdx >= styles.Length) lvlIdx = styles.Length - 1;

            var style = styles[lvlIdx];
            var center = new float3(cell.Centroid.x, 0, cell.Centroid.y);

            float baseHeight = 1.0f;
            color = new float4(0.5f, 0.5f, 0.5f, 1);
            bool isWater = false;

            if (EntityManager.HasComponent<CellBiome>(e))
            {
                var b = EntityManager.GetComponentData<CellBiome>(e);
                color = RenderUtils.GetBiomeColor(b.Type);
                isWater = b.Type == BiomeType.Ocean;

                if (isWater) baseHeight = 0.2f;
                else baseHeight = 1.0f + (math.pow(math.max(0, b.Elevation), 1.5f) * style.HeightScale);

                color += noise.snoise(new float2(center.x, center.z) * 0.1f) * 0.05f;
            }

            return new GenerationContext
            {
                Style = style,
                BaseHeight = baseHeight,
                BottomDepth = -style.BottomDepth,
                CenterPos = center,
                IsWater = isWater,
                Color = color
            };
        }
    }
}