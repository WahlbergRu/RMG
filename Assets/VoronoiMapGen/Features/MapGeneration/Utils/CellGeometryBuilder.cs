using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Utils
{
    public static class CellGeometryBuilder
    {
        /// <summary>
        ///     Строит плоский 2D полигон ячейки на основе списка ребер.
        /// </summary>
        public static void BuildPolygonForCell(
            DynamicBuffer<CellPolygonVertex> vertBuffer,
            DynamicBuffer<CellTriIndex> triBuffer,
            VoronoiCell cell,
            NativeParallelMultiHashMap<int, float2> polyMap, // Карта ребер
            float2 mapSize)
        {
            // 1. Собираем уникальные вершины
            NativeList<float2> uniqueVerts = new NativeList<float2>(16, Allocator.Temp);

            if (polyMap.TryGetFirstValue(cell.SiteIndex, out float2 v, out NativeParallelMultiHashMapIterator<int> it))
                do
                {
                    // Простой линейный поиск дубликатов (для малого кол-ва вершин это быстрее Hashset)
                    bool exists = false;
                    for (int k = 0; k < uniqueVerts.Length; k++)
                        if (math.distancesq(uniqueVerts[k], v) < 0.0001f)
                        {
                            exists = true;
                            break;
                        }

                    if (!exists) uniqueVerts.Add(v);
                } while (polyMap.TryGetNextValue(out v, ref it));

            // 2. Обрезаем по границам карты
            PolygonClipper.ClipToRect(ref uniqueVerts, mapSize);

            // 3. Сортируем вершины против часовой стрелки (CCW) для правильной триангуляции
            SortVerticesCCW(uniqueVerts, cell.Centroid);

            // 4. Записываем результат в буферы ECS
            vertBuffer.Clear();
            triBuffer.Clear();

            for (int k = 0; k < uniqueVerts.Length; k++)
                // Записываем плоские данные (Y=0), высота добавится позже в системах рендеринга
                vertBuffer.Add(new CellPolygonVertex { Value = new float3(uniqueVerts[k].x, 0, uniqueVerts[k].y) });

            // 5. Простая триангуляция "веером" (Triangle Fan)
            // Подходит, так как ячейки Вороного выпуклые
            if (uniqueVerts.Length >= 3)
                for (int k = 1; k < uniqueVerts.Length - 1; k++)
                {
                    triBuffer.Add(new CellTriIndex { Value = 0 });
                    triBuffer.Add(new CellTriIndex { Value = k + 1 });
                    triBuffer.Add(new CellTriIndex { Value = k });
                }

            uniqueVerts.Dispose();
        }

        private static void SortVerticesCCW(NativeList<float2> verts, float2 center)
        {
            // Сортировка пузырьком для малого массива (10-20 элементов) работает отлично и не аллоцирует память
            for (int i = 0; i < verts.Length - 1; i++)
            for (int j = i + 1; j < verts.Length; j++)
            {
                float angleA = math.atan2(verts[i].y - center.y, verts[i].x - center.x);
                float angleB = math.atan2(verts[j].y - center.y, verts[j].x - center.x);

                if (angleA > angleB)
                {
                    float2 temp = verts[i];
                    verts[i] = verts[j];
                    verts[j] = temp;
                }
            }
        }
    }
}