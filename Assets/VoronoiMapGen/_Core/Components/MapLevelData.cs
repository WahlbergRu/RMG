using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Data
{
    /// <summary>
    ///     Единый контейнер (Snapshot) для всех данных одного уровня генерации.
    ///     Восстановленный файл.
    /// </summary>
    public struct MapLevelData
    {
        public int LevelIndex;
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> Meta;
        public NativeArray<VoronoiCell> Cells;
        public NativeArray<VoronoiEdge> Edges;

        // Слои данных симуляции
        public NativeArray<TectonicPlateData> Tectonics;
        public NativeArray<ClimateData> Climate;
        public NativeArray<HydrologyData> Hydrology;
        public NativeArray<BiomeData> Biomes;

        // Проверка на валидность (создан ли уровень)
        public bool IsCreated => Sites.IsCreated && Cells.IsCreated;
        public int Length => Sites.Length;

        /// <summary>
        ///     Безопасная очистка всех ресурсов уровня
        /// </summary>
        public void Dispose()
        {
            if (Sites.IsCreated) Sites.Dispose();
            if (Meta.IsCreated) Meta.Dispose();
            if (Cells.IsCreated) Cells.Dispose();
            if (Edges.IsCreated) Edges.Dispose();

            if (Tectonics.IsCreated) Tectonics.Dispose();
            if (Climate.IsCreated) Climate.Dispose();
            if (Hydrology.IsCreated) Hydrology.Dispose();
            if (Biomes.IsCreated) Biomes.Dispose();
        }
    }
}