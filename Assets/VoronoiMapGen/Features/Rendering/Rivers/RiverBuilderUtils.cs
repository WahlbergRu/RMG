using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Features.Rendering.Rivers
{
    public static class RiverBuilderUtils
    {
        public const float MAX_HEIGHT_DIFF = 300.0f;
        public const float MAX_DIST_SQ = 810000f; 
        public const float Z_FIGHT_BIAS = 0.2f;
        
        // --- ADDED MISSING CONSTANT ---
        public const int CHUNK_LIMIT = 64000; 

        public static bool IsFinite(float3 v)
        {
            return math.all(math.isfinite(v));
        }

        public static bool ValidateVertices(Unity.Collections.NativeList<float3> verts)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                float3 v = verts[i];
                if (math.any(math.isnan(v)) || math.any(math.isinf(v)))
                    return false;
            }
            return true;
        }

        public static float CalculateBaseTerrainHeightSafe(CellBiome b, float heightScale)
        {
            if (b.Type == BiomeType.Ocean) return 0.2f;
            return 1.0f + math.pow(math.max(0f, b.Elevation), 1.5f) * heightScale;
        }
        
        public static int GetSafeStyleIndex(DetailLevel lvl, int length)
        {
            int idx = (int)lvl;
            if (idx < 0) return 0;
            if (idx >= length) return length - 1;
            return idx;
        }
    }
}