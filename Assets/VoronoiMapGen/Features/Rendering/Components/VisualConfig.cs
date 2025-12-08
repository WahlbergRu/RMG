using Unity.Entities;

namespace VoronoiMapGen.Features.Rendering.Components
{
    public enum TerrainStyle
    {
        Blocky,
        Smooth,
        Stratified
    }

    public struct TerrainVisualData : IBufferElementData
    {
        public TerrainStyle Style;
        public float HeightScale;
        public float BottomDepth;

        public int StrataCount;
        public float StrataInset;
        public float StrataJitter;
        public float TopNoiseAmplitude;

        // --- НОВЫЕ ПАРАМЕТРЫ ДЛЯ РЕК ---
        public float RiverWidthScale; // Множитель ширины
        public float RiverMeanderAmplitude; // Насколько сильно извивается (амплитуда синусоиды/шума)
        public float RiverMeanderFrequency; // Как часто извивается (частота)
        public float RiverNoiseInfluence; // Влияние случайного шума (хаос)
    }
}