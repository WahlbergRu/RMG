using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Rendering.Terrain
{
    public static class MeshGenerationUtils
    {
        /// <summary>
        ///     Создает внутреннее кольцо вершин (Inset) на основе центроида
        /// </summary>
        public static void CalculateInsetRing(
            DynamicBuffer<CellPolygonVertex> sourceVerts,
            float2 center,
            float insetDistance,
            float yPos,
            ref NativeList<float3> outRing)
        {
            // Для выпуклых ячеек Вороного простой Lerp к центру работает отлично и дешево
            for (int i = 0; i < sourceVerts.Length; i++)
            {
                float2 v = new float2(sourceVerts[i].Value.x, sourceVerts[i].Value.z);
                float2 dir = v - center;
                float dist = math.length(dir);

                // Если дистанция слишком мала, не сжимаем дальше, чтобы не вывернуть полигон
                if (dist < insetDistance * 1.1f)
                {
                    outRing.Add(new float3(center.x, yPos, center.y));
                }
                else
                {
                    float2 newPos = center + math.normalize(dir) * (dist - insetDistance);
                    outRing.Add(new float3(newPos.x, yPos, newPos.y));
                }
            }
        }

        /// <summary>
        ///     Генерирует боковые стенки между двумя кольцами вершин (верхним и нижним)
        /// </summary>
        public static void AddWallSegment(
            NativeList<float3> topRing,
            NativeList<float3> bottomRing,
            NativeArray<SimpleVertex> vertexBuffer,
            NativeArray<int> indexBuffer,
            ref int vIndex,
            ref int iIndex)
        {
            int n = topRing.Length;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;

                float3 t1 = topRing[i];
                float3 t2 = topRing[next];
                float3 b1 = bottomRing[i];
                float3 b2 = bottomRing[next];

                // Вычисляем нормаль для грани
                float3 dir = t2 - t1;
                float3 down = b1 - t1;
                float3 normal = math.normalize(math.cross(down, dir));

                // 4 Вершины
                vertexBuffer[vIndex + 0] = new SimpleVertex { Position = t1, Normal = normal, UV = new float2(0, 1) };
                vertexBuffer[vIndex + 1] = new SimpleVertex { Position = t2, Normal = normal, UV = new float2(1, 1) };
                vertexBuffer[vIndex + 2] = new SimpleVertex { Position = b2, Normal = normal, UV = new float2(1, 0) };
                vertexBuffer[vIndex + 3] = new SimpleVertex { Position = b1, Normal = normal, UV = new float2(0, 0) };

                // 2 Треугольника
                int baseV = vIndex;
                indexBuffer[iIndex++] = baseV + 0;
                indexBuffer[iIndex++] = baseV + 1;
                indexBuffer[iIndex++] = baseV + 2;

                indexBuffer[iIndex++] = baseV + 0;
                indexBuffer[iIndex++] = baseV + 2;
                indexBuffer[iIndex++] = baseV + 3;

                vIndex += 4;
            }
        }

        /// <summary>
        ///     Добавляет "крышку" (Triangulation Fan)
        /// </summary>
        public static void AddCap(
            NativeList<float3> ring,
            DynamicBuffer<CellTriIndex> triIndices,
            NativeArray<SimpleVertex> vertexBuffer,
            NativeArray<int> indexBuffer,
            float3 normal,
            float noiseAmp,
            ref int vIndex,
            ref int iIndex)
        {
            int baseV = vIndex;

            // Копируем вершины
            for (int i = 0; i < ring.Length; i++)
            {
                float3 pos = ring[i];
                // Добавляем шум если нужно
                if (noiseAmp > 0)
                {
                    float n = noise.snoise(new float2(pos.x, pos.z) * 0.2f);
                    pos.y += n * noiseAmp;
                }

                vertexBuffer[vIndex++] = new SimpleVertex
                {
                    Position = pos,
                    Normal = normal,
                    UV = new float2(pos.x, pos.z) // Planar mapping
                };
            }

            // Копируем индексы (предполагаем, что они уже есть в буфере от Delaunay)
            // ИЛИ используем простой Fan, так как полигон выпуклый после Voronoi

            // Вариант 1: Использовать сохраненные индексы (если порядок совпадает)
            // Но кольца Inset могут слегка сбить это. Для безопасности используем Fan, если полигон выпуклый.
            // Ячейки Вороного всегда выпуклые (почти).

            for (int i = 1; i < ring.Length - 1; i++)
            {
                indexBuffer[iIndex++] = baseV + 0;
                indexBuffer[iIndex++] = baseV + i + 1;
                indexBuffer[iIndex++] = baseV + i;
            }
        }

        // Вспомогательная структура для вертекса
        public struct SimpleVertex
        {
            public float3 Position;
            public float3 Normal;
            public float2 UV;
        }
    }
}