// ============================================================
// FILE: Assets\VoronoiMapGen\Features\MapGeneration\MapGenSession.cs
// ============================================================
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Features.Data;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Utils; 

namespace VoronoiMapGen.Features.MapGeneration
{
    public class MapGenSession : IDisposable
    {
        public int LevelIndex;
        public int TotalCells => Sites.IsCreated ? Sites.Length : 0;

        // Основные данные (Owned)
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> Meta;
        
        // Симуляция (Owned -> Transferred to History)
        public NativeArray<TectonicPlateData> Tectonics;
        public NativeArray<ClimateData> Climate;
        public NativeArray<HydrologyData> Hydrology;
        public NativeArray<BiomeData> Biomes;
        
        public NativeArray<SettlementData> Settlements;
        public NativeArray<DistrictData> Districts;

        // Геометрия (Temporary, always disposed)
        public NativeList<TriangleIndices> Triangles; 
        public NativeList<float2> CellVertices;       
        public NativeList<int> CellCounts;            
        public NativeList<VoronoiEdge> Edges;         

        // Финал (Owned -> Copied to History -> Disposed here)
        // (Клетки и ребра для истории копируются, поэтому оригиналы в сессии надо удалять)
        public NativeArray<VoronoiCell> FinalCells;
        
        public NativeParallelMultiHashMap<int, float2> PolyMap;
        public NativeParallelHashMap<int, Entity> ParentEntityMap;

        public MapGenSession(int level)
        {
            LevelIndex = level;
            Triangles = new NativeList<TriangleIndices>(Allocator.Persistent);
            CellVertices = new NativeList<float2>(Allocator.Persistent);
            CellCounts = new NativeList<int>(Allocator.Persistent);
            Edges = new NativeList<VoronoiEdge>(Allocator.Persistent);
        }

        public void AllocateSimulationArrays(int count)
        {
            Tectonics = new NativeArray<TectonicPlateData>(count, Allocator.Persistent);
            Climate = new NativeArray<ClimateData>(count, Allocator.Persistent);
            Biomes = new NativeArray<BiomeData>(count, Allocator.Persistent);
            Hydrology = new NativeArray<HydrologyData>(count, Allocator.Persistent);
            Settlements = new NativeArray<SettlementData>(count, Allocator.Persistent);
            Districts = new NativeArray<DistrictData>(count, Allocator.Persistent);
        }

        public void PrepareForBatching()
        {
            if (Edges.IsCreated)
            {
                if (PolyMap.IsCreated) PolyMap.Dispose();
                PolyMap = new NativeParallelMultiHashMap<int, float2>(Edges.Length * 2, Allocator.Persistent);
                
                for (int i = 0; i < Edges.Length; i++)
                {
                    VoronoiEdge e = Edges[i];
                    if (math.lengthsq(e.VertexA) < 0.001f) continue;
                    PolyMap.Add(e.SiteA, e.VertexA);
                    PolyMap.Add(e.SiteA, e.VertexB);
                    if (e.SiteB != -1) { 
                        PolyMap.Add(e.SiteB, e.VertexA); 
                        PolyMap.Add(e.SiteB, e.VertexB); 
                    }
                }
            }
            
            if (!ParentEntityMap.IsCreated) 
                ParentEntityMap = new NativeParallelHashMap<int, Entity>(math.max(1, TotalCells), Allocator.Persistent);
        }

        public MapLevelData ToLevelData()
        {
            // Создаем копии геометрии для хранения в истории
            // (Так как эти массивы мы тут же и удалим)
            var fC = new NativeArray<VoronoiCell>(FinalCells.IsCreated ? FinalCells.Length : 0, Allocator.Persistent);
            if(FinalCells.IsCreated) fC.CopyFrom(FinalCells);
            
            var fE = new NativeArray<VoronoiEdge>(Edges.IsCreated ? Edges.Length : 0, Allocator.Persistent);
            if(Edges.IsCreated) fE.CopyFrom(Edges.AsArray());

            return new MapLevelData
            {
                LevelIndex = LevelIndex,
                Sites = Sites,      // <-- Эти массивы передаются по ссылке!
                Meta = Meta,
                
                Tectonics = Tectonics,
                Climate = Climate,
                Hydrology = Hydrology,
                Biomes = Biomes,
                Settlements = Settlements,
                Districts = Districts,
                
                Cells = fC, // Копия
                Edges = fE  // Копия
            };
        }

        /// <summary>
        /// Вызывать ПЕРЕД Dispose, если мы передали массивы данных в HistoryData.
        /// Сбрасывает ссылки на массивы в default, чтобы Dispose их не трогал.
        /// </summary>
        public void ReleaseSimulationOwnership()
        {
            Sites = default;
            Meta = default;
            Tectonics = default;
            Climate = default;
            Hydrology = default;
            Biomes = default;
            Settlements = default;
            Districts = default;
        }

        public void Dispose()
        {
            // Удаляем данные, если мы всё ещё ими владеем (они Created и не дефолтные)
            if (Sites.IsCreated) Sites.Dispose();
            if (Meta.IsCreated) Meta.Dispose();
            
            if (Tectonics.IsCreated) Tectonics.Dispose();
            if (Climate.IsCreated) Climate.Dispose();
            if (Hydrology.IsCreated) Hydrology.Dispose();
            if (Biomes.IsCreated) Biomes.Dispose();
            
            if (Settlements.IsCreated) Settlements.Dispose();
            if (Districts.IsCreated) Districts.Dispose();

            // Временные данные всегда удаляем
            if (Triangles.IsCreated) Triangles.Dispose();
            if (CellVertices.IsCreated) CellVertices.Dispose();
            if (CellCounts.IsCreated) CellCounts.Dispose();
            if (Edges.IsCreated) Edges.Dispose();
            
            if (FinalCells.IsCreated) FinalCells.Dispose();
            if (PolyMap.IsCreated) PolyMap.Dispose();
            if (ParentEntityMap.IsCreated) ParentEntityMap.Dispose();
        }
    }
}