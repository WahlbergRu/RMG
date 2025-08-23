using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    public static class MeshHelpers
    {
        public static ulong HashFloat2(float2 v)
        {
            return ((ulong)Mathf.FloatToHalf(v.x) << 16) | (ushort)Mathf.FloatToHalf(v.y);
        }

        /// <summary> Сортировка по часовой вокруг центра. </summary>
        public struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;
            public ClockwiseComparer(float2 center) => _center = center;

            public int Compare(float2 a, float2 b)
            {
                float2 da = a - _center;
                float2 db = b - _center;
                float angle = math.atan2(da.y, da.x) - math.atan2(db.y, db.x);
                return angle > 0 ? -1 : (angle < 0 ? 1 : 0);
            }
        }

        /// <summary> Наполняет outPairs парами индексов (i, i+1) для фан-триангуляции. </summary>
        public static void BuildFanTriPairs(int vertexCount, NativeList<int> outPairs)
        {
            outPairs.Clear();
            if (vertexCount < 3) return;
            for (int i = 1; i < vertexCount - 1; i++)
            {
                outPairs.Add(i);
                outPairs.Add(i + 1);
            }
        }
    }
}
