using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering.Terrain
{
    // УБРАЛ [BurstCompile] с класса и методов.
    // Код будет скомпилирован Burst'ом, потому что его вызывает Burst Job.
    public static class TerrainGeometryBuilder
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CalculateLayout(int vertexCount, in TerrainVisualData style, bool isWater, out int totalVerts, out int totalIndices)
        {
            var n = vertexCount;
            var layers = style.Style == TerrainStyle.Stratified && !isWater ? style.StrataCount : 1;
            var wallVerts = layers * 4 * n;
            totalVerts = n + wallVerts;
            totalIndices = (n - 2) * 3 + layers * n * 6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FillMesh(
            // Используем NativeSlice - это более универсально для записи в части массива
            NativeArray<ProceduralVertex> outVerts,
            NativeArray<ProceduralIndex> outIndices,
            NativeArray<float3> input, 
            in GenerationContext ctx, // передаем по ссылке (in) для скорости
            ref NativeList<float3> ringBufferA,
            ref NativeList<float3> ringBufferB
        )
        {
            if (ctx.Style.Style == TerrainStyle.Stratified && !ctx.IsWater)
                GenerateStratified(outVerts, outIndices, input, ctx, ref ringBufferA, ref ringBufferB);
            else
                GenerateBlocky(outVerts, outIndices, input, ctx, ref ringBufferA, ref ringBufferB);
        }

        private static void GenerateBlocky(
            NativeArray<ProceduralVertex> vb,
            NativeArray<ProceduralIndex> ib,
            NativeArray<float3> input,
            in GenerationContext ctx,
            ref NativeList<float3> ring0,
            ref NativeList<float3> ring1)
        {
            int vPtr = 0;
            int iPtr = 0;
            ring0.Clear(); ring1.Clear();

            CalculateInsetRing(input, ctx.CenterPos.xz, 0, ctx.BaseHeight, ref ring0);
            CalculateInsetRing(input, ctx.CenterPos.xz, 0, ctx.BottomDepth, ref ring1);

            var baseV = vPtr;
            for (var k = 0; k < ring0.Length; k++)
            {
                var pos = ring0[k];
                if (ctx.Style.TopNoiseAmplitude > 0)
                {
                    pos.y += noise.snoise(new float2(pos.x + ctx.CenterPos.x, pos.z + ctx.CenterPos.z) * 0.2f) * ctx.Style.TopNoiseAmplitude;
                }
                
                // Texture Tiling
                float uvScale = ctx.Style.TextureTiling > 0.0001f ? ctx.Style.TextureTiling : 0.05f; 
                vb[vPtr++] = new ProceduralVertex { Position = pos, Normal = math.up(), Color = ctx.Color, UV = new float2(pos.x, pos.z) * uvScale };
            }

            for (var k = 1; k < ring0.Length - 1; k++)
            {
                ib[iPtr++] = new ProceduralIndex { Value = baseV + 0 };
                ib[iPtr++] = new ProceduralIndex { Value = baseV + k + 1 };
                ib[iPtr++] = new ProceduralIndex { Value = baseV + k };
            }
            AddWallSegment(ring0, ring1, ctx.Color, vb, ib, ref vPtr, ref iPtr);
        }

        private static void GenerateStratified(
            NativeArray<ProceduralVertex> vb,
            NativeArray<ProceduralIndex> ib,
            NativeArray<float3> input,
            in GenerationContext ctx,
            ref NativeList<float3> ringTop,
            ref NativeList<float3> ringBot)
        {
            int vPtr = 0;
            int iPtr = 0;
            var currentY = ctx.BaseHeight;
            var currentInset = 0f;

            ringTop.Clear();
            CalculateInsetRing(input, ctx.CenterPos.xz, currentInset, currentY, ref ringTop);

            var baseV = vPtr;
            for (var k = 0; k < ringTop.Length; k++)
            {
                var pos = ringTop[k];
                if (ctx.Style.TopNoiseAmplitude > 0)
                {
                     pos.y += noise.snoise(new float2(pos.x + ctx.CenterPos.x, pos.z + ctx.CenterPos.z) * 0.2f) * ctx.Style.TopNoiseAmplitude;
                }
                float uvScale = ctx.Style.TextureTiling > 0.0001f ? ctx.Style.TextureTiling : 0.05f; 
                vb[vPtr++] = new ProceduralVertex { Position = pos, Normal = math.up(), Color = ctx.Color, UV = new float2(pos.x, pos.z) * uvScale };
            }

            for (var k = 1; k < ringTop.Length - 1; k++)
            {
                ib[iPtr++] = new ProceduralIndex { Value = baseV + 0 };
                ib[iPtr++] = new ProceduralIndex { Value = baseV + k + 1 };
                ib[iPtr++] = new ProceduralIndex { Value = baseV + k };
            }

            for (var k = 0; k < ctx.Style.StrataCount; k++)
            {
                var ratio = (float)(k + 1) / ctx.Style.StrataCount;
                var nextY = math.lerp(ctx.BaseHeight, ctx.BottomDepth, ratio);
                if (k == 0) nextY = math.max(nextY, ctx.BaseHeight - 5.0f);

                var jitter = noise.snoise(new float2(ctx.CenterPos.x, ctx.CenterPos.z + k) * 15f) * ctx.Style.StrataJitter;

                ringBot.Clear();
                CalculateInsetRing(input, ctx.CenterPos.xz, currentInset + jitter + (k > 0 ? ctx.Style.StrataInset : 0), nextY, ref ringBot);

                var layerColor = ctx.Color * (1.0f - (ratio * 0.5f)); 
                layerColor.w = 1.0f; 

                AddWallSegment(ringTop, ringBot, layerColor, vb, ib, ref vPtr, ref iPtr);
                ringTop.Clear();
                ringTop.AddRange(ringBot.AsArray());
                currentY = nextY;
            }
        }
        
        private static void CalculateInsetRing(NativeArray<float3> sourceVerts, float2 center, float insetDistance, float yPos, ref NativeList<float3> outRing)
        {
            for (var i = 0; i < sourceVerts.Length; i++)
            {
                var v = new float2(sourceVerts[i].x, sourceVerts[i].z);
                var dir = v - center;
                var dist = math.length(dir);
                if (dist < insetDistance * 1.01f) outRing.Add(new float3(center.x, yPos, center.y));
                else {
                    var newPos = center + math.normalize(dir) * (dist - insetDistance);
                    outRing.Add(new float3(newPos.x, yPos, newPos.y));
                }
            }
        }

        private static void AddWallSegment(NativeList<float3> topRing, NativeList<float3> bottomRing, float4 color, NativeArray<ProceduralVertex> vBuffer, NativeArray<ProceduralIndex> iBuffer, ref int vIndex, ref int iIndex)
        {
            var n = topRing.Length;
            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var t1 = topRing[i]; var t2 = topRing[next];
                var b1 = bottomRing[i]; var b2 = bottomRing[next];

                var dir = t2 - t1;
                var down = b1 - t1;
                var normal = math.normalize(math.cross(down, dir));

                int baseV = vIndex;
                vBuffer[vIndex + 0] = new ProceduralVertex { Position = t1, Normal = normal, Color = color, UV = new float2(0, 1) };
                vBuffer[vIndex + 1] = new ProceduralVertex { Position = t2, Normal = normal, Color = color, UV = new float2(1, 1) };
                vBuffer[vIndex + 2] = new ProceduralVertex { Position = b2, Normal = normal, Color = color, UV = new float2(1, 0) };
                vBuffer[vIndex + 3] = new ProceduralVertex { Position = b1, Normal = normal, Color = color, UV = new float2(0, 0) };
                
                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 0 };
                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 1 };
                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 2 };

                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 0 };
                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 2 };
                iBuffer[iIndex++] = new ProceduralIndex { Value = baseV + 3 };
                vIndex += 4;
            }
        }
    }
}