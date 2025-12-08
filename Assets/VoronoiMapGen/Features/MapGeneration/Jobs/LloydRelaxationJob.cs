using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration.Jobs
{
    [BurstCompile]
    public struct LloydRelaxationJob : IJob
    {
        [ReadOnly] public NativeArray<VoronoiCell> Cells;
        [ReadOnly] public NativeArray<VoronoiSite> SiteMetadata;
        [ReadOnly] public float2 MapSize;

        // Мы будем менять позиции сайтов
        public NativeArray<float2> Sites;

        public void Execute()
        {
            for (int i = 0; i < Cells.Length; i++)
            {
                VoronoiCell cell = Cells[i];
                int siteIndex = cell.SiteIndex;

                // Проверяем метаданные:
                // Если Value < -0.5f, значит это ПРИЗРАК (Ghost).
                // Призраков двигать нельзя, они держат рамку!
                if (SiteMetadata[siteIndex].Value < -0.5f) continue;

                // Двигаем сайт в центроид ячейки
                // Центроид - это геометрический центр полигона.
                // Но так как мы еще не обрезали полигоны по Сазерленду-Ходжману в Джобе (это делает рендер),
                // у нас центроид рассчитан VoronoiConstructionJob как среднее геометрическое вершин.
                // Для релаксации этого достаточно.

                // Дополнительная защита: не даем сайту улететь за карту
                float2 newPos = cell.Centroid;
                newPos = math.clamp(newPos, new float2(0), MapSize);

                // Применяем сглаживание (Lerp), чтобы сетка не схлопнулась слишком быстро (опционально)
                // Но классический Ллойд - это мгновенный перенос.
                Sites[siteIndex] = newPos;
            }
        }
    }
}