using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    // Вспомогательная структура для сортировки
    struct EdgeEntry : IComparable<EdgeEntry>
    {
        public int2 EdgeNodes; // Индексы точек (сайтов), отсортированные (min, max)
        public int TriangleIndex;
        public float2 CircumCenter;

        public int CompareTo(EdgeEntry other)
        {
            // Сначала сравниваем по первому узлу, потом по второму
            if (EdgeNodes.x != other.EdgeNodes.x)
                return EdgeNodes.x.CompareTo(other.EdgeNodes.x);
            return EdgeNodes.y.CompareTo(other.EdgeNodes.y);
        }
    }

    [BurstCompile]
    public struct VoronoiConstructionJob : IJob
    {
        [ReadOnly] public NativeArray<DelaunayTriangle> Triangles;
        [ReadOnly] public NativeArray<float2> Sites;
        [ReadOnly] public int Level;

        public NativeList<VoronoiEdge> Edges;
        public NativeList<VoronoiCell> Cells;

        public void Execute()
        {
            // 1. Генерация ячеек (это быстро, оставляем как есть)
            // ВАЖНО: Мы предполагаем, что Sites идут по порядку индексов от 0 до N
            for (int i = 0; i < Sites.Length; i++)
            {
                Cells.Add(new VoronoiCell
                {
                    SiteIndex = i,
                    Centroid = Sites[i], // Временно центр = сайт (для релаксации)
                    RegionIndex = i,
                    Level = Level,
                    ParentRegionIndex = -1,
                    Value = 0
                });
            }

            // 2. УМНАЯ Генерация ребер (Sort & Scan)
            // У каждого треугольника 3 грани. 
            int capacity = Triangles.Length * 3;
            var edgeEntries = new NativeList<EdgeEntry>(capacity, Allocator.Temp);

            for (int i = 0; i < Triangles.Length; i++)
            {
                var t = Triangles[i];
                
                // Добавляем 3 грани треугольника в список
                AddEdgeEntry(ref edgeEntries, t.A, t.B, i, t.CircumCenter);
                AddEdgeEntry(ref edgeEntries, t.B, t.C, i, t.CircumCenter);
                AddEdgeEntry(ref edgeEntries, t.C, t.A, i, t.CircumCenter);
            }

            // СОРТИРОВКА! Это сгруппирует одинаковые грани вместе.
            edgeEntries.Sort();

            // Проход по отсортированному списку
            // Если мы встречаем две записи с одинаковыми EdgeNodes (например 5-12 и 5-12),
            // значит, эти два треугольника - соседи. Соединяем их центры ребром.
            for (int i = 0; i < edgeEntries.Length - 1; i++)
            {
                var entryA = edgeEntries[i];
                var entryB = edgeEntries[i + 1];

                // Проверяем, совпадают ли грани (Sites)
                if (entryA.EdgeNodes.Equals(entryB.EdgeNodes))
                {
                    // Нашли соседей!
                    Edges.Add(new VoronoiEdge
                    {
                        SiteA = entryA.EdgeNodes.x,
                        SiteB = entryA.EdgeNodes.y,
                        // Ребро Вороного соединяет центры окружностей треугольников
                        VertexA = entryA.CircumCenter, 
                        VertexB = entryB.CircumCenter,
                        CellA = Entity.Null,
                        CellB = Entity.Null,
                        Level = Level
                    });

                    // Пропускаем второй элемент пары, так как мы его уже обработали
                    i++; 
                }
                // Если грани не совпали, значит это "внешняя" грань (граница карты), 
                // у нее нет второго соседа-треугольника. Мы ее игнорируем для графа Вороного.
            }

            edgeEntries.Dispose();
        }

        private void AddEdgeEntry(ref NativeList<EdgeEntry> list, int a, int b, int triIndex, float2 center)
        {
            // Всегда храним индексы как (min, max), чтобы грань 1-2 и 2-1 считалась одинаковой
            int min = math.min(a, b);
            int max = math.max(a, b);
            
            list.Add(new EdgeEntry
            {
                EdgeNodes = new int2(min, max),
                TriangleIndex = triIndex,
                CircumCenter = center
            });
        }
    }
}