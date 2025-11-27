using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>() || SystemAPI.HasSingleton<GeometryBuiltTag>())
                return;

            MapSettings settings = new MapSettings { MapSize = new float2(1000, 1000), Seed = 12345 };
            if (SystemAPI.TryGetSingleton(out MapSettings s)) settings = s;

            var cellQuery = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, VoronoiSite>() 
                .Build();
            
            var entities = cellQuery.ToEntityArray(Allocator.Temp);
            var cells = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            var sites = cellQuery.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

            if (entities.Length == 0) return;

            BufferLookup<CellPolygonVertex> bufferLookup = SystemAPI.GetBufferLookup<CellPolygonVertex>(isReadOnly: true);
            bufferLookup.Update(ref state);

            ComponentLookup<CellBiome> biomeLookup = SystemAPI.GetComponentLookup<CellBiome>(isReadOnly: true);
            biomeLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                // Фильтруем мусор
                if (sites[i].Value < -0.5f || math.any(math.isnan(cells[i].Centroid))) continue;

                BiomeType biomeType = BiomeType.Grassland;
                float centerHeight = 0;

                if (biomeLookup.HasComponent(e))
                {
                    var b = biomeLookup[e];
                    biomeType = b.Type;
                    centerHeight = b.Elevation; 
                }

                ProcessCell(e, cells[i], settings, ecb, bufferLookup, biomeType, centerHeight);
            }
            
            var builtEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(builtEntity, new GeometryBuiltTag());
            ecb.Playback(state.EntityManager);
            
            entities.Dispose(); cells.Dispose(); sites.Dispose(); ecb.Dispose();
        }

        private static void ProcessCell(
            Entity e, 
            VoronoiCell cell, 
            MapSettings settings, 
            EntityCommandBuffer ecb, 
            BufferLookup<CellPolygonVertex> lookup,
            BiomeType biomeType,
            float centerHeight)
        {
            if (!lookup.HasBuffer(e)) return;
            var srcBuffer = lookup[e];
            if (srcBuffer.Length < 3) return;

            var poly = new NativeList<float2>(srcBuffer.Length + 8, Allocator.Temp);
            for (int k = 0; k < srcBuffer.Length; k++) 
                poly.Add(new float2(srcBuffer[k].Value.x, srcBuffer[k].Value.z));

            // 1. Clipping & Sorting
            poly.Sort(new PolygonUtils.ClockwiseComparer(cell.Centroid));
            PolygonUtils.ClipToBounds(ref poly, settings.MapSize);

            // Обрезка родителем (чтобы дети не вылезали за границы L0/L1)
            if (cell.Level > 0 && cell.ParentEntity != Entity.Null && lookup.HasBuffer(cell.ParentEntity))
            {
                var parentBuffer = lookup[cell.ParentEntity];
                if (parentBuffer.Length >= 3)
                {
                    var parentPoly = new NativeArray<float3>(parentBuffer.Length, Allocator.Temp);
                    for(int p=0; p<parentBuffer.Length; p++) parentPoly[p] = parentBuffer[p].Value;
                    PolygonUtils.ClipToPolygon(ref poly, parentPoly);
                    parentPoly.Dispose();
                }
            }

            // 2. INSET (Отступы) - ГЛАВНОЕ ИЗМЕНЕНИЕ
            float inset = 0f;
            int smooth = 0;
            bool isWater = (biomeType == BiomeType.Ocean || biomeType == BiomeType.Coast);

            if (isWater)
            {
                // Вода наезжает на соседей, чтобы скрыть дно (отрицательный отступ)
                inset = (biomeType == BiomeType.Coast) ? -0.5f : -2.0f;
                smooth = 1; // Вода гладкая
            }
            else
            {
                // СУША: Inset = 0.0f -> Сплошной монолитный террейн без дыр!
                // Для L0 (глобальных плит) оставляем отступ, чтобы видеть структуру
                if (cell.Level == 0) inset = 5.0f; 
                else inset = 0.0f; 
                
                smooth = 0; // Угловатый Low Poly стиль
            }

            PolygonUtils.ApplyInset(ref poly, cell.Centroid, inset);
            if (smooth > 0) PolygonUtils.ApplySmoothing(ref poly, smooth);

            // 3. Генерация 3D Геометрии
            var outVerts = ecb.SetBuffer<CellPolygonVertex>(e);
            var outTris = ecb.SetBuffer<CellTriIndex>(e);
            outVerts.Clear();
            outTris.Clear();

            if (poly.Length < 3) { poly.Dispose(); return; }

            // --- НАСТРОЙКИ ВЫСОТЫ ---
            // Частота шума
            float noiseScale = 0.008f; 
            // Амплитуда шума (насколько бугристая поверхность)
            float noiseAmp = 0.3f;     
            
            // Базовая высота. Умножаем на 1.5, чтобы горы были выше!
            float baseH = math.max(0.1f, centerHeight * 1.5f); 
            
            var topVerts = new NativeList<float3>(poly.Length, Allocator.Temp);

            for (int i = 0; i < poly.Length; i++)
            {
                float2 vPos = poly[i];
                float y = 0;
                
                if (isWater)
                {
                    // Вода плоская
                    y = (biomeType == BiomeType.Coast) ? -0.05f : -0.3f;
                }
                else
                {
                    // СУША: 3D наклон
                    // Получаем уникальный шум для каждой вершины
                    float detail = noise.snoise(vPos * noiseScale + new float2(settings.Seed * 0.1f));
                    
                    // Формула: Основная высота + Детализация
                    // baseH * 0.7f -> Основной "стол"
                    // detail * noiseAmp * baseH -> Искривление краев вверх/вниз
                    y = baseH * 0.7f + (detail * noiseAmp * baseH);
                    
                    // Не даем уйти под воду
                    if (y < 0.01f) y = 0.01f;
                }
                topVerts.Add(new float3(vPos.x, y, vPos.y));
            }

            // --- A. Верхняя крышка ---
            int topStartIndex = 0; 
            for(int i=0; i<topVerts.Length; i++) outVerts.Add(new CellPolygonVertex { Value = topVerts[i] });

            for (int i = 1; i < topVerts.Length - 1; i++)
            {
                outTris.Add(new CellTriIndex { Value = topStartIndex + 0 });
                outTris.Add(new CellTriIndex { Value = topStartIndex + i + 1 });
                outTris.Add(new CellTriIndex { Value = topStartIndex + i });
            }

            // --- B. Стены (Skirts) ---
            // Строим стены, чтобы скрыть перепады высот между соседями
            if (biomeType != BiomeType.Ocean)
            {
                int bottomStartIndex = outVerts.Length;

                for (int i = 0; i < topVerts.Length; i++)
                {
                    float3 v = topVerts[i];
                    
                    // --- ИЗМЕНЕНИЕ: Глубина стенки ---
                    // Так как шаг между уровнями = 25, делаем стенку 30,
                    // чтобы она гарантированно вошла в нижний слой.
                    v.y = -30.0f; 
                    
                    outVerts.Add(new CellPolygonVertex { Value = v });
                }

                // ... (Триангуляция стенок без изменений) ...
                int len = topVerts.Length;
                for (int i = 0; i < len; i++)
                {
                    int next = (i + 1) % len;
                    
                    int topA = topStartIndex + i;
                    int topB = topStartIndex + next;
                    int botA = bottomStartIndex + i;
                    int botB = bottomStartIndex + next;

                    outTris.Add(new CellTriIndex { Value = topA });
                    outTris.Add(new CellTriIndex { Value = topB });
                    outTris.Add(new CellTriIndex { Value = botB });

                    outTris.Add(new CellTriIndex { Value = topA });
                    outTris.Add(new CellTriIndex { Value = botB });
                    outTris.Add(new CellTriIndex { Value = botA });
                }
            }
            
            ecb.AddComponent<CellDirtyFlag>(e);
            poly.Dispose();
            topVerts.Dispose();
        }
    }
}