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

            // Базовые настройки
            MapSettings settings = new MapSettings { MapSize = new float2(1000, 1000) };
            if (SystemAPI.TryGetSingleton(out MapSettings s)) settings = s;

            var cellQuery = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, VoronoiSite>() 
                .Build();
            
            var entities = cellQuery.ToEntityArray(Allocator.Temp);
            var cells = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            var sites = cellQuery.ToComponentDataArray<VoronoiSite>(Allocator.Temp);

            if (entities.Length == 0) return;

            // Lookup для обрезки (оставляем для надежности)
            BufferLookup<CellPolygonVertex> bufferLookup = SystemAPI.GetBufferLookup<CellPolygonVertex>(isReadOnly: true);
            bufferLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                // Фильтр призраков
                if (sites[i].Value < -0.5f) continue;
                if (math.any(math.isnan(cells[i].Centroid))) continue;

                ProcessCell(e, cells[i], settings, ecb, bufferLookup);
                ecb.AddComponent<VoronoiCellMeshTag>(e);
            }
            
            var builtEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(builtEntity, new GeometryBuiltTag());
            ecb.Playback(state.EntityManager);
            
            entities.Dispose();
            cells.Dispose();
            sites.Dispose();
            ecb.Dispose();
        }

        private static void ProcessCell(Entity e, VoronoiCell cell, MapSettings settings, EntityCommandBuffer ecb, BufferLookup<CellPolygonVertex> lookup)
        {
            // 1. Получаем вершины
            if (!lookup.HasBuffer(e)) return;
            var srcBuffer = lookup[e];
            if (srcBuffer.Length < 3) return;

            var poly = new NativeList<float2>(srcBuffer.Length + 8, Allocator.Temp);
            for (int k = 0; k < srcBuffer.Length; k++) 
                poly.Add(new float2(srcBuffer[k].Value.x, srcBuffer[k].Value.z));

            // 2. Сортировка и Обрезка по карте
            poly.Sort(new PolygonUtils.ClockwiseComparer(cell.Centroid));
            PolygonUtils.ClipToBounds(ref poly, settings.MapSize);

            // 3. Обрезка по родителю (Dual Graph обычно точен, но это страховка)
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

            // === 4. СТИЛИЗАЦИЯ (ДИНАМИЧЕСКИЕ ПАРАМЕТРЫ) ===
            float inset = 0;
            int smooth = 0;

            if (cell.Level == 0) 
            {
                // L0: Континенты. Большие отступы (море), сильное скругление.
                inset = 15.0f; 
                smooth = 3;
            }
            else if (cell.Level == 1)
            {
                // L1: Регионы. Средние отступы (границы/реки).
                inset = 4.0f;
                smooth = 2;
            }
            else
            {
                // L2: Города/Кварталы. Тонкие щели (улицы), почти квадратные.
                inset = 1.5f;
                smooth = 1;
            }

            PolygonUtils.ApplyInset(ref poly, cell.Centroid, inset);
            PolygonUtils.ApplySmoothing(ref poly, smooth);
            // ==============================================

            // 5. Запись
            var outVerts = ecb.SetBuffer<CellPolygonVertex>(e);
            var outTris = ecb.SetBuffer<CellTriIndex>(e);
            outVerts.Clear();
            outTris.Clear();

            if (poly.Length < 3) { poly.Dispose(); return; }

            float h = cell.Level * 0.1f; 
            for (int i = 0; i < poly.Length; i++)
                outVerts.Add(new CellPolygonVertex { Value = new float3(poly[i].x, h, poly[i].y) });

            for (int i = 1; i < poly.Length - 1; i++)
            {
                outTris.Add(new CellTriIndex { Value = 0 });
                outTris.Add(new CellTriIndex { Value = i });
                outTris.Add(new CellTriIndex { Value = i + 1 });
            }
            
            ecb.AddComponent<CellDirtyFlag>(e);
            poly.Dispose();
        }
    }
}