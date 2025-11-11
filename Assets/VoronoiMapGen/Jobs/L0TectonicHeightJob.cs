using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct L0TectonicHeightJob : IJobFor
    {
        [ReadOnly] public NativeArray<VoronoiSite> Sites;
        [ReadOnly] public NativeArray<VoronoiEdge> Edges; // Рёбра L0
        [ReadOnly] public float MapScale;
        
        public NativeArray<TerrainData> Heights;
        public NativeArray<TectonicData> TectonicData;
        public NativeArray<RelaxationData> RelaxationData;

        public void Execute(int index)
        {
            VoronoiSite site = Sites[index];
            float edgeInfluence = CalculateEdgeInfluence(site.Position);
            float centerRelaxation = CalculateCenterRelaxation(site.Position);

            // Базовая высота: океан или континент
            float baseHeight = edgeInfluence > 0.5f ? 0.8f : 0.5f; // Горы или континент
            if (edgeInfluence < 0.1f) baseHeight = -0.8f; // Океан

            // Релаксация: высота падает к центру
            float relaxedHeight = baseHeight * centerRelaxation;

            Heights[index] = new TerrainData
            {
                Elevation = relaxedHeight,
                Slope = 0f,
                Roughness = 0f,
                ElevationVariation = 0f
            };

            TectonicData[index] = new TectonicData
            {
                CollisionIntensity = edgeInfluence,
                PlateVelocity = float2.zero,
                IsOcean = baseHeight < 0f
            };

            RelaxationData[index] = new RelaxationData
            {
                EdgeInfluence = edgeInfluence,
                CenterRelaxation = centerRelaxation,
                DistanceToEdge = 1f - centerRelaxation
            };
        }

        private float CalculateEdgeInfluence(float2 position)
        {
            // Имитация влияния рёбер (в реальности через ближайшее ребро)
            float noise = Unity.Mathematics.noise.snoise(position * 0.001f);
            return math.saturate((noise + 1f) * 0.5f);
        }

        private float CalculateCenterRelaxation(float2 position)
        {
            // Релаксация к центру (чем ближе к центру ячейки, тем ниже)
            float distanceToCenter = math.length(position - float2.zero);
            return math.saturate(1f - distanceToCenter * 0.001f);
        }
    }
}