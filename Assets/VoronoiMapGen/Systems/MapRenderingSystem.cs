using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiGeometryBuildSystem))]
    public partial class MapRenderingSystem : SystemBase
    {
        private Material _cellMaterial;
        private Material _roadMaterial;
        private Material _borderMaterial;

        private bool _cellsSpawned;
        private bool _roadsSpawned;

        protected override void OnCreate()
        {
            _cellMaterial   = CreateMaterial("Universal Render Pipeline/Lit",  "CellMaterial",   true);
            _roadMaterial   = CreateMaterial("Universal Render Pipeline/Unlit","RoadMaterial",   true, Color.yellow);
            _borderMaterial = CreateMaterial("Universal Render Pipeline/Unlit","BorderMaterial", true, Color.blue);

            if (_roadMaterial   != null) _roadMaterial.SetInt("_Cull",   (int)CullMode.Off);
            if (_borderMaterial != null) _borderMaterial.SetInt("_Cull", (int)CullMode.Off);

            RequireForUpdate<MapRenderingSettings>();
            RequireForUpdate<VoronoiCell>();
            RequireForUpdate<VoronoiEdge>();
            RequireForUpdate<CellPolygonVertex>();
            RequireForUpdate<CellTriIndex>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<MapGeneratedTag>()) return;

            var settings = SystemAPI.GetSingleton<MapRenderingSettings>();

            if (!_cellsSpawned)
            {
                BuildCellMeshes();
                _cellsSpawned = true;
            }

            if (!_roadsSpawned)
            {
                BuildRoadsAndBorders(settings);
                _roadsSpawned = true;
            }
        }

        #region Materials
        private Material CreateMaterial(string shaderName, string name, bool instancing, Color? color = null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{shaderName}' not found! Ensure URP is installed.");
                return null;
            }

            var mat = new Material(shader)
            {
                name = name,
                enableInstancing = instancing
            };

            if (color.HasValue) mat.color = color.Value;
            return mat;
        }
        #endregion

        #region Cells (local vertices + centroid position)
        private void BuildCellMeshes()
        {
            var query = GetEntityQuery(
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.Exclude<VoronoiCellMeshTag>());

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            var meshes   = new List<Mesh>();
            var cellList = new List<Entity>();

            foreach (var entity in entities)
            {
                if (!EntityManager.Exists(entity)) continue;

                var verts = EntityManager.GetBuffer<CellPolygonVertex>(entity);
                var tris  = EntityManager.GetBuffer<CellTriIndex>(entity);

                if (verts.Length < 3 || tris.Length < 3) continue;

                meshes.Add(CreateMeshFromCellLocal(entity, verts, tris));
                cellList.Add(entity);
            }

            if (cellList.Count == 0) return;

            var renderMeshArray = new RenderMeshArray(new[] { _cellMaterial }, meshes.ToArray());
            var desc = new RenderMeshDescription(ShadowCastingMode.On, true);

            for (int i = 0; i < cellList.Count; i++)
                SetupCellEntity(cellList[i], renderMeshArray, desc, i);
        }

        private Mesh CreateMeshFromCellLocal(Entity entity, DynamicBuffer<CellPolygonVertex> verts, DynamicBuffer<CellTriIndex> tris)
        {
            var cell = EntityManager.GetComponentData<VoronoiCell>(entity);
            var c = cell.Centroid;

            var mesh = new Mesh
            {
                name = $"CellMesh_{entity.Index}",
                indexFormat = IndexFormat.UInt32
            };

            var vArray = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float2 v = verts[i].Value;
                // локаль относительно центроида
                vArray[i] = new Vector3(v.x - c.x, 0f, v.y - c.y);
            }

            var tArray = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
                tArray[i] = tris[i].Value;

            mesh.SetVertices(vArray);
            mesh.SetTriangles(tArray, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private void SetupCellEntity(Entity entity, RenderMeshArray renderMeshArray, RenderMeshDescription desc, int meshIndex)
        {
            if (!EntityManager.Exists(entity)) return;

            float3 pos = EntityManager.HasComponent<CellLocalPosition>(entity)
                ? EntityManager.GetComponentData<CellLocalPosition>(entity).Value
                : new float3(EntityManager.GetComponentData<VoronoiCell>(entity).Centroid.x, 0f,
                             EntityManager.GetComponentData<VoronoiCell>(entity).Centroid.y);

            RenderMeshUtility.AddComponents(entity, EntityManager, desc, renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, meshIndex));

            if (EntityManager.HasComponent<CellBiome>(entity))
            {
                var biome = EntityManager.GetComponentData<CellBiome>(entity);
                EntityManager.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = BiomeColor(biome.Type) });
            }

            EntityManager.AddComponentData(entity, LocalTransform.FromPosition(pos));
            EntityManager.AddComponent<VoronoiCellMeshTag>(entity);
        }
        #endregion

        #region Roads & Borders (local verts + center position) - separated processed sets
        private void BuildRoadsAndBorders(MapRenderingSettings settings)
        {
            var edgeQuery = GetEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            var cellQuery = GetEntityQuery(ComponentType.ReadOnly<VoronoiCell>());

            if (edgeQuery.IsEmpty || cellQuery.IsEmpty) return;

            using var edges = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            using var cells = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);

            // разделённые множества, чтобы обработка дорог не мешала созданию бордеров
            var processedRoads = new HashSet<(int, int)>(new EdgeComparer());
            var processedBorders = new HashSet<(int, int)>(new EdgeComparer());

            if (settings.DrawRoads)
                BuildRoadsLocal(edges, cells, settings, processedRoads);

            if (settings.DrawBorders)
                BuildBordersLocal(edges, settings, processedBorders);
        }

        private void BuildRoadsLocal(NativeArray<VoronoiEdge> edges, NativeArray<VoronoiCell> cells,
                                     MapRenderingSettings settings, HashSet<(int, int)> processedRoads)
        {
            foreach (var edge in edges)
            {
                var key = EdgeKey(edge.SiteA, edge.SiteB);
                if (!processedRoads.Add(key)) continue;

                var cellA = FindCell(cells, edge.SiteA);
                var cellB = FindCell(cells, edge.SiteB);
                if (!cellA.HasValue || !cellB.HasValue) continue;

                float2 a = cellA.Value.Centroid;
                float2 b = cellB.Value.Centroid;

                // центр сегмента в МИРОВЫХ координатах (Y = 0)
                float3 center = new float3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

                // локальный меш относительно центра
                var mesh = CreateQuadMeshLocal(a, b, center, settings.RoadWidth, "RoadSegment");
                CreateRoadSegment(mesh, _roadMaterial, center);
            }
        }

        private void BuildBordersLocal(NativeArray<VoronoiEdge> edges, MapRenderingSettings settings, HashSet<(int, int)> processedBorders)
        {
            foreach (var edge in edges)
            {
                var key = EdgeKey(edge.SiteA, edge.SiteB);
                if (!processedBorders.Add(key)) continue;

                // используем реальные вершины ребра Voronoi (VertexA/B) — они в мировых координатах (float2)
                float2 vA = edge.VertexA;
                float2 vB = edge.VertexB;

                float3 center = new float3((vA.x + vB.x) * 0.5f, 0f, (vA.y + vB.y) * 0.5f);

                var mesh = CreateQuadMeshLocal(vA, vB, center, settings.EdgeWidth, "BorderSegment");
                CreateBorderSegment(mesh, _borderMaterial, center);
            }
        }

        private static VoronoiCell? FindCell(NativeArray<VoronoiCell> cells, int siteIndex)
        {
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].SiteIndex == siteIndex) return cells[i];
            return null;
        }
        #endregion

        #region Mesh helpers (local quad builder) and entity creation (atomic)
        /// <summary>
        /// Строит ПРЯМОУГОЛЬНЫЙ сегмент (дорога/бордер) в ЛОКАЛЬНЫХ координатах относительно center.
        /// Вершины лежат на Y = 0.
        /// </summary>
        private Mesh CreateQuadMeshLocal(float2 a, float2 b, float3 centerWorld, float width, string name)
        {
            float3 aW = new float3(a.x, 0f, a.y);
            float3 bW = new float3(b.x, 0f, b.y);

            float3 aL = aW - centerWorld;
            float3 bL = bW - centerWorld;

            // защитный случай: если точки совпадают
            if (math.lengthsq(bL - aL) < 1e-8f)
            {
                // degenerate small quad
                bL += new float3(0.0001f, 0f, 0f);
            }

            float3 dir = math.normalize(bL - aL);
            float3 perp = new float3(-dir.z, 0f, dir.x) * (width * 0.5f);

            var verts = new[]
            {
                new Vector3(aL.x + perp.x, 0f, aL.z + perp.z),
                new Vector3(aL.x - perp.x, 0f, aL.z - perp.z),
                new Vector3(bL.x - perp.x, 0f, bL.z - perp.z),
                new Vector3(bL.x + perp.x, 0f, bL.z + perp.z)
            };

            var tris = new[] { 0, 1, 3, 1, 2, 3 };

            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        // --- Atomic creation functions (easy to extract later) ---
        private void CreateRoadSegment(Mesh mesh, Material material, float3 worldPos)
        {
            // если ты хочешь отличать материал дороги и бордера, можно здесь настроить renderQueue / ztest
            CreateSegmentEntity(mesh, material, typeof(RoadEntityTag), worldPos);
        }

        private void CreateBorderSegment(Mesh mesh, Material material, float3 worldPos)
        {
            // бордерам можно дать более высокий renderQueue чтобы они визуально были поверх
            if (material != null) material.renderQueue = (int)RenderQueue.Geometry + 1;
            CreateSegmentEntity(mesh, material, typeof(BorderEntityTag), worldPos);
        }

        private void CreateSegmentEntity(Mesh mesh, Material material, System.Type tagType, float3 worldPos)
        {
            var array = new RenderMeshArray(new[] { material }, new[] { mesh });
            var desc  = new RenderMeshDescription(ShadowCastingMode.Off, false, MotionVectorGenerationMode.Camera);

            var entity = EntityManager.CreateEntity();

            RenderMeshUtility.AddComponents(
                entity,
                EntityManager,
                desc,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );

            EntityManager.AddComponentData(entity, LocalTransform.FromPosition(worldPos));
            EntityManager.AddComponent(entity, ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(tagType)));
        }
        #endregion

        #region Utilities
        private static (int, int) EdgeKey(int a, int b) => (math.min(a, b), math.max(a, b));

        private static float4 BiomeColor(BiomeType type) => type switch
        {
            BiomeType.Ocean     => new float4(0.1f, 0.3f, 0.8f, 1),
            BiomeType.Coast     => new float4(0.9f, 0.8f, 0.6f, 1),
            BiomeType.Ice       => new float4(0.8f, 0.9f, 1.0f, 1),
            BiomeType.Desert    => new float4(0.9f, 0.8f, 0.5f, 1),
            BiomeType.Grassland => new float4(0.3f, 0.7f, 0.2f, 1),
            BiomeType.Forest    => new float4(0.1f, 0.5f, 0.1f, 1),
            BiomeType.Mountain  => new float4(0.5f, 0.4f, 0.3f, 1),
            BiomeType.Snow      => new float4(0.95f, 0.95f, 0.95f, 1),
            _                   => new float4(1, 1, 1, 1)
        };

        private struct EdgeComparer : IEqualityComparer<(int, int)>
        {
            public bool Equals((int, int) x, (int, int) y) => x.Item1 == y.Item1 && x.Item2 == y.Item2;
            public int GetHashCode((int, int) obj) => (obj.Item1 * 397) ^ obj.Item2;
        }
        #endregion
    }
}
