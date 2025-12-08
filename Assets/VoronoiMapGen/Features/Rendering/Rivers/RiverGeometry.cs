using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering
{
    // Burst optimization allowed now
    [BurstCompile]
    public static class RiverGeometry
    {
        private const float WATERFALL_THRESHOLD = 3.0f;

        public static void BuildCascadeSegment(
            float3 start, float3 end,
            float wStart, float wEnd,
            TerrainVisualData style,
            float yNoiseAmpA, float yNoiseAmpB, 
            float slope,
            ref NativeList<float3> verts,  // Using ref NativeList for zero-garbage
            ref NativeList<int> tris, 
            ref NativeList<float2> uvs,
            int seed)
        {
            float3 midPoint = (start + end) * 0.5f;
            // Ensure cliffTop and cliffBot maintain their vertical difference
            float3 cliffTop = new float3(midPoint.x, start.y, midPoint.z);
            float3 cliffBot = new float3(midPoint.x, end.y, midPoint.z);

            float widthMid = (wStart + wEnd) * 0.5f;
            float diff = math.abs(start.y - end.y);
            bool isWaterfall = diff > WATERFALL_THRESHOLD;

            // Strip 1: Start -> Middle (Top)
            BuildPhysicsStrip(start, cliffTop, wStart, widthMid, start.y, start.y,
                style, slope, yNoiseAmpA, yNoiseAmpA,
                ref verts, ref tris, ref uvs, seed, 0f, 0.5f);

            // Waterfall Face
            if (isWaterfall || diff > 0.05f) 
            {
                BuildWaterfallFace(cliffTop, cliffBot, widthMid, ref verts, ref tris, ref uvs);
            }

            // Strip 2: Middle (Bot) -> End
            BuildPhysicsStrip(cliffBot, end, widthMid, wEnd, end.y, end.y,
                style, slope, yNoiseAmpB, yNoiseAmpB,
                ref verts, ref tris, ref uvs, seed, 0.5f, 1.0f);
        }

        private static void BuildPhysicsStrip(
            float3 pA, float3 pB, float wA, float wB, float hA, float hB,
            TerrainVisualData style, float slope,
            float tNoiseA, float tNoiseB,
            ref NativeList<float3> verts, ref NativeList<int> tris, ref NativeList<float2> uvs,
            int seed, float tGStart, float tGEnd)
        {
            float3 dir = pB - pA;
            float len = math.length(new float2(dir.x, dir.z));

            if (len < 0.01f) return;

            float3 fwd = math.normalize(dir);
            // Replaced Vector3 logic with Mathematics
            float3 right = new float3(-fwd.z, 0, fwd.x);

            float meanderFreq = style.RiverMeanderFrequency;
            float meanderAmp = style.RiverMeanderAmplitude;
            float noiseInf = style.RiverNoiseInfluence;

            // Use 'math' instead of 'Mathf'
            int steps = math.max(2, (int)(len / 2.0f) + (int)(meanderAmp * 1.5f));
            int baseIdx = verts.Length;

            float baerOffset = (wA + wB) * 0.5f * 0.1f;

            for (int i = 0; i <= steps; i++)
            {
                float tLoc = (float)i / steps;
                float tTot = math.lerp(tGStart, tGEnd, tLoc);

                float3 p = math.lerp(pA, pB, tLoc);
                float w = math.lerp(wA, wB, tLoc);
                float h = math.lerp(hA, hB, tLoc);

                // --- Spline Logic ---
                // Envelope
                float env = math.pow(math.max(0, math.sin(tTot * math.PI)), 0.6f);

                // Simplex Noise (Using Unity.Mathematics.noise)
                float2 noisePos = new float2(p.x, p.z);
                float nVal = noise.snoise(noisePos * meanderFreq + new float2(seed * 0.15f));
                float detailNoise = noise.snoise(noisePos * (meanderFreq * 3.5f) + new float2(seed * 0.9f)) * 0.3f;

                float offsetValue = (nVal + detailNoise) * noiseInf + baerOffset;
                float3 totalOffset = right * (offsetValue * meanderAmp * env);

                float3 center = p + totalOffset;
                center.y = h;

                // Vertical noise gluing
                float tNAmp = math.lerp(tNoiseA, tNoiseB, tLoc);
                if (tNAmp > 0)
                {
                     center.y += noise.snoise(new float2(center.x, center.z) * 0.2f) * tNAmp;
                }

                verts.Add(center - right * w * 0.5f);
                verts.Add(center + right * w * 0.5f);

                float vCoords = tTot * (len * 0.1f);
                uvs.Add(new float2(0, vCoords));
                uvs.Add(new float2(1, vCoords));

                if (i > 0)
                {
                    int cL = baseIdx + i * 2, cR = cL + 1, pL = baseIdx + (i - 1) * 2, pR = pL + 1;
                    tris.Add(pL);
                    tris.Add(cR);
                    tris.Add(cL);
                    
                    tris.Add(pL);
                    tris.Add(pR);
                    tris.Add(cR);
                }
            }
        }

        private static void BuildWaterfallFace(float3 top, float3 bot, float width, 
            ref NativeList<float3> verts, ref NativeList<int> tris, ref NativeList<float2> uvs)
        {
            if (verts.Length < 2) return;
            
            int iTL = verts.Length - 2;
            int iTR = verts.Length - 1;
            
            float3 pTL = verts[iTL];
            float3 pTR = verts[iTR];
            float3 pBL = new float3(pTL.x, bot.y, pTL.z);
            float3 pBR = new float3(pTR.x, bot.y, pTR.z);

            verts.Add(pBL);
            verts.Add(pBR);
            
            uvs.Add(new float2(0, 0));
            uvs.Add(new float2(1, 0));
            
            int iBL = verts.Length - 2;
            int iBR = verts.Length - 1;
            
            // Waterfall Face Triangles
            tris.Add(iTL);
            tris.Add(iTR);
            tris.Add(iBL);
            
            tris.Add(iBL);
            tris.Add(iTR);
            tris.Add(iBR);
        }
    }
}