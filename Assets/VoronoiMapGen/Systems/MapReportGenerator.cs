using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    public static class MapReportGenerator
    {
        public static void Report(EntityManager em, MapSettings settings, NativeArray<LevelSettings> levels)
        {
            Debug.Log($"Map generated with {levels.Length} levels");

            for (int level = 0; level < levels.Length; level++)
            {
                
                // Сайты текущего уровня
                int sites = CountEntitiesWithLevel<VoronoiSite>(em, level);
                // Ячейки текущего уровня
                int cells = CountEntitiesWithLevel<VoronoiCell>(em, level);
                // Рёбра текущего уровня
                int edges = CountEntitiesWithLevel<VoronoiEdge>(em, level);

                Debug.Log($"Level {level}: {sites} sites, {cells} cells, {edges} edges");
            }
        }
        
        

        private static int CountEntitiesWithLevel<T>(EntityManager em, int level) where T : unmanaged, IComponentData
        {
            // Создаём запрос с фильтром по DetailLevelData
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<DetailLevelData>()
            );
                
            // Собираем все сущности с нужным уровнем
            int count = 0;
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (em.HasComponent<DetailLevelData>(entity))
                    {
                        var levelData = em.GetComponentData<DetailLevelData>(entity);
                        if (levelData.Level == (DetailLevel)level)
                            count++;
                    }
                }
            }
            
            return count;
        }
    }
}