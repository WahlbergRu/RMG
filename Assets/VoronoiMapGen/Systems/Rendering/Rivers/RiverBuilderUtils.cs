using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using System.Collections.Generic;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class RiverBuilderUtils
    {
        // Константы
        public const float MAX_HEIGHT_DIFF = 300.0f;
        public const float MAX_DIST_SQ = 900f * 900f;
        public const float Z_FIGHT_BIAS = 0.2f;
        public const int CHUNK_LIMIT = 64000;

        // Валидация векторов на NaN и Infinity
        public static bool IsFinite(float3 v) => math.all(math.isfinite(v));

        public static bool ValidateVertices(List<Vector3> verts)
        {
            foreach (var v in verts)
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x)) return false;
            return true;
        }

        // Безопасный расчет индекса уровня (LOD)
        public static int GetSafeStyleIndex(DetailLevel lvl, int length)
        {
            int idx = (int)lvl;
            if (idx < 0) return 0;
            if (idx >= length) return length - 1;
            return idx;
        }

        // Расчет высоты (единая формула с ландшафтом)
        public static float CalculateBaseTerrainHeightSafe(CellBiome b, float heightScale)
        {
            if (b.Type == BiomeType.Ocean) return 0.2f;
            return 1.0f + (math.pow(math.max(0f, b.Elevation), 1.5f) * heightScale);
        }
    }
}