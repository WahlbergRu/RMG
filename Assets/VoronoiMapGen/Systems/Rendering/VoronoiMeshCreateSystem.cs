using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using VoronoiMapGen.Systems.Rendering.Terrain;

namespace VoronoiMapGen.Systems.Rendering
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshCreateSystem : SystemBase
    {
        private const int BATCH_SIZE = 1000; // Уменьшил батч, чтобы быстрее откликалось на движение
        private Material _defaultMaterial;
        private readonly List<Mesh> _createdMeshes = new List<Mesh>();
        private Camera _mainCamera; // Кэш камеры

        protected override void OnCreate()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Hidden/Internal-ErrorShader");
            
            _defaultMaterial = new Material(shader) { enableInstancing = true };
            _defaultMaterial.SetFloat("_Smoothness", 0.0f);
            _defaultMaterial.SetFloat("_Cull", 0);

            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>();
        }

        protected override void OnDestroy()
        {
            CleanupResources(forceImmediate: false);
            if (_defaultMaterial) Object.DestroyImmediate(_defaultMaterial);
        }

        public void CleanupResources(bool forceImmediate = false)
        {
            if (_createdMeshes.Count == 0) return;
            var manager = UnifiedResourceManager.TryGetInstance();
            if (manager != null && !forceImmediate && Application.isPlaying)
            {
                foreach (var m in _createdMeshes) manager.SafeDestroy(m);
            }
            else
            {
                foreach (var m in _createdMeshes)
                {
                    if (m != null) Object.DestroyImmediate(m);
                }
            }
            _createdMeshes.Clear();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;

            // 1. Получаем камеру и плоскости отсечения
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return; // Нет камеры - не рендерим

            // Получаем 6 плоскостей видимости камеры
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(_mainCamera);

            // Ищем ячейки, которые нужно построить
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
                using var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.TempJob); // Для проверки Bounds

                // Фильтруем: Уровень включен + Попадает в камеру
                var batchEntities = FilterEntities(entities, cells, settings.RenderLevelMask, frustumPlanes);

                if (batchEntities.Length > 0)
                {
                    GenerateBatch(batchEntities, styles);
                }
                batchEntities.Dispose();
            }
            finally
            {
                styles.Dispose();
            }
        }

        private NativeList<Entity> FilterEntities(NativeArray<Entity> source, NativeArray<VoronoiCell> cells, int renderMask, Plane[] planes)
        {
            var batch = new NativeList<Entity>(BATCH_SIZE, Allocator.TempJob);
            
            // Размер ячейки для проверки (грубая оценка AABB)
            // Можно брать из настроек, но 150.0f покрывает большинство ячеек L2-L3
            float cullingSize = 150.0f; 
            Vector3 sizeBox = new Vector3(cullingSize, 1000f, cullingSize); // Высота большая, чтобы не резать горы

            for (int i = 0; i < source.Length; i++)
            {
                // Если набрали батч, выходим, остальное в след. кадре (распределение нагрузки)
                if (batch.Length >= BATCH_SIZE) break;

                var e = source[i];
                
                // Проверка уровня (быстрая)
                var lvl = EntityManager.GetComponentData<DetailLevelData>(e).Level;
                if ((renderMask & (1 << (int)lvl)) == 0)
                {
                    // Если уровень выключен глобально - помечаем как "не надо строить"
                    // Но если вдруг включат, придется сбросить тег. Пока просто пропускаем и не метим.
                    // Если пометить VoronoiCellMeshTag, то при включении уровня меш не появится.
                    // Поэтому просто continue.
                    continue;
                }

                // Проверка геометрии
                if (EntityManager.GetBuffer<CellPolygonVertex>(e).Length < 3)
                {
                    EntityManager.AddComponent<VoronoiCellMeshTag>(e);
                    continue;
                }

                // --- FRUSTUM CULLING (LAZY LOAD) ---
                // Проверяем, видит ли камера эту ячейку.
                // Центр берем из VoronoiCell.
                var center = cells[i].Centroid;
                Bounds bounds = new Bounds(new Vector3(center.x, 0, center.y), sizeBox);

                // GeometryUtility.TestPlanesAABB возвращает true, если объект ВНУТРИ или ПЕРЕСЕКАЕТ пирамиду
                if (GeometryUtility.TestPlanesAABB(planes, bounds))
                {
                    // Виден -> Добавляем в очередь на генерацию
                    batch.Add(e);
                }
                // Если НЕ виден -> просто пропускаем в этом кадре. 
                // Когда камера подвинется, query снова найдет его (т.к. тега MeshTag еще нет) и проверит.
            }
            return batch;
        }

        private void GenerateBatch(NativeList<Entity> entities, NativeArray<TerrainVisualData> styles)
        {
            int count = entities.Length;
            var mda = Mesh.AllocateWritableMeshData(count);
            var meshes = new Mesh[count];
            var bakeList = new NativeList<BakeData>(count, Allocator.TempJob);

            var biomeLookup = SystemAPI.GetComponentLookup<CellBiome>(isReadOnly: true);
            var levelLookup = SystemAPI.GetComponentLookup<DetailLevelData>(isReadOnly: true);
            biomeLookup.Update(ref CheckedStateRef);
            levelLookup.Update(ref CheckedStateRef);

            var ring0 = new NativeList<float3>(64, Allocator.Temp);
            var ring1 = new NativeList<float3>(64, Allocator.Temp);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    Entity e = entities[i];
                    meshes[i] = new Mesh { name = $"Cell_{e.Index}" };
                    _createdMeshes.Add(meshes[i]);

                    GenerationContext ctx = BuildContext(e, styles, biomeLookup, levelLookup);
                    var inputVerts = EntityManager.GetBuffer<CellPolygonVertex>(e);
                    
                    TerrainGeometryBuilder.CalculateLayout(inputVerts.Length, ctx.Style, ctx.IsWater, out int totalVerts, out int totalIndices);

                    var md = mda[i];
                    md.SetVertexBufferParams(totalVerts, 
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                    md.SetIndexBufferParams(totalIndices, IndexFormat.UInt32);

                    TerrainGeometryBuilder.FillMesh(
                        md.GetVertexData<MeshGenerationUtils.SimpleVertex>(),
                        md.GetIndexData<int>(),
                        inputVerts, ctx, ring0, ring1
                    );

                    md.subMeshCount = 1;
                    md.SetSubMesh(0, new SubMeshDescriptor(0, totalIndices), MeshUpdateFlags.DontRecalculateBounds);

                    bakeList.Add(new BakeData { Entity = e, MeshIndex = i, Color = ctx.Color, 
                        Transform = new LocalTransform { Position = ctx.CenterPos, Rotation = quaternion.identity, Scale = 1f } 
                    });
                }

                Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);

                var rma = new RenderMeshArray(new[] { _defaultMaterial }, meshes);
                var desc = new RenderMeshDescription(ShadowCastingMode.On, true);

                for (int k = 0; k < bakeList.Length; k++)
                {
                    var d = bakeList[k];
                    EntityManager.AddComponent<VoronoiCellMeshTag>(d.Entity);
                    meshes[d.MeshIndex].RecalculateBounds();
                    EntityManager.AddComponentData(d.Entity, new RenderBounds { Value = meshes[d.MeshIndex].bounds.ToAABB() });
                    EntityManager.AddComponentData(d.Entity, new URPMaterialPropertyBaseColor { Value = d.Color });
                    EntityManager.SetComponentData(d.Entity, d.Transform);
                    RenderMeshUtility.AddComponents(d.Entity, EntityManager, desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(0, d.MeshIndex));
                }
            }
            finally
            {
                bakeList.Dispose(); ring0.Dispose(); ring1.Dispose();
            }
        }

        private GenerationContext BuildContext(Entity e, NativeArray<TerrainVisualData> styles, ComponentLookup<CellBiome> biomes, ComponentLookup<DetailLevelData> levels)
        {
            var cell = EntityManager.GetComponentData<VoronoiCell>(e);
            int lvlIdx = (int)levels[e].Level;
            if (lvlIdx >= styles.Length) lvlIdx = styles.Length - 1;

            var style = styles[lvlIdx];
            var center = new float3(cell.Centroid.x, 0, cell.Centroid.y);
            
            float baseHeight = 1.0f;
            float4 color = new float4(0.5f, 0.5f, 0.5f, 1);
            bool isWater = false;

            if (biomes.HasComponent(e))
            {
                var b = biomes[e];
                color = RenderUtils.GetBiomeColor(b.Type);
                isWater = b.Type == BiomeType.Ocean;
                if (isWater) baseHeight = 0.2f;
                else baseHeight = 1.0f + (math.pow(math.max(0, b.Elevation), 1.5f) * style.HeightScale);
                
                color += noise.snoise(new float2(center.x, center.z) * 0.1f) * 0.05f;
            }

            return new GenerationContext {
                Style = style, BaseHeight = baseHeight, BottomDepth = -style.BottomDepth,
                CenterPos = center, IsWater = isWater, Color = color
            };
        }
    }
}