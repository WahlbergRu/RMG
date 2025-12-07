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
using VoronoiMapGen.Systems.Rendering;
using VoronoiMapGen.Systems.Rendering.Terrain; // <-- Подключаем неймспейс с хелперами

namespace VoronoiMapGen.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshCreateSystem : SystemBase
    {
        private const int BATCH_SIZE = 2000;
        private Material _defaultMaterial;
        private readonly List<Mesh> _createdMeshes = new List<Mesh>();

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
            CleanupResources(immediate: true);
            if (_defaultMaterial) Object.DestroyImmediate(_defaultMaterial);
        }

        public void CleanupResources(bool immediate = false)
        {
            if (_createdMeshes.Count == 0) return;
            foreach (var m in _createdMeshes)
            {
                if (m == null) continue;
                if (immediate) Object.DestroyImmediate(m);
                else Object.Destroy(m);
            }
            _createdMeshes.Clear();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;

            // 1. Собираем сущности без меша
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
                var batchEntities = FilterEntities(entities, settings.RenderLevelMask);

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

        private NativeList<Entity> FilterEntities(NativeArray<Entity> source, int renderMask)
        {
            var batch = new NativeList<Entity>(BATCH_SIZE, Allocator.TempJob);
            for (int i = 0; i < source.Length; i++)
            {
                if (batch.Length >= BATCH_SIZE) break;
                var e = source[i];
                var lvl = EntityManager.GetComponentData<DetailLevelData>(e).Level;

                if ((renderMask & (1 << (int)lvl)) == 0 || EntityManager.GetBuffer<CellPolygonVertex>(e).Length < 3)
                {
                    EntityManager.AddComponent<VoronoiCellMeshTag>(e); // Помечаем как обработанное (пропущено)
                    continue;
                }
                batch.Add(e);
            }
            return batch;
        }

        private void GenerateBatch(NativeList<Entity> entities, NativeArray<TerrainVisualData> styles)
        {
            int count = entities.Length;
            var mda = Mesh.AllocateWritableMeshData(count);
            var meshes = new Mesh[count];
            var bakeList = new NativeList<BakeData>(count, Allocator.TempJob);

            // Lookup'ы для быстрого доступа
            var biomeLookup = SystemAPI.GetComponentLookup<CellBiome>(isReadOnly: true);
            var levelLookup = SystemAPI.GetComponentLookup<DetailLevelData>(isReadOnly: true);
            biomeLookup.Update(ref CheckedStateRef);
            levelLookup.Update(ref CheckedStateRef);

            // Буферы для переиспользования памяти
            var ring0 = new NativeList<float3>(64, Allocator.Temp);
            var ring1 = new NativeList<float3>(64, Allocator.Temp);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    Entity e = entities[i];
                    meshes[i] = new Mesh { name = $"Cell_{e.Index}" };
                    _createdMeshes.Add(meshes[i]);

                    // Сборка контекста (данных для генерации)
                    GenerationContext ctx = BuildContext(e, styles, biomeLookup, levelLookup);
                    
                    var inputVerts = EntityManager.GetBuffer<CellPolygonVertex>(e);
                    
                    // Расчет размера буферов
                    TerrainGeometryBuilder.CalculateLayout(inputVerts.Length, ctx.Style, ctx.IsWater, out int totalVerts, out int totalIndices);

                    var md = mda[i];
                    md.SetVertexBufferParams(totalVerts, 
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                    md.SetIndexBufferParams(totalIndices, IndexFormat.UInt32);

                    // ГЕНЕРАЦИЯ (ВЫЗОВ ВНЕШНЕГО КЛАССА)
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

                // Apply to Unity
                Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);

                // Bake to ECS
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