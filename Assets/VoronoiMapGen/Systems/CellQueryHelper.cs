using Unity.Collections;
using Unity.Entities;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    public static class CellQueryHelper
    {
        public static NativeList<Entity> GetLevel1Cells(EntityManager em)
        {
            NativeList<Entity> level1Cells = new NativeList<Entity>(Allocator.TempJob);

            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>(), ComponentType.ReadOnly<DetailLevelData>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            foreach (Entity entity in entities)
            {
                DetailLevelData levelData = em.GetComponentData<DetailLevelData>(entity);
                if (levelData.Level == DetailLevel.Regional)
                    level1Cells.Add(entity);
            }

            return level1Cells;
        }

        public static int FindMaxSiteIndex(EntityManager em, DetailLevel level)
        {
            int maxSiteIndex = -1;

            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiSite>());
            using NativeArray<VoronoiSite> sites = query.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

            foreach (VoronoiSite site in sites)
            {
                if (site.Level == (int)level && site.Index > maxSiteIndex)
                    maxSiteIndex = site.Index;
            }

            return maxSiteIndex;
        }
    }
}