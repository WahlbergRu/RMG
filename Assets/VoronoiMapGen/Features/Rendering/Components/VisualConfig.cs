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

        // Настройки рек
        public float RiverWidthScale; 
        public float RiverMeanderAmplitude; 
        public float RiverMeanderFrequency; 
        public float RiverNoiseInfluence; 
        
        // --- НОВОЕ ПОЛЕ ---
        public float TextureTiling; 
    }
}