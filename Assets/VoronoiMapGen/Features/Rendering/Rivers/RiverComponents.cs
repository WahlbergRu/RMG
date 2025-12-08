using Unity.Entities;

namespace VoronoiMapGen.Features.Rendering.Rivers
{
    // Компонент для хранения данных конкретного сегмента (если нужен)
    public struct RiverSegmentOwner : IComponentData
    {
        public int ParentSiteIndex;
        public int TargetSiteIndex;
        public float Flux;
        public int Level;
    }

    // --- НОВОЕ: Тэг для объединенных мешей ---
    public struct RiverChunkTag : IComponentData
    {
    }
}