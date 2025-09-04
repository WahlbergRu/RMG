using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Jobs;
using Debug = UnityEngine.Debug;

namespace VoronoiMapGen.Systems
{
    public static class DelaunayGenerator
    {
        /// <summary>
        /// Запускает DelaunayTriangulationJob (с твоей реализацией) и возвращает
        /// NativeList<DelaunayTriangle> и NativeList<int3> (ребра).
        /// Вызывающий должен Dispose() возвращённых списков.
        /// </summary>
        public static (NativeList<DelaunayTriangle> triangles, NativeList<int3> edges) Triangulate(
            in NativeArray<float2> sites,
            in NativeArray<VoronoiSite> siteMetadata,
            int level)
        {
            var triangles = new NativeList<DelaunayTriangle>(math.max(4, sites.Length * 2), Allocator.TempJob);
            var edges = new NativeList<int3>(math.max(4, sites.Length * 3), Allocator.TempJob);

            var job = new DelaunayTriangulationJob
            {
                Sites = sites,
                SiteMetadata = siteMetadata,
                Level = level,
                Triangles = triangles,
                Edges = edges
            };

            var sw = Stopwatch.StartNew();
            // DelaunayTriangulationJob реализован как IJob — выполняем синхронно
            job.Run();
            sw.Stop();
            Debug.Log($"  DelaunayTriangulationJob.Run() finished in {sw.ElapsedMilliseconds} ms; triangles={triangles.Length}, edges={edges.Length}");

            return (triangles, edges);
        }
    }
}