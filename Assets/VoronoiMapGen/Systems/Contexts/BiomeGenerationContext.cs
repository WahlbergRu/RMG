using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using Debug = UnityEngine.Debug;

namespace VoronoiMapGen.Contexts
{
    public class BiomeGenerationContext : IDisposable
    {
        private readonly EntityManager _em;
        private readonly MapSettings _settings;
        private readonly NativeList<Entity> _level1Cells;
        private readonly int _maxSiteIndex;

        public NativeArray<float2> Sites;
        public NativeArray<VoronoiCell> Cells;
        public NativeArray<CellBiome> Biomes;

        public bool IsInitialized { get; private set; } = false;

        public BiomeGenerationContext(EntityManager em, MapSettings settings, NativeList<Entity> level1Cells, int maxSiteIndex)
        {
            _em = em;
            _settings = settings;
            _level1Cells = level1Cells;
            _maxSiteIndex = maxSiteIndex;
        }

        public bool Initialize()
        {
            if (_maxSiteIndex < 0) return false;

            Sites = new NativeArray<float2>(_level1Cells.Length, Allocator.TempJob);
            Cells = new NativeArray<VoronoiCell>(_level1Cells.Length, Allocator.TempJob);
            Biomes = new NativeArray<CellBiome>(_level1Cells.Length, Allocator.TempJob);

            using NativeArray<float2> sitePositions = GetSitePositions(_maxSiteIndex);

            for (int i = 0; i < _level1Cells.Length; i++)
            {
                Entity e = _level1Cells[i];
                Cells[i] = _em.GetComponentData<VoronoiCell>(e);

                int siteIndex = Cells[i].SiteIndex;
                if (siteIndex >= 0 && siteIndex <= _maxSiteIndex)
                    Sites[i] = sitePositions[siteIndex];
                else
                    Sites[i] = Cells[i].Centroid;
            }

            IsInitialized = true;
            return true;
        }

        private NativeArray<float2> GetSitePositions(int maxSiteIndex)
        {
            NativeArray<float2> sitePositions = new NativeArray<float2>(maxSiteIndex + 1, Allocator.TempJob);
            for (int i = 0; i < sitePositions.Length; i++)
                sitePositions[i] = float2.zero;

            EntityQuery query = _em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiSite>());

            // This version requires JobHandle output
            NativeArray<VoronoiSite> sites = query.ToComponentDataArray<VoronoiSite>(Allocator.TempJob);

            for (int i = 0; i < sites.Length; i++)
            {
                VoronoiSite s = sites[i];
                if (s.Level == (int)DetailLevel.Regional && s.Index <= maxSiteIndex)
                    sitePositions[s.Index] = s.Position;
            }

            sites.Dispose(); // Don't forget to dispose the array from ToComponentDataArray

            return sitePositions;
        }

        public void GenerateBiomes()
        {
            if (!IsInitialized)
            {
                Debug.LogError("Cannot generate biomes: context not initialized");
                return;
            }

            BiomeAssignmentJob biomeJob = new BiomeAssignmentJob
            {
                Cells = Cells,
                Sites = Sites,
                Biomes = Biomes,
                MapCenter = _settings.MapSize * 0.5f,
                MapRadius = math.length(_settings.MapSize) * 0.5f
            };

            Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            biomeJob.Schedule(Sites.Length, 64).Complete();
            sw.Stop();
            Debug.Log($"  BiomeAssignmentJob completed in {sw.ElapsedMilliseconds} ms");
        }

        public void ApplyBiomes()
        {
            if (!IsInitialized)
            {
                Debug.LogError("Cannot apply biomes: context not initialized");
                return;
            }

            int biomesAdded = 0;
            for (int i = 0; i < _level1Cells.Length; i++)
            {
                Entity cellEntity = _level1Cells[i];
                if (!_em.Exists(cellEntity)) continue;

                if (!_em.HasComponent<CellBiome>(cellEntity))
                    _em.AddComponent<CellBiome>(cellEntity);

                _em.SetComponentData(cellEntity, Biomes[i]);
                biomesAdded++;
            }

            Debug.Log($"  Successfully added biomes to {biomesAdded} cells");
        }

        public void Dispose()
        {
            if (Sites.IsCreated) Sites.Dispose();
            if (Cells.IsCreated) Cells.Dispose();
            if (Biomes.IsCreated) Biomes.Dispose();
        }
    }
}
