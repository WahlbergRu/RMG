using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.MapGeneration.Jobs;
using VoronoiMapGen.Features.Utils;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Features.MapGeneration.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MapGenerationSystem : SystemBase
    {
        private int m_CurrentLevel;
        private MapHistoryData m_History;
        private bool m_IsComplete;
        private bool m_IsInitialized;
        private NativeArray<LevelSettings> m_LevelSettings;
        private MapSettings m_Settings;

        protected override void OnCreate()
        {
            RequireForUpdate<MapSettings>();
        }

        protected override void OnDestroy()
        {
            this.Dependency.Complete();
            if (m_LevelSettings.IsCreated) m_LevelSettings.Dispose();
            if (m_History != null) m_History.Dispose();
        }

        protected override void OnUpdate()
        {
            if (m_IsComplete) return;
            if (!m_IsInitialized)
            {
                Initialize();
                return;
            }

            if (m_CurrentLevel < m_LevelSettings.Length)
            {
                ProcessSingleLevel(m_CurrentLevel);
                m_CurrentLevel++;
            }
            else
            {
                CompleteGeneration();
            }
        }

        private void Initialize()
        {
            Entity settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            if (EntityManager.HasComponent<MapGeneratedTag>(settingsEntity)) { Enabled = false; return; }

            m_Settings = SystemAPI.GetSingleton<MapSettings>();
            DynamicBuffer<LevelSettings> buffer = EntityManager.GetBuffer<LevelSettings>(settingsEntity);
            m_LevelSettings = buffer.ToNativeArray(Allocator.Persistent);

            m_History = new MapHistoryData(m_LevelSettings.Length);
            EntityManager.AddComponent<MapGenerationInProgress>(settingsEntity);
            m_IsInitialized = true;
        }

        private void ProcessSingleLevel(int level)
        {
            Debug.Log($"Processing L{level}");
            LevelSettings levelSettings = m_LevelSettings[level];
            
            NativeArray<float2> sites = default;
            NativeArray<VoronoiSite> meta = default;
            NativeArray<TectonicPlateData> tectonicData = default;
            NativeArray<ClimateData> climateData = default;
            NativeArray<HydrologyData> hydrologyData = default;
            NativeArray<BiomeData> biomeData = default;

            NativeList<VoronoiCell> cellsList = new NativeList<VoronoiCell>(Allocator.Persistent);
            NativeList<VoronoiEdge> edgesList = new NativeList<VoronoiEdge>(Allocator.Persistent);

            // Cache vars
            NativeArray<float2> cv = default; NativeArray<int> cc = default; NativeArray<VoronoiEdge> ce = default;
            bool fromCache = m_Settings.UseCache && MapCacheUtils.LoadLevel(m_Settings.Seed, level,
                    out sites, out meta, out tectonicData, out climateData, out hydrologyData, out biomeData, out cv, out cc, out ce);

            if (fromCache)
            {
                NativeList<float2> tvL = new NativeList<float2>(cv.Length, Allocator.Temp); tvL.AddRange(cv);
                NativeList<int> tcL = new NativeList<int>(cc.Length, Allocator.Temp); tcL.AddRange(cc);
                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, new NativeList<TriangleIndices>(0, Allocator.Temp), tvL, tcL, ref cellsList, ref edgesList);
                edgesList.Clear(); edgesList.AddRange(ce);
                cv.Dispose(); cc.Dispose(); ce.Dispose();
            }
            else
            {
                NativeArray<VoronoiCell> pC = default; NativeArray<VoronoiSite> pM = default;
                NativeArray<HydrologyData> pH = default; NativeArray<TectonicPlateData> pT = default; NativeArray<ClimateData> pCl = default;

                if (m_History.TryGetLevel(level - 1, out MapLevelData parentData)) { pC=parentData.Cells; pM=parentData.Meta; pH=parentData.Hydrology; pT=parentData.Tectonics; pCl=parentData.Climate; }

                (NativeArray<float2> rawS, NativeArray<VoronoiSite> rawM) = SiteGenerator.Generate(m_Settings, levelSettings, level, pC, pM, pH, pT, pCl);
                (sites, meta) = MapProcessingHelpers.FilterValidSites(rawS, rawM, Allocator.Persistent);
                rawS.Dispose(); rawM.Dispose();

                NativeList<TriangleIndices> tri = new NativeList<TriangleIndices>(Allocator.TempJob);
                NativeList<float2> verts = new NativeList<float2>(Allocator.TempJob);
                NativeList<int> counts = new NativeList<int>(Allocator.TempJob);

                for (int i = 0; i <= levelSettings.RelaxationIterations; i++) {
                    DelaunayBuilder.Triangulate(sites, ref tri, m_Settings.MapSize);
                    verts.Clear(); counts.Clear();
                    VoronoiBuilder.BuildCells(sites, tri, m_Settings.MapSize, ref verts, ref counts);
                    if (i != levelSettings.RelaxationIterations) ApplyLloydRelaxation(sites, verts, counts, m_Settings.MapSize);
                }

                int cnt = sites.Length;
                tectonicData = new NativeArray<TectonicPlateData>(cnt, Allocator.Persistent);
                climateData = new NativeArray<ClimateData>(cnt, Allocator.Persistent);
                biomeData = new NativeArray<BiomeData>(cnt, Allocator.Persistent);
                hydrologyData = new NativeArray<HydrologyData>(cnt, Allocator.Persistent);

                NativeArray<TectonicPlateData> dTm = new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
                NativeArray<ClimateData> dCm = new NativeArray<ClimateData>(0, Allocator.TempJob);

                new TectonicGenerationJob { Seed = m_Settings.Seed, MapSize = m_Settings.MapSize, Level = level, Sites = sites, SiteMeta = meta, ParentTectonics = level == 0 ? dTm : pT, TectonicData = tectonicData }.Schedule(cnt, 64).Complete();
                new ClimateGenerationJob { Seed = m_Settings.Seed, MapSize = m_Settings.MapSize, Level = level, Sites = sites, SiteMeta = meta, Tectonics = tectonicData, ParentClimate = level == 0 ? dCm : pCl, Climate = climateData, Biomes = biomeData }.Schedule(cnt, 64).Complete();
                dTm.Dispose(); dCm.Dispose();

                NativeList<VoronoiEdge> tEd = MapProcessingHelpers.ExtractEdgesFromDelaunay(tri, Allocator.TempJob);
                NativeParallelMultiHashMap<int, NeighborInfo> nMap = new NativeParallelMultiHashMap<int, NeighborInfo>(tEd.Length*2, Allocator.TempJob);
                new BuildNeighborGraphJob { Edges = tEd, SitePositions = sites, Tectonics = tectonicData, MaxConnectionDistSq = 500000f, NeighborsMap = nMap }.Schedule().Complete();

                NativeArray<VoronoiCell> tmpCells = new NativeArray<VoronoiCell>(cnt, Allocator.TempJob);
                for(int i=0;i<cnt;i++) tmpCells[i] = new VoronoiCell { SiteIndex=i, Centroid=sites[i] };
                new CalculateHydrologyJob { Cells = tmpCells, Tectonics = tectonicData, Climate = climateData, NeighborsMap = nMap, Hydrology = hydrologyData }.Schedule().Complete();
                tmpCells.Dispose(); nMap.Dispose(); tEd.Dispose();

                MapProcessingHelpers.AssembleFinalGeometry(level, sites, meta, tri, verts, counts, ref cellsList, ref edgesList);
                if (m_Settings.UseCache) MapCacheUtils.SaveLevel(m_Settings.Seed, level, sites, meta, tectonicData, climateData, hydrologyData, biomeData, verts, counts, edgesList);
                tri.Dispose(); verts.Dispose(); counts.Dispose();
            }

            NativeArray<VoronoiCell> fC = new NativeArray<VoronoiCell>(cellsList.Length, Allocator.Persistent); fC.CopyFrom(cellsList.AsArray());
            NativeArray<VoronoiEdge> fE = new NativeArray<VoronoiEdge>(edgesList.Length, Allocator.Persistent); fE.CopyFrom(edgesList.AsArray());

            MapLevelData lvlData = new MapLevelData { LevelIndex = level, Sites = sites, Meta = meta, Cells = fC, Edges = fE, Tectonics = tectonicData, Climate = climateData, Hydrology = hydrologyData, Biomes = biomeData };
            
            EntityCreationPipeline.CreateEntities(EntityManager, lvlData, levelSettings, m_Settings.MapSize, edgesList);
            m_History.StoreLevel(lvlData);
            cellsList.Dispose(); edgesList.Dispose();
        }

        private void ApplyLloydRelaxation(NativeArray<float2> sites, NativeList<float2> verts, NativeList<int> counts, float2 mapSize)
        {
            int off = 0;
            for(int i=0; i<sites.Length; i++) {
                int c = counts[i];
                if (c>0) {
                    float2 centroid = 0; float area = 0;
                    for(int k=0; k<c; k++) {
                        float2 curr = verts[off+k]; float2 next = verts[off+(k+1)%c];
                        float a = curr.x*next.y - next.x*curr.y;
                        area += a; centroid += (curr+next)*a;
                    }
                    if(math.abs(area)>1e-5f) sites[i] = math.clamp(centroid/(area*3f), 0, mapSize);
                }
                off += c;
            }
        }

        private void CompleteGeneration()
        {
            m_IsComplete = true;
            Entity e = SystemAPI.GetSingletonEntity<MapSettings>();
            EntityManager.AddComponent<MapGeneratedTag>(e);
            EntityManager.RemoveComponent<MapGenerationInProgress>(e);
            Enabled = false;
        }
    }

    [BurstCompile]
    public struct TectonicGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        public int Level;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMeta;
        [ReadOnly] public NativeArray<TectonicPlateData> ParentTectonics;
        public NativeArray<TectonicPlateData> TectonicData;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            float baseHeight = 0;
            bool isOcean = false;

            if (Level == 0)
            {
                // GLOBAL (L0) GENERATION
                float2 center = MapSize * 0.5f;
                float dist = math.distance(pos, center);
                float maxRadius = math.min(MapSize.x, MapSize.y) * 0.45f;
                float distPercent = math.clamp(dist / maxRadius, 0f, 1f);

                float islandShape = (1.0f - math.pow(distPercent, 1.5f)) * 1.5f - 0.3f;
                float baseNoise = noise.snoise(pos * 0.0004f + new float2(Seed * 0.1f));
                
                float ridged = 1.0f - math.abs(noise.snoise(pos * 0.0012f + new float2(Seed * 0.5f)));
                ridged = math.pow(ridged, 2.5f); 

                baseHeight = islandShape + baseNoise * 0.3f + ridged * 0.8f * math.smoothstep(0.2f, 0.8f, islandShape);
                
                if (distPercent < 0.7f && baseNoise < -0.2f) baseHeight *= 0.8f; // coastline logic
                isOcean = baseHeight < 0.08f;
            }
            else
            {
                // CHILD GENERATION (INHERITANCE)
                // --- FIXED INHERITANCE LOGIC ---
                int parentIdx = SiteMeta[i].ParentIndex;
                if (ParentTectonics.Length > 0 && parentIdx >= 0 && parentIdx < ParentTectonics.Length)
                {
                    TectonicPlateData parentData = ParentTectonics[parentIdx];
                    if (parentData.IsOcean)
                    {
                        isOcean = true;
                        baseHeight = -0.2f; 
                    }
                    else
                    {
                        // Снижаем амплитуду шума с каждым уровнем, чтобы не ломать высоту
                        float amp = 0.15f / (float)(Level + 1); 
                        float freq = 0.002f * math.pow(3.0f, Level);
                        float detail = noise.snoise(pos * freq + new float2(Seed * 0.3f));

                        // Строгое наследование: Height = Parent + small_variation
                        baseHeight = parentData.BaseHeight + detail * amp;
                        
                        // Если родитель был сушей, ребенок скорее всего суша (если не очень низко)
                        isOcean = baseHeight < 0.01f;
                    }
                }
                else
                {
                    baseHeight = 0; isOcean = true;
                }
            }

            TectonicData[i] = new TectonicPlateData
            {
                IsOcean = isOcean,
                Velocity = float2.zero,
                BaseHeight = baseHeight,
                CrustAge = 0
            };
        }
    }

    [BurstCompile]
    public struct ClimateGenerationJob : IJobParallelFor
    {
        public int Seed;
        public float2 MapSize;
        public int Level;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMeta;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;
        [ReadOnly] public NativeArray<ClimateData> ParentClimate;
        public NativeArray<ClimateData> Climate;
        public NativeArray<BiomeData> Biomes;

        public void Execute(int i)
        {
            float2 pos = Sites[i];
            TectonicPlateData plate = Tectonics[i];
            float height = plate.BaseHeight;
            float temp = 0.5f, moisture = 0.5f;

            if (Level > 0 && ParentClimate.Length > 0)
            {
                int pIdx = SiteMeta[i].ParentIndex;
                if (pIdx >= 0 && pIdx < ParentClimate.Length) {
                    ClimateData pc = ParentClimate[pIdx];
                    // Меньше вариаций для климата
                    temp = pc.Temperature + noise.snoise(pos * 0.01f) * 0.02f;
                    moisture = pc.Moisture + noise.snoise(pos * 0.01f + new float2(100)) * 0.02f;
                }
            }
            else {
                float lat = pos.y / MapSize.y;
                temp = 1.0f - math.abs(lat - 0.5f) * 2.0f;
                if (height > 0.4f) temp -= (height - 0.4f) * 0.8f; 
                moisture = 0.5f + noise.snoise(pos * 0.0005f + new float2(Seed)) * 0.2f;
            }

            if (plate.IsOcean) { moisture = 1.0f; temp = 0.5f; }
            temp = math.clamp(temp, 0, 1); moisture = math.clamp(moisture, 0, 1);

            Climate[i] = new ClimateData { Temperature = temp, Moisture = moisture, WindDirection = 0 };
            
            // BIOME TABLE
            BiomeType type;
            if (plate.IsOcean) type = BiomeType.Ocean;
            else if (height < 0.07f) type = BiomeType.Coast;
            else if (height > 0.9f) type = BiomeType.Snow;
            else if (height > 0.6f) type = BiomeType.Mountain;
            else if (temp < 0.25f) type = BiomeType.Ice;
            else if (temp > 0.6f && moisture < 0.3f) type = BiomeType.Desert;
            else if (temp > 0.6f && moisture < 0.6f) type = BiomeType.Grassland;
            else type = BiomeType.Forest;

            Biomes[i] = new BiomeData { Type = type };
        }
    }
}