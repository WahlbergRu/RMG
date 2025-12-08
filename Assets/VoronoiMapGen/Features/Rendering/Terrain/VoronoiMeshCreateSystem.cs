using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
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
        private const int CELLS_PER_CHUNK = 2000;
        private EntityArchetype _chunkArchetype;

        protected override void OnCreate()
        {
            RequireForUpdate<MapGeneratedTag>();
            if (!EntityManager.CreateEntityQuery(typeof(UnifiedRenderTag)).HasSingleton<UnifiedRenderTag>())
                EntityManager.CreateEntity(typeof(UnifiedRenderTag));

            _chunkArchetype = EntityManager.CreateArchetype(
                typeof(UnifiedRenderTag),
                typeof(ProceduralMeshRequest),
                typeof(ProceduralMeshReference),
                typeof(MeshDirtyTag),
                typeof(ProceduralVertex),
                typeof(ProceduralIndex),
                typeof(LocalToWorld),
                typeof(RenderBounds),
                typeof(DetailLevelData)
            );
        }

        public void CleanupResources(bool unused = false) { }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (!EntityManager.HasBuffer<TerrainVisualData>(settingsEntity)) return;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellBiome, CellPolygonVertex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>()
                .Build();

            if (query.IsEmpty) return;
            query.SetOrderVersionFilter();

            var stylesBuffer = EntityManager.GetBuffer<TerrainVisualData>(settingsEntity);
            var styles = stylesBuffer.ToNativeArray(Allocator.TempJob);

            try
            {
                using var entities = query.ToEntityArray(Allocator.TempJob);
                var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.TempJob);
                var biomes = query.ToComponentDataArray<CellBiome>(Allocator.TempJob);
                var levels = query.ToComponentDataArray<DetailLevelData>(Allocator.TempJob);
                var bufferLookup = SystemAPI.GetBufferLookup<CellPolygonVertex>(true);

                int cellCount = entities.Length;

                var cellCentroids = new NativeArray<float2>(cellCount, Allocator.TempJob);
                var cellBiomesFlat = new NativeArray<CellBiome>(cellCount, Allocator.TempJob);
                var cellLevelsFlat = new NativeArray<int>(cellCount, Allocator.TempJob);

                var flatPolygons = new NativeList<float3>(cellCount * 12, Allocator.TempJob);
                var polyOffsets = new NativeArray<int>(cellCount, Allocator.TempJob);
                var polyCounts = new NativeArray<int>(cellCount, Allocator.TempJob);

                int currentPolyOffset = 0;
                for (int i = 0; i < cellCount; i++)
                {
                    Entity e = entities[i];
                    cellCentroids[i] = cells[i].Centroid;
                    cellBiomesFlat[i] = biomes[i];
                    cellLevelsFlat[i] = (int)levels[i].Level;

                    if (bufferLookup.HasBuffer(e))
                    {
                        var buf = bufferLookup[e];
                        int pCount = buf.Length;
                        var rawPtr = buf.AsNativeArray();
                        for(int k=0; k<pCount; k++) flatPolygons.Add(rawPtr[k].Value);
                        
                        polyOffsets[i] = currentPolyOffset;
                        polyCounts[i] = pCount;
                        currentPolyOffset += pCount;
                    }
                    else
                    {
                        polyCounts[i] = 0;
                        polyOffsets[i] = currentPolyOffset;
                    }
                }
                
                cells.Dispose(); biomes.Dispose(); levels.Dispose();

                var meshCounts = new NativeArray<int2>(cellCount, Allocator.TempJob);
                
                new CalcSizeJob
                {
                    Levels = cellLevelsFlat,
                    PolygonCounts = polyCounts,
                    Biomes = cellBiomesFlat,
                    Styles = styles,
                    OutputCounts = meshCounts
                }.Schedule(cellCount, 64).Complete();
                
                var totalVerts = 0;
                var totalInds = 0;
                var cellWriteOffsets = new NativeArray<int2>(cellCount, Allocator.TempJob);
                
                for(int i=0; i<cellCount; i++)
                {
                    cellWriteOffsets[i] = new int2(totalVerts, totalInds);
                    totalVerts += meshCounts[i].x;
                    totalInds += meshCounts[i].y;
                }

                var outGlobalVerts = new NativeArray<ProceduralVertex>(totalVerts, Allocator.TempJob);
                var outGlobalInds = new NativeArray<ProceduralIndex>(totalInds, Allocator.TempJob);
                
                new GenerateMeshJob
                {
                    Centroids = cellCentroids,
                    Biomes = cellBiomesFlat,
                    Levels = cellLevelsFlat,
                    FlatPolygons = flatPolygons.AsDeferredJobArray(),
                    PolyOffsets = polyOffsets,
                    PolyCounts = polyCounts,
                    Styles = styles,
                    WriteOffsets = cellWriteOffsets,
                    OutVerts = outGlobalVerts,
                    OutInds = outGlobalInds
                }.Schedule(cellCount, 64).Complete();
                
                var chunkV = new NativeList<ProceduralVertex>(CELLS_PER_CHUNK * 20, Allocator.Temp);
                var chunkI = new NativeList<ProceduralIndex>(CELLS_PER_CHUNK * 60, Allocator.Temp);
                var batchedEntities = new NativeList<Entity>(CELLS_PER_CHUNK, Allocator.Temp);
                
                int currentLvl = -1;
                for(int i=0; i<cellCount; i++)
                {
                    var countData = meshCounts[i];
                    if (countData.x == 0) continue; 
                    
                    int entityLvl = cellLevelsFlat[i];
                    bool isFull = batchedEntities.Length >= CELLS_PER_CHUNK;
                    bool isDiffLevel = (currentLvl != -1 && currentLvl != entityLvl);
                    
                    if ((isFull || isDiffLevel) && batchedEntities.Length > 0)
                    {
                        FlushToChunk(chunkV, chunkI, currentLvl, settings.RenderLevelMask);
                        chunkV.Clear(); chunkI.Clear();
                        EntityManager.AddComponent<VoronoiCellMeshTag>(batchedEntities.AsArray());
                        batchedEntities.Clear();
                    }
                    
                    currentLvl = entityLvl;
                    batchedEntities.Add(entities[i]);
                    
                    int2 offset = cellWriteOffsets[i];
                    int chunkBaseIndex = chunkV.Length;
                    chunkV.AddRange(outGlobalVerts.GetSubArray(offset.x, countData.x));
                    
                    var iSlice = outGlobalInds.GetSubArray(offset.y, countData.y);
                    for(int k=0; k<countData.y; k++)
                    {
                        chunkI.Add(new ProceduralIndex { Value = iSlice[k].Value + chunkBaseIndex });
                    }
                }
                
                if (batchedEntities.Length > 0)
                {
                    FlushToChunk(chunkV, chunkI, currentLvl, settings.RenderLevelMask);
                    EntityManager.AddComponent<VoronoiCellMeshTag>(batchedEntities.AsArray());
                }
                
                cellCentroids.Dispose(); cellBiomesFlat.Dispose(); cellLevelsFlat.Dispose();
                flatPolygons.Dispose(); polyOffsets.Dispose(); polyCounts.Dispose();
                meshCounts.Dispose(); cellWriteOffsets.Dispose();
                outGlobalVerts.Dispose(); outGlobalInds.Dispose();
            }
            finally
            {
                styles.Dispose();
            }
        }
        
        private void FlushToChunk(NativeList<ProceduralVertex> v, NativeList<ProceduralIndex> i, int level, int renderMask)
        {
            if (v.Length == 0) return;

            var chunkEntity = EntityManager.CreateEntity(_chunkArchetype);
            EntityManager.SetComponentData(chunkEntity, new ProceduralMeshRequest
            {
                MaterialName = "Universal Render Pipeline/Particles/Lit",
                Color = new float4(1,1,1,1),
                Smoothness = 0.5f
            });

            EntityManager.SetComponentData(chunkEntity, new DetailLevelData { Level = (DetailLevel)level });
            EntityManager.SetComponentData(chunkEntity, new LocalToWorld { Value = float4x4.identity });
            EntityManager.SetComponentData(chunkEntity, new RenderBounds { Value = new AABB { Extents = new float3(50000, 10000, 50000) } });
            EntityManager.SetComponentEnabled<MeshDirtyTag>(chunkEntity, true);

            var vBuf = EntityManager.GetBuffer<ProceduralVertex>(chunkEntity);
            var iBuf = EntityManager.GetBuffer<ProceduralIndex>(chunkEntity);
            
            vBuf.ResizeUninitialized(v.Length);
            iBuf.ResizeUninitialized(i.Length);
            vBuf.AsNativeArray().CopyFrom(v.AsArray());
            iBuf.AsNativeArray().CopyFrom(i.AsArray());

            if ((renderMask & (1 << level)) == 0) EntityManager.AddComponent<DisableRendering>(chunkEntity);
        }

        [BurstCompile]
        struct CalcSizeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Levels;
            [ReadOnly] public NativeArray<int> PolygonCounts;
            [ReadOnly] public NativeArray<CellBiome> Biomes;
            [ReadOnly] public NativeArray<TerrainVisualData> Styles;
            public NativeArray<int2> OutputCounts;

            public void Execute(int i)
            {
                int pCount = PolygonCounts[i];
                if (pCount < 3) { OutputCounts[i] = int2.zero; return; }

                int lvlIdx = math.min(Levels[i], Styles.Length - 1);
                var style = Styles[lvlIdx];
                bool isWater = Biomes[i].Type == BiomeType.Ocean;

                // Передаем style через IN
                TerrainGeometryBuilder.CalculateLayout(pCount, in style, isWater, out int v, out int ind);
                OutputCounts[i] = new int2(v, ind);
            }
        }

        [BurstCompile]
        struct GenerateMeshJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Centroids;
            [ReadOnly] public NativeArray<CellBiome> Biomes;
            [ReadOnly] public NativeArray<int> Levels;
            [ReadOnly] public NativeArray<float3> FlatPolygons;
            [ReadOnly] public NativeArray<int> PolyOffsets;
            [ReadOnly] public NativeArray<int> PolyCounts;
            [ReadOnly] public NativeArray<TerrainVisualData> Styles;
            [ReadOnly] public NativeArray<int2> WriteOffsets;
            
            [NativeDisableParallelForRestriction] public NativeArray<ProceduralVertex> OutVerts;
            [NativeDisableParallelForRestriction] public NativeArray<ProceduralIndex> OutInds;

            public void Execute(int i)
            {
                int pCount = PolyCounts[i];
                if (pCount < 3) return;

                int lvlIdx = math.min(Levels[i], Styles.Length - 1);
                var style = Styles[lvlIdx];
                var center = new float3(Centroids[i].x, 0, Centroids[i].y);
                var biome = Biomes[i];
                
                float4 color = RenderUtils.GetBiomeColor(biome.Type);
                bool isWater = biome.Type == BiomeType.Ocean;
                float baseHeight = isWater ? 0.2f : 1.0f + (math.pow(math.max(0, biome.Elevation), 1.5f) * style.HeightScale);
                color += noise.snoise(new float2(center.x, center.z) * 0.1f) * 0.05f;

                var ctx = new GenerationContext
                {
                    Style = style,
                    BaseHeight = baseHeight,
                    BottomDepth = -style.BottomDepth,
                    CenterPos = center,
                    IsWater = isWater,
                    Color = color
                };

                int2 offsets = WriteOffsets[i];
                int polyStart = PolyOffsets[i];
                // HACK: Recreating Slice using pointer magic would be unsafe in C# without UnsafeUtility, 
                // but NativeArray supports GetSubArray which is what we need.
                // We are passing a "flat" array, so in BuildGeometry we pass the SubArray.
                // However, BuildGeometry expects `NativeArray<float3>`. GetSubArray returns `NativeArray<float3>`, so it fits!
                
                // IMPORTANT: Since `FlatPolygons` is read-only in this job, GetSubArray creates a view.
                // But `FillMesh` accepts `NativeArray<float3>` (not ReadOnly explicitly in signature to allow passing this view easily).
                
                // If the builder signature is `NativeArray<float3> input`, passing `GetSubArray` works.
                var polySlice = FlatPolygons.GetSubArray(polyStart, pCount);

                var ringA = new NativeList<float3>(32, Allocator.Temp);
                var ringB = new NativeList<float3>(32, Allocator.Temp);
                
                TerrainGeometryBuilder.CalculateLayout(pCount, in style, isWater, out int vLen, out int iLen);
                
                var vTarget = OutVerts.GetSubArray(offsets.x, vLen);
                var iTarget = OutInds.GetSubArray(offsets.y, iLen);
                
                TerrainGeometryBuilder.FillMesh(vTarget, iTarget, polySlice, in ctx, ref ringA, ref ringB);
            }
        }
    }
}