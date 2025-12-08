using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration.Jobs
{
    [BurstCompile]
    public struct BuildNeighborGraphJob : IJob
    {
        [ReadOnly] public NativeList<VoronoiEdge> Edges;
        [ReadOnly] public NativeArray<float2> SitePositions;
        [ReadOnly] public NativeArray<TectonicPlateData> Tectonics;

        // Этот глобальный лимит оставим как "крайнюю меру" (санитарная отсечка)
        public float MaxConnectionDistSq;

        // Выходной граф
        public NativeParallelMultiHashMap<int, NeighborInfo> NeighborsMap;

        public void Execute()
        {
            NeighborsMap.Clear();
            var siteCount = SitePositions.Length;

            // -----------------------------------------------------------
            // ШАГ 1: Найдем дистанцию до САМОГО БЛИЗКОГО соседа для каждой точки.
            // Это даст нам "естественный размер" ячейки в данном месте карты.
            // -----------------------------------------------------------

            var minNeighborDist = new NativeArray<float>(siteCount, Allocator.Temp);

            // Инициализируем огромными значениями
            for (var i = 0; i < siteCount; i++) minNeighborDist[i] = float.MaxValue;

            // Пробегаем по всем ребрам и ищем минимумы
            for (var i = 0; i < Edges.Length; i++)
            {
                var edge = Edges[i];
                var a = edge.SiteA;
                var b = edge.SiteB;
                if (a < 0 || b < 0 || a >= siteCount || b >= siteCount) continue;

                var dSq = math.distancesq(SitePositions[a], SitePositions[b]);

                // Сразу игнорируем совсем дикие связи по глобальному лимиту
                if (dSq > MaxConnectionDistSq) continue;

                // Обновляем минимум для обеих точек (избегаем sqrt пока что для скорости)
                if (dSq < minNeighborDist[a]) minNeighborDist[a] = dSq;
                if (dSq < minNeighborDist[b]) minNeighborDist[b] = dSq;
            }

            // -----------------------------------------------------------
            // ШАГ 2: Строим граф, применяя ОТНОСИТЕЛЬНЫЙ фильтр.
            // -----------------------------------------------------------

            // Коэффициент допуска. Если сосед в 2.5 раза дальше, чем самый близкий - это не сосед.
            // (В квадрате: 2.5 * 2.5 = 6.25)
            var relativeThresholdSq = 6.25f;

            for (var i = 0; i < Edges.Length; i++)
            {
                var edge = Edges[i];
                var a = edge.SiteA;
                var b = edge.SiteB;

                if (a < 0 || b < 0 || a >= siteCount || b >= siteCount) continue;

                var posA = SitePositions[a];
                var posB = SitePositions[b];

                var distSq = math.distancesq(posA, posB);

                // --- Фильтр 1: Глобальный (Океаны и ошибки генерации) ---
                var isOceanA = Tectonics[a].IsOcean;
                var isOceanB = Tectonics[b].IsOcean;

                if (isOceanA && isOceanB) continue; // Вода-Вода нам не нужны для рек

                // Если береговая линия - режем дистанцию жестче
                var globalLimit = MaxConnectionDistSq;
                if (isOceanA != isOceanB) globalLimit *= 0.25f;

                if (distSq > globalLimit) continue;

                // --- Фильтр 2: ЛОКАЛЬНЫЙ АДАПТИВНЫЙ ---
                // У каждой точки есть свое понимание "близко" и "далеко".
                // Если для точки А этот сосед слишком далек ОТНОСИТЕЛЬНО её масштаба...
                var limitA = minNeighborDist[a] * relativeThresholdSq;
                if (distSq > limitA) continue;

                // И для точки Б тоже проверим (симметрия не обязательна, но желательна для графа)
                var limitB = minNeighborDist[b] * relativeThresholdSq;
                if (distSq > limitB) continue;

                // --- Фильтр пройден, добавляем ---
                var dist = math.sqrt(distSq);
                NeighborsMap.Add(a, new NeighborInfo { Index = b, Distance = dist });
                NeighborsMap.Add(b, new NeighborInfo { Index = a, Distance = dist });
            }

            minNeighborDist.Dispose();
        }
    }
}