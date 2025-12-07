// FILE: Assets\VoronoiMapGen\Systems\Rendering\Rivers\RiverGeometry.cs
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class RiverGeometry
    {
        private const float WATERFALL_THRESHOLD = 3.0f;

        // Обновленная сигнатура метода - принимает TerrainVisualData
        public static void BuildCascadeSegment(
            float3 start, float3 end, 
            float wStart, float wEnd,
            TerrainVisualData style, // <-- Передаем весь стиль
            float yNoiseAmpA, float yNoiseAmpB, // Амплитуды вертикального шума
            float slope,
            List<Vector3> verts, List<int> tris, List<Vector2> uvs, 
            int seed)
        {
            float3 midPoint = (start + end) * 0.5f; 
            float3 cliffTop = new float3(midPoint.x, start.y, midPoint.z);
            float3 cliffBot = new float3(midPoint.x, end.y, midPoint.z);

            float widthMid = (wStart + wEnd) * 0.5f;
            float diff = math.abs(start.y - end.y);
            bool isWaterfall = diff > WATERFALL_THRESHOLD;

            // Передаем стиль дальше
            BuildPhysicsStrip(start, cliffTop, wStart, widthMid, start.y, start.y, 
                              style, slope, yNoiseAmpA, yNoiseAmpA, 
                              verts, tris, uvs, seed, 0f, 0.5f);

            if (isWaterfall || diff > 0.05f) 
            {
                BuildWaterfallFace(cliffTop, cliffBot, widthMid, verts, tris, uvs);
            }

            BuildPhysicsStrip(cliffBot, end, widthMid, wEnd, end.y, end.y, 
                              style, slope, yNoiseAmpB, yNoiseAmpB, 
                              verts, tris, uvs, seed, 0.5f, 1.0f);
        }

        private static void BuildPhysicsStrip(
            float3 pA, float3 pB, float wA, float wB, float hA, float hB,
            TerrainVisualData style, float slope, // <-- Стиль
            float tNoiseA, float tNoiseB,
            List<Vector3> verts, List<int> tris, List<Vector2> uvs,
            int seed, float tGStart, float tGEnd)
        {
            float3 dir = pB - pA;
            float len = math.length(new float2(dir.x, dir.z)); 
            
            if (len < 0.01f) return;

            float3 fwd = math.normalize(dir);
            float3 right = new float3(-fwd.z, 0, fwd.x);

            // --- ИСПОЛЬЗУЕМ КОЭФФИЦИЕНТЫ ИЗ НАСТРОЕК ---
            float meanderFreq = style.RiverMeanderFrequency; 
            float meanderAmp = style.RiverMeanderAmplitude; 
            float noiseInf = style.RiverNoiseInfluence;

            // Адаптивное количество шагов в зависимости от длины и изгиба
            int steps = math.max(2, (int)(len / 2.0f) + (int)(meanderAmp * 1.5f));
            int baseIdx = verts.Count;
            
            // Смещение по закону Бэра (легкий естественный изгиб даже для прямой реки)
            float baerOffset = (wA + wB) * 0.5f * 0.1f; 

            for (int i = 0; i <= steps; i++)
            {
                float tLoc = (float)i / steps;
                float tTot = math.lerp(tGStart, tGEnd, tLoc); // Глобальное время (0..1)

                float3 p = math.lerp(pA, pB, tLoc);
                float w = math.lerp(wA, wB, tLoc);
                float h = math.lerp(hA, hB, tLoc);

                // --- ФОРМУЛА ИЗГИБА (SPLINE) ---
                
                // 1. "Конверт" (Envelope): изгибы сильны в центре сегмента и сходят на нет к краям,
                // чтобы сегменты стыковались идеально.
                float env = math.pow(math.max(0, math.sin(tTot * math.PI)), 0.6f);
                
                // 2. Основной Шум (Simplex Noise) для извилистости
                float nVal = noise.snoise(new float2(p.x, p.z) * meanderFreq + new float2(seed * 0.15f));
                
                // 3. Дополнительный детальный шум для хаоса
                float detailNoise = noise.snoise(new float2(p.x, p.z) * (meanderFreq * 3.5f) + new float2(seed * 0.9f)) * 0.3f;

                // Итоговое смещение вбок
                float offsetValue = ((nVal + detailNoise) * noiseInf) + baerOffset;
                float3 totalOffset = right * (offsetValue * meanderAmp * env);

                float3 center = p + totalOffset;
                center.y = h;

                // Приклеивание к террейну (Gluing) по вертикали
                float tNAmp = math.lerp(tNoiseA, tNoiseB, tLoc);
                if (tNAmp > 0)
                {
                    center.y += noise.snoise(new float2(center.x, center.z) * 0.2f) * tNAmp;
                }

                verts.Add(center - right * w * 0.5f);
                verts.Add(center + right * w * 0.5f);
                
                float vCoords = tTot * (len * 0.1f);
                uvs.Add(new Vector2(0, vCoords));
                uvs.Add(new Vector2(1, vCoords));

                if (i > 0) 
                {
                    int cL = baseIdx + i*2, cR = cL+1, pL = baseIdx + (i-1)*2, pR = pL+1;
                    tris.Add(pL); tris.Add(cR); tris.Add(cL);
                    tris.Add(pL); tris.Add(pR); tris.Add(cR);
                }
            }
        }

        private static void BuildWaterfallFace(float3 top, float3 bot, float width, List<Vector3> verts, List<int> tris, List<Vector2> uvs)
        {
            if (verts.Count < 2) return;
            int iTL = verts.Count - 2; int iTR = verts.Count - 1;
            Vector3 pTL = verts[iTL]; Vector3 pTR = verts[iTR];
            Vector3 pBL = new Vector3(pTL.x, bot.y, pTL.z);
            Vector3 pBR = new Vector3(pTR.x, bot.y, pTR.z);

            verts.Add(pBL); verts.Add(pBR);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            int iBL = verts.Count - 2; int iBR = verts.Count - 1; 
            tris.Add(iTL); tris.Add(iTR); tris.Add(iBL);
            tris.Add(iBL); tris.Add(iTR); tris.Add(iBR);
        }
    }
}