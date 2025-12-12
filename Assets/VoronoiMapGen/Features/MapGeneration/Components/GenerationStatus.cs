using Unity.Collections;
using Unity.Entities;

namespace VoronoiMapGen.Components
{
    public struct GenerationStatus : IComponentData
    {
        public float TotalProgress; // 0.0 to 1.0
        public FixedString64Bytes CurrentStepName;
        public int TotalLevels;
        public int ProcessedLevels;
        public bool IsCompleted;
    }
}