using Unity.Collections;
using Unity.Entities;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    public static class CellQueryHelper
    {
        public static NativeList<Entity> GetLevel1Cells(EntityManager em)
        {
            var level1Cells = new NativeList<Entity>(Allocator.TempJob);

            var query = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>(), ComponentType.ReadOnly<DetailLevelData>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                var levelData = em.GetComponentData<DetailLevelData>(entity);
                if (levelData.Level == DetailLevel.Regional)
                    level1Cells.Add(entity);
            }

            return level1Cells;
        }

        public static int FindMaxSiteIndex(EntityManager em, DetailLevel level)
        {
            var maxSiteIndex = -1;

            var query = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiSite>());
            using var sites = query.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

            foreach (var site in sites)
            {
                if (site.Level == (int)level && site.Index > maxSiteIndex)
                    maxSiteIndex = site.Index;
            }

            return maxSiteIndex;
        }
    }
}