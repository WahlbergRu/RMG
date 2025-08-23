using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using static VoronoiMapGen.Mesh.MeshHelpers;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Берёт VoronoiEdge, собирает уникальные вершины для каждой ячейки, сортирует CW,
    /// пишет в буферы CellPolygonVertex + фан-триангуляцию в CellTriIndex, ставит CellDirtyFlag.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VoronoiGeometryBuildSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;

            // читаем все рёбра разом
            var edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);

            // собираем сущности и данные для обработки
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // проходим по ячейкам
            var cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            using var cells = cellQuery.ToEntityArray(Allocator.Temp);

            for (int entityIndex = 0; entityIndex < cells.Length; entityIndex++)
            {
                var entity = cells[entityIndex];
                var cell = state.EntityManager.GetComponentData<VoronoiCell>(entity);
                var siteIndex = cell.SiteIndex;

                // Создаем буферы через ECB если их нет
                if (!state.EntityManager.HasBuffer<CellPolygonVertex>(entity))
                    ecb.AddBuffer<CellPolygonVertex>(entity);
                
                if (!state.EntityManager.HasBuffer<CellTriIndex>(entity))
                    ecb.AddBuffer<CellTriIndex>(entity);

                // Собираем вершины
                using var unique = new NativeHashSet<ulong>(16, Allocator.Temp);
                using var verts = new NativeList<float2>(16, Allocator.Temp);

                // собираем вершины из прилегающих рёбер
                for (int i = 0; i < edges.Length; i++)
                {
                    var e = edges[i];
                    if (e.SiteA != siteIndex && e.SiteB != siteIndex) continue;

                    var hA = HashFloat2(e.VertexA);
                    if (unique.Add(hA)) verts.Add(e.VertexA);

                    var hB = HashFloat2(e.VertexB);
                    if (unique.Add(hB)) verts.Add(e.VertexB);
                }

                // запасной квад, если вершины не набрались
                if (verts.Length < 3)
                {
                    verts.Clear();
                    verts.Add(new float2(-0.25f, -0.25f));
                    verts.Add(new float2( 0.25f, -0.25f));
                    verts.Add(new float2( 0.25f,  0.25f));
                    verts.Add(new float2(-0.25f,  0.25f));
                }

                // сортировка по центроиду клетки
                verts.Sort(new ClockwiseComparer(cell.Centroid));

                // Заполняем буфер вершин через ECB
                var vertsBuf = ecb.AddBuffer<CellPolygonVertex>(entity);
                for (int i = 0; i < verts.Length; i++)
                {
                    vertsBuf.Add(new CellPolygonVertex { Value = verts[i] });
                }

                // триангуляция (пары)
                using var pairs = new NativeList<int>(Allocator.Temp);
                BuildFanTriPairs(verts.Length, pairs);
                
                var triBuf = ecb.AddBuffer<CellTriIndex>(entity);
                for (int i = 0; i < pairs.Length; i++)
                {
                    triBuf.Add(new CellTriIndex { Value = pairs[i] });
                }

                // Добавляем компонент CellDirtyFlag через ECB
                ecb.AddComponent<CellDirtyFlag>(entity);
            }

            // Применяем все изменения
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}