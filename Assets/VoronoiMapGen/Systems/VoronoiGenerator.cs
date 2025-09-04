using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using Debug = UnityEngine.Debug;

namespace VoronoiMapGen.Systems
{
    public static class VoronoiGenerator
    {
        /// <summary>
        /// Запускает VoronoiConstructionJob — принимает triangles как NativeList<DelaunayTriangle>.
        /// Возвращает NativeList<VoronoiEdge> и NativeList<VoronoiCell>.
        /// Вызывающий освобождает возвращаемые списки.
        /// </summary>
        public static (NativeList<VoronoiEdge> edges, NativeList<VoronoiCell> cells) Construct(
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            int level,
            in NativeList<DelaunayTriangle> triangles)
        {
            var voronoiEdges = new NativeList<VoronoiEdge>(math.max(4, sites.Length * 3), Allocator.TempJob);
            var voronoiCells = new NativeList<VoronoiCell>(math.max(4, sites.Length), Allocator.TempJob);

            var job = new VoronoiConstructionJob
            {
                Triangles = triangles,
                Sites = sites,
                SiteMetadata = siteMetadata,
                Level = level,
                Edges = voronoiEdges,
                Cells = voronoiCells
            };

            var sw = Stopwatch.StartNew();
            job.Run(); // предполагается, что VoronoiConstructionJob реализован как IJob и синхронно заполняет списки
            sw.Stop();
            Debug.Log($"  VoronoiConstructionJob.Run() finished in {sw.ElapsedMilliseconds} ms; voronoiEdges={voronoiEdges.Length}, voronoiCells={voronoiCells.Length}");

            return (voronoiEdges, voronoiCells);
        }
    }
}