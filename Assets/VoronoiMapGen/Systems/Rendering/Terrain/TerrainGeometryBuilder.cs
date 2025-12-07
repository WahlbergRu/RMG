using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems.Rendering.Terrain
{
    public static class TerrainGeometryBuilder
    {
        // Рассчитывает, сколько памяти (вершин и индексов) нужно выделить
        public static void CalculateLayout(int vertexCount, TerrainVisualData style, bool isWater, out int totalVerts, out int totalIndices)
        {
            int n = vertexCount;
            int layers = (style.Style == TerrainStyle.Stratified && !isWater) ? style.StrataCount : 1;

            // Стены: 4 вершины на грань * кол-во граней * кол-во слоев + Крышка (n вершин)
            int wallVerts = layers * (4 * n);
            totalVerts = n + wallVerts;

            // Индексы: Крышка (fan = n-2 треугольника) + Стенки (layers * n * 2 треугольника)
            totalIndices = (n - 2) * 3 + (layers * n * 6);
        }

        public static void FillMesh(
            NativeArray<MeshGenerationUtils.SimpleVertex> vb,
            NativeArray<int> ib,
            DynamicBuffer<CellPolygonVertex> inputVerts,
            GenerationContext ctx,
            NativeList<float3> ringBufferA,
            NativeList<float3> ringBufferB)
        {
            if (ctx.Style.Style == TerrainStyle.Stratified && !ctx.IsWater)
            {
                GenerateStratified(vb, ib, inputVerts, ctx, ringBufferA, ringBufferB);
            }
            else
            {
                GenerateBlocky(vb, ib, inputVerts, ctx, ringBufferA, ringBufferB);
            }
        }

        private static void GenerateBlocky(
            NativeArray<MeshGenerationUtils.SimpleVertex> vb,
            NativeArray<int> ib,
            DynamicBuffer<CellPolygonVertex> inputVerts,
            GenerationContext ctx,
            NativeList<float3> ring0,
            NativeList<float3> ring1)
        {
            int vPtr = 0;
            int iPtr = 0;

            ring0.Clear(); ring1.Clear();

            // 1. Создаем контуры (Верх и Низ)
            MeshGenerationUtils.CalculateInsetRing(inputVerts, ctx.CenterPos.xz, 0, ctx.BaseHeight, ref ring0);
            MeshGenerationUtils.CalculateInsetRing(inputVerts, ctx.CenterPos.xz, 0, ctx.BottomDepth, ref ring1);

            // 2. Переводим в локальные координаты (относительно центра ячейки)
            MakeLocal(ring0, ctx.CenterPos);
            MakeLocal(ring1, ctx.CenterPos);

            // 3. Крышка (Top)
            int baseV = vPtr;
            for (int k = 0; k < ring0.Length; k++)
            {
                float3 pos = ring0[k];
                // Шум поверхности
                if (ctx.Style.TopNoiseAmplitude > 0)
                {
                    float worldX = pos.x + ctx.CenterPos.x;
                    float worldZ = pos.z + ctx.CenterPos.z;
                    pos.y += noise.snoise(new float2(worldX, worldZ) * 0.2f) * ctx.Style.TopNoiseAmplitude;
                }

                vb[vPtr++] = new MeshGenerationUtils.SimpleVertex { Position = pos, Normal = math.up(), UV = new float2(pos.x, pos.z) };
            }

            // Индексы крышки (Triangle Fan)
            for (int k = 1; k < ring0.Length - 1; k++)
            {
                ib[iPtr++] = baseV + 0;
                ib[iPtr++] = baseV + k + 1;
                ib[iPtr++] = baseV + k;
            }

            // 4. Стенки
            MeshGenerationUtils.AddWallSegment(ring0, ring1, vb, ib, ref vPtr, ref iPtr);
        }

        private static void GenerateStratified(
            NativeArray<MeshGenerationUtils.SimpleVertex> vb,
            NativeArray<int> ib,
            DynamicBuffer<CellPolygonVertex> inputVerts,
            GenerationContext ctx,
            NativeList<float3> ringTop,
            NativeList<float3> ringBot)
        {
            int vPtr = 0;
            int iPtr = 0;

            float currentY = ctx.BaseHeight;
            float currentInset = 0f;

            // 1. Крышка
            ringTop.Clear();
            MeshGenerationUtils.CalculateInsetRing(inputVerts, ctx.CenterPos.xz, currentInset, currentY, ref ringTop);
            MakeLocal(ringTop, ctx.CenterPos);

            int baseV = vPtr;
            for (int k = 0; k < ringTop.Length; k++)
            {
                float3 pos = ringTop[k];
                if (ctx.Style.TopNoiseAmplitude > 0)
                {
                    float worldX = pos.x + ctx.CenterPos.x;
                    float worldZ = pos.z + ctx.CenterPos.z;
                    pos.y += noise.snoise(new float2(worldX, worldZ) * 0.2f) * ctx.Style.TopNoiseAmplitude;
                }
                vb[vPtr++] = new MeshGenerationUtils.SimpleVertex { Position = pos, Normal = math.up(), UV = new float2(pos.x, pos.z) };
            }

            for (int k = 1; k < ringTop.Length - 1; k++)
            {
                ib[iPtr++] = baseV + 0;
                ib[iPtr++] = baseV + k + 1;
                ib[iPtr++] = baseV + k;
            }

            // 2. Слоистые стенки
            for (int k = 0; k < ctx.Style.StrataCount; k++)
            {
                float nextY = math.lerp(ctx.BaseHeight, ctx.BottomDepth, (float)(k + 1) / ctx.Style.StrataCount);
                if (k == 0) nextY = math.max(nextY, ctx.BaseHeight - 5.0f); 

                // Вычисляем смещение (Jitter + Inset)
                float jitter = noise.snoise(new float2(ctx.CenterPos.x, ctx.CenterPos.z + k) * 15f) * ctx.Style.StrataJitter;
                
                ringBot.Clear();
                MeshGenerationUtils.CalculateInsetRing(inputVerts, ctx.CenterPos.xz, currentInset + jitter + (k > 0 ? ctx.Style.StrataInset : 0), nextY, ref ringBot);
                MakeLocal(ringBot, ctx.CenterPos);

                MeshGenerationUtils.AddWallSegment(ringTop, ringBot, vb, ib, ref vPtr, ref iPtr);

                // Нижнее кольцо становится верхним для следующего слоя
                ringTop.Clear();
                ringTop.AddRange(ringBot.AsArray());
                currentY = nextY;
            }
        }

        private static void MakeLocal(NativeList<float3> ring, float3 center)
        {
            for (int i = 0; i < ring.Length; i++) ring[i] -= center;
        }
    }
}