using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Mesh
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct VoronoiMeshSystem : ISystem
    {
        private static Material _defaultMaterial;
        private EntityQuery _cellQuery;
        private EntityQuery _siteQuery;
        private EntityQuery _edgeQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _defaultMaterial = EnsureDefaultMaterial();

            _cellQuery = SystemAPI.QueryBuilder().WithAll<VoronoiCell>().Build();
            _siteQuery = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
            _edgeQuery = SystemAPI.QueryBuilder().WithAll<VoronoiEdge>().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            // Очистка ресурсов при необходимости
        }

        public void OnUpdate(ref SystemState state)
        {
            state.RequireForUpdate(_cellQuery);
            state.RequireForUpdate(_siteQuery);
            state.RequireForUpdate(_edgeQuery);

            // Карта ещё не готова
            if (!SystemAPI.HasSingleton<MapGeneratedTag>())
                return;

            // Меши уже построены
            if (SystemAPI.HasSingleton<VoronoiMeshGeneratedTag>())
                return;

            Debug.Log("VoronoiMeshSystem: Building Voronoi meshes...");

            using var cells = _cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.TempJob);
            using var sites = _siteQuery.ToComponentDataArray<VoronoiSite>(Allocator.TempJob);
            using var edges = _edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.TempJob);

            // Словарь siteIndex → position
            var siteLookup = new NativeParallelHashMap<int, float2>(sites.Length, Allocator.Temp);
            foreach (var site in sites)
                siteLookup[site.Index] = site.Position;

            foreach (var cell in cells)
            {
                if (!siteLookup.TryGetValue(cell.SiteIndex, out var sitePosition))
                {
                    Debug.LogWarning($"[VoronoiMeshSystem] Missing site {cell.SiteIndex} for cell");
                    continue;
                }

                CreateCellEntity(ref state, in cell, sitePosition, edges);
            }

            // Отмечаем, что меши построены
            state.EntityManager.CreateSingleton(new VoronoiMeshGeneratedTag());

            siteLookup.Dispose();
        }

        /// <summary>
        /// Создаёт entity для визуализации одной ячейки.
        /// </summary>
        private void CreateCellEntity(
            ref SystemState state,
            in VoronoiCell cell,
            float2 sitePosition,
            NativeArray<VoronoiEdge> allEdges)
        {
            var mesh = BuildCellMesh(cell, allEdges);
            if (mesh == null || mesh.vertexCount < 3)
            {
                Debug.LogError($"[VoronoiMeshSystem] Failed to build mesh for site {cell.SiteIndex}");
                return;
            }

            var entity = state.EntityManager.CreateEntity();

            // Локальная трансформация
            state.EntityManager.AddComponentData(entity, new LocalTransform
            {
                Position = new float3(sitePosition.x, 0f, sitePosition.y),
                Rotation = quaternion.identity,
                Scale = 1f
            });

            state.EntityManager.AddComponentData(entity, new VoronoiCellMeshTag());

            // Настраиваем рендер
            var desc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.On,
                receiveShadows: true);

            var meshArray = new RenderMeshArray(
                new[] { _defaultMaterial },
                new[] { mesh });

            var meshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
            RenderMeshUtility.AddComponents(entity, state.EntityManager, in desc, meshArray, meshInfo);
        }

        /// <summary>
        /// Генерирует меш полигона для ячейки.
        /// </summary>
        private static UnityEngine.Mesh BuildCellMesh(VoronoiCell cell, NativeArray<VoronoiEdge> edges)
        {
            var vertices = new NativeList<float2>(16, Allocator.Temp);
            var unique = new NativeHashSet<ulong>(16, Allocator.Temp);

            // Собираем вершины из рёбер
            foreach (var edge in edges)
            {
                if (edge.SiteA != cell.SiteIndex && edge.SiteB != cell.SiteIndex)
                    continue;

                TryAddVertex(edge.VertexA, vertices, unique);
                TryAddVertex(edge.VertexB, vertices, unique);
            }

            if (vertices.Length < 3)
            {
                unique.Dispose();
                vertices.Dispose();
                return MeshGenerationUtility.CreateQuadMesh(0.5f, 0.5f);
            }

            // Сортируем по часовой стрелке
            vertices.Sort(new ClockwiseComparer(cell.Centroid));

            var verts3D = new NativeList<Vector3>(vertices.Length, Allocator.Temp);
            foreach (var v in vertices)
                verts3D.Add(new Vector3(v.x, 0f, v.y));

            var tris = new NativeList<int>(Allocator.Temp);
            for (int i = 1; i < verts3D.Length - 1; i++)
            {
                tris.Add(0);
                tris.Add(i);
                tris.Add(i + 1);
            }

            var mesh = new UnityEngine.Mesh
            {
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(verts3D.AsArray());
            mesh.SetIndices(tris.AsArray(), MeshTopology.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            // Освобождаем ресурсы
            unique.Dispose();
            vertices.Dispose();
            verts3D.Dispose();
            tris.Dispose();

            return mesh;
        }

        private static void TryAddVertex(float2 v, NativeList<float2> list, NativeHashSet<ulong> unique)
        {
            var hash = HashVertex(v);
            if (unique.Add(hash))
                list.Add(v);
        }

        private static ulong HashVertex(float2 v) =>
            ((ulong)Mathf.FloatToHalf(v.x) << 16) | (ushort)Mathf.FloatToHalf(v.y);

        private static Material EnsureDefaultMaterial()
        {
            if (_defaultMaterial != null)
                return _defaultMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Legacy Shaders/Diffuse");

            if (shader == null)
                Debug.LogError("[VoronoiMeshSystem] No suitable shader found!");

            _defaultMaterial = new Material(shader)
            {
                color = new Color(
                    UnityEngine.Random.value * 0.5f + 0.5f,
                    UnityEngine.Random.value * 0.5f + 0.5f,
                    UnityEngine.Random.value * 0.5f + 0.5f
                )
            };

            return _defaultMaterial;
        }

        private struct ClockwiseComparer : IComparer<float2>
        {
            private readonly float2 _center;

            public ClockwiseComparer(float2 center) => _center = center;

            public int Compare(float2 a, float2 b)
            {
                var da = a - _center;
                var db = b - _center;
                var angle = math.atan2(da.y, da.x) - math.atan2(db.y, db.x);
                return angle > 0 ? -1 : (angle < 0 ? 1 : 0);
            }
        }
    }
}