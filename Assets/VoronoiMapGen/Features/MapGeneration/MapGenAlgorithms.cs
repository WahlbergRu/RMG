// ============================================================
// FILE: Assets\VoronoiMapGen\Features\MapGeneration\MapGenAlgorithms.cs
// ============================================================
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Components;          
using VoronoiMapGen.Features.Data;       
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.MapGeneration.Jobs;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Utils;
using VoronoiMapGen.Utils;
// ВАЖНО: правильный неймспейс для MapProcessingHelpers
using VoronoiMapGen.Features.Utils; 

namespace VoronoiMapGen.Features.MapGeneration
{
    public static class MapGenAlgorithms
    {
        public static void GenerateSites(
            MapGenSession session, 
            MapSettings settings, 
            LevelSettings lvlSettings, 
            MapHistoryData history)
        {
            NativeArray<VoronoiCell> pC = default; NativeArray<VoronoiSite> pM = default;
            NativeArray<HydrologyData> pH = default; NativeArray<TectonicPlateData> pT = default;
            NativeArray<ClimateData> pCl = default; NativeArray<SettlementData> pSett = default;

            if (history != null && history.TryGetLevel(session.LevelIndex - 1, out var pData)) {
                pC=pData.Cells; pM=pData.Meta; pH=pData.Hydrology; 
                pT=pData.Tectonics; pCl=pData.Climate; pSett=pData.Settlements; 
            }

            var result = SiteGenerator.Generate(settings, lvlSettings, session.LevelIndex, pC, pM, pH, pT, pCl, pSett);
            
            // Теперь MapProcessingHelpers доступен благодаря using
            var filtered = MapProcessingHelpers.FilterValidSites(result.sites, result.siteMetadata, Allocator.Persistent);
            
            if (result.sites.IsCreated) result.sites.Dispose(); 
            if (result.siteMetadata.IsCreated) result.siteMetadata.Dispose();

            session.Sites = filtered.sites;
            session.Meta = filtered.meta;
        }

        public static void BuildGeometry(MapGenSession s, float2 mapSize)
        {
            if (!s.Sites.IsCreated || s.Sites.Length < 3) return;
            DelaunayBuilder.Triangulate(s.Sites, ref s.Triangles, mapSize);
            s.CellVertices.Clear(); s.CellCounts.Clear();
            VoronoiBuilder.BuildCells(s.Sites, s.Triangles, mapSize, ref s.CellVertices, ref s.CellCounts);
        }

        public static void RelaxSites(MapGenSession s, float2 mapSize)
        {
            int off = 0; 
            for (int i = 0; i < s.Sites.Length; i++) 
            {
                int c = s.CellCounts[i]; 
                if (c > 0) {
                    float2 centroid = 0; 
                    float area = 0; 
                    for (int k = 0; k < c; k++) {
                        float2 curr = s.CellVertices[off+k]; 
                        float2 next = s.CellVertices[off+(k+1)%c];
                        float a = curr.x*next.y - next.x*curr.y; 
                        area += a; 
                        centroid += (curr+next)*a;
                    } 
                    if (math.abs(area) > 1e-5f) 
                        s.Sites[i] = math.clamp(centroid / (area * 3f), 0, mapSize);
                } 
                off += c;
            }
        }

        public static void RunSimulation(
            MapGenSession session, 
            MapSettings settings, 
            MapHistoryData history)
        {
            int cnt = session.TotalCells;
            session.AllocateSimulationArrays(cnt);

            // Tectonics
            var dTm = new NativeArray<TectonicPlateData>(0, Allocator.TempJob);
            NativeArray<TectonicPlateData> pT = default;
            if (history != null && history.TryGetLevel(session.LevelIndex-1, out var pDataT)) pT = pDataT.Tectonics;
            else pT = dTm;

            new TectonicGenerationJob { 
                Seed = settings.Seed, MapSize = settings.MapSize, Level = session.LevelIndex, 
                Sites = session.Sites, SiteMeta = session.Meta, ParentTectonics = pT, 
                TectonicData = session.Tectonics 
            }.Schedule(cnt, 64).Complete();
            dTm.Dispose();

            // Climate
            var dCm = new NativeArray<ClimateData>(0, Allocator.TempJob);
            NativeArray<ClimateData> pCl = default;
            if (history != null && history.TryGetLevel(session.LevelIndex-1, out var pDataC)) pCl = pDataC.Climate;
            else pCl = dCm;

            new ClimateGenerationJob { 
                Seed = settings.Seed, MapSize = settings.MapSize, Level = session.LevelIndex, 
                Sites = session.Sites, SiteMeta = session.Meta, Tectonics = session.Tectonics, 
                ParentClimate = pCl, Climate = session.Climate, Biomes = session.Biomes, 
                Config = settings.Climate 
            }.Schedule(cnt, 64).Complete();
            dCm.Dispose();

            // Hydrology
            var tEd = MapProcessingHelpers.ExtractEdgesFromDelaunay(session.Triangles, Allocator.TempJob);
            var nMap = new NativeParallelMultiHashMap<int, NeighborInfo>(tEd.Length*2, Allocator.TempJob);
            float limit = session.LevelIndex == 2 ? 800000f : 500000f;
            
            // ИСПРАВЛЕНИЕ: Убрано 'GraphJobs.' перед типом
            new BuildNeighborGraphJob { 
                Edges = tEd, SitePositions = session.Sites, Tectonics = session.Tectonics, 
                MaxConnectionDistSq = limit, NeighborsMap = nMap 
            }.Schedule().Complete();

            var tmpCells = new NativeArray<VoronoiCell>(cnt, Allocator.TempJob);
            for(int i=0; i<cnt; i++) tmpCells[i] = new VoronoiCell { SiteIndex=i, Centroid=session.Sites[i] };

            new CalculateHydrologyJob { 
                Cells = tmpCells, Tectonics = session.Tectonics, Climate = session.Climate, 
                NeighborsMap = nMap, Hydrology = session.Hydrology, Config = settings.Hydrology 
            }.Schedule().Complete();

            // Cities
            if (session.LevelIndex == 2) 
                SettlementBuilder.CalculateSettlements(tmpCells, session.Hydrology, session.Biomes, session.Tectonics, nMap, ref session.Settlements, 4, settings.Civilization, settings.Seed);
            
            if (session.LevelIndex == 3 && history.TryGetLevel(2, out var l2data))
                ZoningBuilder.CalculateDistricts(tmpCells, session.Meta, l2data.Settlements, l2data.Sites, ref session.Districts, settings.Seed + 3);

            tEd.Dispose(); nMap.Dispose(); tmpCells.Dispose();
        }

        public static void FinalizeEdgesAndCells(MapGenSession s)
        {
            var cellList = new NativeList<VoronoiCell>(s.Sites.Length, Allocator.Temp);
            var edgeList = new NativeList<VoronoiEdge>(Allocator.Temp);
            
            MapProcessingHelpers.AssembleFinalGeometry(
                s.LevelIndex, s.Sites, s.Meta, s.Triangles, s.CellVertices, s.CellCounts, 
                ref cellList, ref edgeList);

            s.FinalCells = new NativeArray<VoronoiCell>(cellList.Length, Allocator.Persistent);
            s.FinalCells.CopyFrom(cellList.AsArray());
            
            s.Edges.Clear();
            // ИСПРАВЛЕНИЕ: явный вызов .AsArray() для NativeList
            s.Edges.AddRange(edgeList.AsArray()); 

            cellList.Dispose(); edgeList.Dispose();
        }
    }
}