using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Data
{
    /// <summary>
    /// Единый контейнер (Snapshot) для всех данных одного уровня генерации.
    /// Передается между системами и пайплайнами, чтобы не таскать 10 аргументов.
    /// </summary>
    public struct MapLevelData
    {
        public int LevelIndex;
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> Meta;
        public NativeArray<VoronoiCell> Cells;
        public NativeArray<VoronoiEdge> Edges; // Иногда нужны для дебага или кэша
        
        // Слои данных
        public NativeArray<TectonicPlateData> Tectonics;
        public NativeArray<ClimateData> Climate;
        public NativeArray<HydrologyData> Hydrology;
        public NativeArray<BiomeData> Biomes;

        // Проверка на валидность (создан ли уровень)
        public bool IsCreated => Sites.IsCreated && Cells.IsCreated;
        public int Length => Sites.Length;

        /// <summary>
        /// Безопасная очистка всех ресурсов уровня
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