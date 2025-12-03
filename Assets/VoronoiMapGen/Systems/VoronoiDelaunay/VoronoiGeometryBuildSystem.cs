using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VoronoiGeometryBuildSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            // Проверка состояний: ждем генерации карты, но не работаем, если геометрия уже построена
            if (!SystemAPI.HasSingleton<MapGeneratedTag>() || SystemAPI.HasSingleton<GeometryBuiltTag>())
                return;

            // 1. Получаем настройки
            var settingsEntity = SystemAPI.GetSingletonEntity<MapSettings>();
            var settings = SystemAPI.GetComponent<MapSettings>(settingsEntity);
            int maxLevel = settings.LevelsCount;

            // 2. Получаем все сущности ячеек
            var cellQuery = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, VoronoiSite>()
                .Build();
            
            // Копируем в массив, чтобы можно было безопасно итерироваться
            var entities = cellQuery.ToEntityArray(Allocator.Temp);

            // 3. Создаем кэш для быстрого доступа (чтобы не использовать тяжелые Lookup)
            var allCells = new NativeParallelHashMap<Entity, VoronoiCell>(entities.Length, Allocator.Temp);
            var allSites = new NativeParallelHashMap<Entity, VoronoiSite>(entities.Length, Allocator.Temp);
            
            for (int i = 0; i < entities.Length; i++)
            {
                allCells.Add(entities[i], EntityManager.GetComponentData<VoronoiCell>(entities[i]));
                allSites.Add(entities[i], EntityManager.GetComponentData<VoronoiSite>(entities[i]));
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 4. Подготавливаем многоразовые буферы для математики (Allocator.Temp очистится сам в конце кадра)
            // Использование Capacity 128 покрывает 99% случаев без реаллокации
            var reusePoly = new NativeList<float2>(128, Allocator.Temp);
            var reuseParent = new NativeList<float2>(128, Allocator.Temp);

            // // 5. ГЛАВНЫЙ ЦИКЛ ПО УРОВНЯМ (Иерархия)
            // // Мы обрабатываем уровни строго по порядку (0 -> 1 -> 2),
            // // чтобы когда мы перешли к детям (1), геометрия родителей (0) была уже готова.
            // for (int lvl = 0; lvl < maxLevel; lvl++)
            // {
            //     for (int i = 0; i < entities.Length; i++)
            //     {
            //         Entity e = entities[i];
            //         VoronoiCell cell = allCells[e];
            //         
            //         // Фильтр уровня
            //         if (cell.Level != lvl) continue;
            //         
            //         // Фильтр валидности (пропускаем "призраков")
            //         if (allSites[e].Value < -0.5f) continue;
            //         if (math.any(math.isnan(cell.Centroid))) continue;
            //
            //         // Проверка наличия родителя
            //         bool hasParent = (cell.Level > 0 && 
            //                           cell.ParentEntity != Entity.Null && 
            //                           allCells.ContainsKey(cell.ParentEntity));
            //
            //         // Обработка конкретной ячейки
            //         ProcessCellImmediate(e, cell, settings, hasParent, ecb, reusePoly, reuseParent);
            //     }
            // }
            
            // 6. Завершение
            // Создаем сущность-маркер, что геометрия готова
            var builtEntity = ecb.CreateEntity();
            ecb.AddComponent(builtEntity, new GeometryBuiltTag());
            
            // Применяем изменения (тэги Dirty и маркер)
            ecb.Playback(EntityManager);
            ecb.Dispose();
            
            entities.Dispose();
            allCells.Dispose();
            allSites.Dispose();
            reusePoly.Dispose();
            reuseParent.Dispose();
        }

        private void ProcessCellImmediate(
            Entity e, 
            VoronoiCell cell, 
            MapSettings settings, 
            bool hasParent,
            EntityCommandBuffer ecb,
            NativeList<float2> poly, 
            NativeList<float2> parentPoly) 
        {
            // Очистка буферов перед использованием
            poly.Clear();
            parentPoly.Clear();

            // 1. Загружаем исходную геометрию ячейки (из диаграммы Вороного)
            DynamicBuffer<CellPolygonVertex> vertexBuffer = EntityManager.GetBuffer<CellPolygonVertex>(e);
            
            // Если полигон вырожденный или пустой - пропускаем
            if (vertexBuffer.Length < 3) return;

            for (int k = 0; k < vertexBuffer.Length; k++) 
                poly.Add(new float2(vertexBuffer[k].Value.x, vertexBuffer[k].Value.z));

            // 2. Сортировка вершин (Clockwise), чтобы обеспечить стабильный порядок
            poly.Sort(new PolygonUtils.ClockwiseComparer(cell.Centroid));

            // 3. Обрезка по границам карты (Map Bounds)
            PolygonUtils.ClipToBounds(ref poly, settings.MapSize);

            // 4. Обрезка по Родителю (Hierarchical Clipping)
            if (hasParent)
            {
                // Читаем буфер родителя напрямую.
                // Так как цикл идет по уровням, родитель УЖЕ был обработан и обрезан в этом же кадре.
                if (EntityManager.HasBuffer<CellPolygonVertex>(cell.ParentEntity))
                {
                    DynamicBuffer<CellPolygonVertex> pBuf = EntityManager.GetBuffer<CellPolygonVertex>(cell.ParentEntity);
                    if (pBuf.Length >= 3)
                    {
                        // Копируем форму родителя
                        for(int p=0; p<pBuf.Length; p++) 
                            parentPoly.Add(new float2(pBuf[p].Value.x, pBuf[p].Value.z));

                        // ВЫПОЛНЯЕМ ОБРЕЗКУ
                        // Используем новую, точную версию PolygonUtils с double precision
                        PolygonUtils.ClipToPolygon(ref poly, parentPoly);
                    }
                }
            }

            // 5. Запись финальной ЛОГИЧЕСКОЙ геометрии
            // Внимание: мы пишем в буфер полигон "как есть" (без отступов и сглаживания).
            // Отступы для красоты будут накладываться только в VoronoiMeshCreateSystem.
            
            vertexBuffer.Clear();
            DynamicBuffer<CellTriIndex> triBuffer = EntityManager.GetBuffer<CellTriIndex>(e);
            triBuffer.Clear();

            if (poly.Length < 3) return;

            // Запись вершин
            for (int i = 0; i < poly.Length; i++)
                vertexBuffer.Add(new CellPolygonVertex { Value = new float3(poly[i].x, 0, poly[i].y) });

            // Триангуляция (Triangle Fan) - подходит для выпуклых полигонов (результат обрезки выпуклых)
            // Вершины: 0, 1, 2 | 0, 2, 3 | 0, 3, 4 ...
            for (int i = 1; i < poly.Length - 1; i++)
            {
                triBuffer.Add(new CellTriIndex { Value = 0 });
                triBuffer.Add(new CellTriIndex { Value = i });
                triBuffer.Add(new CellTriIndex { Value = i + 1 });
            }
            
            // Ставим флаг для системы рендеринга, что этот меш нужно перестроить
            ecb.AddComponent<CellDirtyFlag>(e);
        }
    }
}