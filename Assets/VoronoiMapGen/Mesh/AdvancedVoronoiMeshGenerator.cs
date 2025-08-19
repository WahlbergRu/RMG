using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMeshSystem))]
    public partial class AdvancedVoronoiMeshSystem : SystemBase
    {
        private EntityQuery _cellQuery;
        private EntityQuery _edgeQuery;
        private EntityQuery _siteQuery;
        private EntityQuery _biomeQuery;
        private EntityQuery _mapGeneratedQuery;
        private bool meshesGenerated = false;

        protected override void OnCreate()
        {
            _cellQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiCell>());
            _edgeQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiEdge>());
            _siteQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiSite>());
            _biomeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Components.VoronoiCell>(),
                ComponentType.ReadOnly<Components.CellBiome>()
            );
            _mapGeneratedQuery = GetEntityQuery(ComponentType.ReadOnly<Components.MapGeneratedTag>());
        }

        protected override void OnUpdate()
        {
            if (meshesGenerated || _mapGeneratedQuery.IsEmpty)
                return;

            GenerateAdvancedMeshes();
            meshesGenerated = true;
        }

        private void GenerateAdvancedMeshes()
        {
            Debug.Log("Generating advanced Voronoi meshes...");

            // Получаем все данные
            var cells = _cellQuery.ToComponentDataArray<Components.VoronoiCell>(Allocator.TempJob);
            var sites = _siteQuery.ToComponentDataArray<Components.VoronoiSite>(Allocator.TempJob);
            var edges = _edgeQuery.ToComponentDataArray<Components.VoronoiEdge>(Allocator.TempJob);
            
            // Создаем словарь сайт -> позиция
            var sitePositions = new NativeHashMap<int, float2>(sites.Length, Allocator.TempJob);
            for (int i = 0; i < sites.Length; i++)
            {
                sitePositions.TryAdd(sites[i].Index, sites[i].Position);
            }

            // Создаем меш для каждой ячейки
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                
                if (sitePositions.TryGetValue(cell.SiteIndex, out float2 sitePosition))
                {
                    CreateAdvancedCellMesh(cell, sitePosition, edges);
                }
            }
            
            cells.Dispose();
            sites.Dispose();
            edges.Dispose();
            sitePositions.Dispose();
        }

        private void CreateAdvancedCellMesh(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            // Создаем новую сущность для меша ячейки
            var cellMeshEntity = EntityManager.CreateEntity();
            
            EntityManager.AddComponentData(cellMeshEntity, new LocalTransform
            {
                Position = new float3(sitePosition.x, sitePosition.y, 0),
                Rotation = quaternion.identity,
                Scale = 1.0f
            });

            EntityManager.AddComponentData(cellMeshEntity, new VoronoiMeshTag
            {
                CellIndex = cell.SiteIndex
            });

            // Создаем меш ячейки
            var cellMesh = CreatePolygonMeshForCell(cell, sitePosition, allEdges);
            if (cellMesh != null)
            {
                EntityManager.AddComponent(cellMeshEntity, typeof(RenderMesh));
                EntityManager.SetComponentData(cellMeshEntity, new RenderMesh
                {
                    mesh = cellMesh,
                    material = GetCellMaterial(cell.SiteIndex)
                });
            }

            // Создаем границы ячейки
            CreateCellBorder(cell, sitePosition, allEdges);
        }

        private void CreateCellBorder(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            // Пока создаем простую границу
            var borderEntity = EntityManager.CreateEntity();
            
            EntityManager.AddComponentData(borderEntity, new LocalTransform
            {
                Position = new float3(sitePosition.x, sitePosition.y, 0.01f), // Немного выше
                Rotation = quaternion.identity,
                Scale = 1.0f
            });

            var borderMesh = MeshGenerationUtility.CreateQuadMesh(1.1f, 1.1f); // Чуть больше
            if (borderMesh != null)
            {
                EntityManager.AddComponent(borderEntity, typeof(RenderMesh));
                EntityManager.SetComponentData(borderEntity, new RenderMesh
                {
                    mesh = borderMesh,
                    material = GetBorderMaterial()
                });
            }
        }

        private UnityEngine.Mesh CreatePolygonMeshForCell(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            // Собираем все вершины ячейки
            var vertices = new NativeList<float2>(32, Allocator.Temp);
            
            // Находим все ребра, связанные с этой ячейкой
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == cell.SiteIndex)
                {
                    vertices.Add(edge.VertexA);
                    vertices.Add(edge.VertexB);
                }
                else if (edge.SiteB == cell.SiteIndex)
                {
                    vertices.Add(edge.VertexA);
                    vertices.Add(edge.VertexB);
                }
            }

            // Если нет вершин, создаем простой меш
            if (vertices.Length == 0)
            {
                vertices.Dispose();
                return MeshGenerationUtility.CreateQuadMesh(1.0f, 1.0f);
            }

            // Создаем полигон из вершин (упрощенная версия)
            var mesh = CreatePolygonMesh(vertices);
            
            vertices.Dispose();
            return mesh;
        }

        private UnityEngine.Mesh CreatePolygonMesh(NativeList<float2> vertices)
        {
            // Пока создаем простой меш - позже реализуем настоящую полигональную сетку
            return MeshGenerationUtility.CreateCircleMesh(0.5f, math.min(vertices.Length, 6));
        }

        private Material GetCellMaterial(int cellIndex)
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            
            var material = new Material(shader);
            // Цвет в зависимости от индекса ячейки
            float hue = (cellIndex * 137.5f) % 360.0f / 360.0f;
            material.color = Color.HSVToRGB(hue, 0.7f, 0.9f);
            return material;
        }

        private Material GetBorderMaterial()
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            
            var material = new Material(shader);
            material.color = Color.black;
            return material;
        }
    }
}