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
        private bool _hasSpawnedCells;
        private bool _hasSpawnedRoads;

        protected override void OnCreate()
        {
            // Материал для ячеек (биомы)
            var cellShader = Shader.Find("Universal Render Pipeline/Lit");
            if (cellShader == null)
            {
                Debug.LogError("URP Lit shader not found! Ensure URP is installed.");
            }
            else
            {
                _cellMaterial = new Material(cellShader)
                {
                    name = "CellMaterial",
                    enableInstancing = true
                };
            }

            // Материалы для дорог и границ
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                Debug.LogError("URP Unlit shader not found! Ensure URP is installed.");
                return;
            }

            _roadMaterial = new Material(unlitShader)
            {
                name = "RoadMaterial",
                color = Color.yellow,
                enableInstancing = true
            };
            _roadMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            _borderMaterial = new Material(unlitShader)
            {
                name = "BorderMaterial",
                color = Color.blue,
                enableInstancing = true
            };
            _borderMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            // Требуем данные для всех этапов
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
            var mapSize = settings.MapSize;
            var entityManager = EntityManager;

            // ===== ЭТАП 1: СОЗДАЁМ МЕШИ ЯЧЕЕК =====
            if (!_hasSpawnedCells)
            {
                CreateCellMeshes();
                _hasSpawnedCells = true;
            }

            // ===== ЭТАП 2: СОЗДАЁМ ДОРОГИ И ГРАНИЦЫ =====
            if (!_hasSpawnedRoads)
            {
                CreateRoadsAndBorders(settings, mapSize);
                _hasSpawnedRoads = true;
            }
        }
        
        private void CreateCellMeshes()
        {
            var cellQuery = GetEntityQuery(
                ComponentType.ReadOnly<CellPolygonVertex>(),
                ComponentType.ReadOnly<CellTriIndex>(),
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.Exclude<VoronoiCellMeshTag>());

            using var entities = cellQuery.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            var validEntities = new List<Entity>();
            var validMeshes = new List<Mesh>();

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityManager.Exists(entity)) continue;

                var vertices = EntityManager.GetBuffer<CellPolygonVertex>(entity);
                var triangles = EntityManager.GetBuffer<CellTriIndex>(entity);

                if (vertices.Length < 3 || triangles.Length < 3) continue;

                // Создаём меш
                var mesh = new Mesh
                {
                    name = $"CellMesh_{entity.Index}",
                    indexFormat = IndexFormat.UInt32
                };

                // Вершины
                var meshVertices = new Vector3[vertices.Length];
                for (int j = 0; j < vertices.Length; j++)
                {
                    float2 v = vertices[j].Value;
                    meshVertices[j] = new Vector3(v.x, 0f, v.y);
                }

                // Индексы
                var meshIndices = new int[triangles.Length];
                for (int j = 0; j < triangles.Length; j++)
                {
                    meshIndices[j] = triangles[j].Value;
                }

                mesh.SetVertices(meshVertices);
                mesh.SetTriangles(meshIndices, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();

                // Добавляем в список
                validEntities.Add(entity);
                validMeshes.Add(mesh);
            }

            if (validEntities.Count == 0) return;

            // Настраиваем рендер
            var renderMeshArray = new RenderMeshArray(new[] { _cellMaterial }, validMeshes.ToArray());
            var renderMeshDesc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.On,
                receiveShadows: true
            );

            // Добавляем компоненты
            for (int i = 0; i < validEntities.Count; i++)
            {
                Entity entity = validEntities[i];
                if (!EntityManager.Exists(entity)) continue;

                float3 pos = float3.zero;
                if (EntityManager.HasComponent<CellLocalPosition>(entity))
                {
                    pos = EntityManager.GetComponentData<CellLocalPosition>(entity).Value;
                }
                else
                {
                    var centroid = EntityManager.GetComponentData<VoronoiCell>(entity).Centroid;
                    pos = new float3(centroid.x, 0f, centroid.y);
                }

                RenderMeshUtility.AddComponents(
                    entity,
                    EntityManager,
                    renderMeshDesc,
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, i)
                );

                // Устанавливаем цвет биома
                if (EntityManager.HasComponent<CellBiome>(entity))
                {
                    var biome = EntityManager.GetComponentData<CellBiome>(entity);
                    float4 color = GetBiomeColor(biome.Type);
                    EntityManager.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = color });
                }

                // 🔑 ИСПРАВЛЕНИЕ: Используем AddComponentData вместо SetComponentData
                EntityManager.AddComponentData(entity, new LocalTransform
                {
                    Position = pos,
                    Rotation = quaternion.identity,
                    Scale = 1f
                });
                
                EntityManager.AddComponent<VoronoiCellMeshTag>(entity);
            }
        }

        private void CreateRoadsAndBorders(MapRenderingSettings settings, float2 mapSize)
        {
            var edgeQuery = GetEntityQuery(ComponentType.ReadOnly<VoronoiEdge>());
            var cellQuery = GetEntityQuery(ComponentType.ReadOnly<VoronoiCell>());

            if (edgeQuery.CalculateEntityCount() == 0 || cellQuery.CalculateEntityCount() == 0)
                return;

            using var edgeComponents = edgeQuery.ToComponentDataArray<VoronoiEdge>(Allocator.Temp);
            using var cellComponents = cellQuery.ToComponentDataArray<VoronoiCell>(Allocator.Temp);

            // ✅ ИСПРАВЛЕНИЕ: Собственная реализация хеша
            var processedEdges = new HashSet<(int, int)>(edgeComponents.Length * 2, new EdgeComparer());

            // ===== СОЗДАЁМ ДОРОГИ =====
            if (settings.DrawRoads)
            {
                foreach (var edge in edgeComponents)
                {
                    var siteA = edge.SiteA;
                    var siteB = edge.SiteB;

                    var edgeKey = (math.min(siteA, siteB), math.max(siteA, siteB));
                    if (processedEdges.Contains(edgeKey)) continue;
                    processedEdges.Add(edgeKey);

                    VoronoiCell? cellA = null;
                    VoronoiCell? cellB = null;
                    for (int i = 0; i < cellComponents.Length; i++)
                    {
                        if (cellComponents[i].SiteIndex == siteA) cellA = cellComponents[i];
                        if (cellComponents[i].SiteIndex == siteB) cellB = cellComponents[i];
                        if (cellA.HasValue && cellB.HasValue) break;
                    }

                    if (!cellA.HasValue || !cellB.HasValue) continue;

                    var roadMesh = CreateRoadMesh(cellA.Value.Centroid, cellB.Value.Centroid, settings.RoadWidth);
                    CreateRenderEntity(roadMesh, _roadMaterial, typeof(RoadEntityTag));
                }
            }

            // ===== СОЗДАЁМ ГРАНИЦЫ =====
            if (settings.DrawBorders)
            {
                foreach (var edge in edgeComponents)
                {
                    var siteA = edge.SiteA;
                    var siteB = edge.SiteB;

                    var edgeKey = (math.min(siteA, siteB), math.max(siteA, siteB));
                    if (processedEdges.Contains(edgeKey)) continue;
                    processedEdges.Add(edgeKey);

                    var borderMesh = CreateBorderMesh(edge.VertexA, edge.VertexB, settings.EdgeWidth);
                    CreateRenderEntity(borderMesh, _borderMaterial, typeof(BorderEntityTag));
                }
            }
        }

        // ===== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =====
        // ✅ ИСПРАВЛЕНИЕ: Возвращаем float4 вместо Color
        private float4 GetBiomeColor(BiomeType biomeType)
        {
            switch (biomeType)
            {
                case BiomeType.Ocean: return new float4(0.1f, 0.3f, 0.8f, 1.0f);
                case BiomeType.Coast: return new float4(0.9f, 0.8f, 0.6f, 1.0f);
                case BiomeType.Ice: return new float4(0.8f, 0.9f, 1.0f, 1.0f);
                case BiomeType.Desert: return new float4(0.9f, 0.8f, 0.5f, 1.0f);
                case BiomeType.Grassland: return new float4(0.3f, 0.7f, 0.2f, 1.0f);
                case BiomeType.Forest: return new float4(0.1f, 0.5f, 0.1f, 1.0f);
                case BiomeType.Mountain: return new float4(0.5f, 0.4f, 0.3f, 1.0f);
                case BiomeType.Snow: return new float4(0.95f, 0.95f, 0.95f, 1.0f);
                default: return new float4(1.0f, 1.0f, 1.0f, 1.0f);
            }
        }

        private Mesh CreateRoadMesh(float2 centroidA, float2 centroidB, float roadWidth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Без умножения на mapSize!
            var scaledA = centroidA;
            var scaledB = centroidB;

            var dir = math.normalize(scaledB - scaledA);
            var perp = new float2(-dir.y, dir.x);
            var halfWidth = roadWidth * 0.5f;

            var leftA = scaledA + perp * halfWidth;
            var leftB = scaledB + perp * halfWidth;
            var rightA = scaledA - perp * halfWidth;
            var rightB = scaledB - perp * halfWidth;

            vertices.Add(new Vector3(leftA.x, 0.01f, leftA.y));
            vertices.Add(new Vector3(rightA.x, 0.01f, rightA.y));
            vertices.Add(new Vector3(rightB.x, 0.01f, rightB.y));
            vertices.Add(new Vector3(leftB.x, 0.01f, leftB.y));

            triangles.Add(0); triangles.Add(1); triangles.Add(3);
            triangles.Add(1); triangles.Add(2); triangles.Add(3);

            var mesh = new Mesh
            {
                name = "RoadSegment",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private Mesh CreateBorderMesh(float2 vertexA, float2 vertexB, float edgeWidth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            var scaledA = vertexA;
            var scaledB = vertexB;

            var dir = math.normalize(scaledB - scaledA);
            var perp = new float2(-dir.y, dir.x);
            var halfWidth = edgeWidth * 0.5f;

            var leftA = scaledA + perp * halfWidth;
            var leftB = scaledB + perp * halfWidth;
            var rightA = scaledA - perp * halfWidth;
            var rightB = scaledB - perp * halfWidth;

            vertices.Add(new Vector3(leftA.x, 0.01f, leftA.y));
            vertices.Add(new Vector3(rightA.x, 0.01f, rightA.y));
            vertices.Add(new Vector3(rightB.x, 0.01f, rightB.y));
            vertices.Add(new Vector3(leftB.x, 0.01f, leftB.y));

            triangles.Add(0); triangles.Add(1); triangles.Add(3);
            triangles.Add(1); triangles.Add(2); triangles.Add(3);

            var mesh = new Mesh
            {
                name = "BorderSegment",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }


        private void CreateRenderEntity(Mesh mesh, Material material, System.Type tagType)
        {
            var renderMeshArray = new RenderMeshArray(new[] { material }, new[] { mesh });
            var desc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.Off,
                receiveShadows: false,
                motionVectorGenerationMode: MotionVectorGenerationMode.Camera
            );

            var entity = EntityManager.CreateEntity();
            
            RenderMeshUtility.AddComponents(
                entity,
                EntityManager,
                desc,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );
            
            EntityManager.AddComponentData(entity, new LocalTransform
            {
                Position = float3.zero,
                Rotation = quaternion.identity,
                Scale = 1f
            });
            
            EntityManager.AddComponent(entity, ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(tagType)));
        }

        // ✅ ИСПРАВЛЕНИЕ: Собственная реализация хеш-функции
        private struct EdgeComparer : IEqualityComparer<(int, int)>
        {
            public bool Equals((int, int) x, (int, int) y) => x.Item1 == y.Item1 && x.Item2 == y.Item2;
            
            public int GetHashCode((int, int) obj)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + obj.Item1;
                    hash = hash * 23 + obj.Item2;
                    return hash;
                }
            }
        }
    }
}