using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs; // Тут лежит ваша BiomeAssignmentJob

namespace VoronoiMapGen.Systems
{
    public static class BiomeGenerationPipeline
    {
        public static void GenerateBiomes(EntityManager em, MapSettings settings)
        {
            // 1. Собираем данные
            var query = em.CreateEntityQuery(
                typeof(VoronoiCell), 
                typeof(VoronoiSite), // Или VoronoiSitePosition, смотря что у вас есть
                typeof(CellBiome)
            );

            int count = query.CalculateEntityCount();
            if (count == 0) return;

            var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.TempJob);
            // Получаем позиции сайтов. Важно: порядок должен совпадать с ячейками (обычно они совпадают по индексам)
            // Но надежнее взять VoronoiSite и достать Position.
            var sitesMeta = query.ToComponentDataArray<VoronoiSite>(Allocator.TempJob);
            var sitesPos = new NativeArray<float2>(count, Allocator.TempJob);
            
            for(int i=0; i<count; i++) sitesPos[i] = sitesMeta[i].Position;

            var biomes = new NativeArray<CellBiome>(count, Allocator.TempJob);

            // 2. Настраиваем Джобу
            // ВАЖНО: Центр карты = MapSize / 2
            float2 center = settings.MapSize * 0.5f; 
            
            // Радиус острова = 45% от ширины карты (чтобы была вода по краям)
            float radius = math.min(settings.MapSize.x, settings.MapSize.y) * 0.45f;

            var job = new BiomeAssignmentJob
            {
                Cells = cells,
                Sites = sitesPos,
                Biomes = biomes,
                MapCenter = center, // <--- ИСПРАВЛЕНИЕ СМЕЩЕНИЯ
                MapRadius = radius
            };

            job.Schedule(count, 64).Complete();

            // 3. Применяем биомы обратно к сущностям
            // (В реальном проекте лучше использовать IJobEntity, но так тоже ок для Pipeline)
            var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                em.SetComponentData(entities[i], biomes[i]);
            }

            // 4. Очистка
            cells.Dispose();
            sitesMeta.Dispose();
            sitesPos.Dispose();
            biomes.Dispose();
            entities.Dispose();
            
            UnityEngine.Debug.Log($"Biome generation complete. Center: {center}, Radius: {radius}");
        }
    }
}