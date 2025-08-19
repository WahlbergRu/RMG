using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMapGen.Systems.MapGenerationSystem))]
    public partial class VoronoiMeshSystem : SystemBase
    {
        private EntityQuery _cellQuery;
        private EntityQuery _edgeQuery;
        private EntityQuery _siteQuery;
        private EntityQuery _mapGeneratedQuery;
        private bool meshesGenerated = false;

        protected override void OnCreate()
        {
            _cellQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiCell>());
            _edgeQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiEdge>());
            _siteQuery = GetEntityQuery(ComponentType.ReadOnly<Components.VoronoiSite>());
            _mapGeneratedQuery = GetEntityQuery(ComponentType.ReadOnly<Components.MapGeneratedTag>());
        }

        protected override void OnUpdate()
        {
            if (meshesGenerated || _mapGeneratedQuery.IsEmpty)
                return;

            // Ждем завершения генерации карты
            GenerateCellMeshes();
            meshesGenerated = true;
        }

        private void GenerateCellMeshes()
        {
            Debug.Log("Generating Voronoi cell meshes...");

            // Получаем все данные
            var cells = _cellQuery.ToComponentDataArray<Components.VoronoiCell>(Allocator.TempJob);
            var sites = _siteQuery.ToComponentDataArray<Components.VoronoiSite>(Allocator.TempJob);
            var edges = _edgeQuery.ToComponentDataArray<Components.VoronoiEdge>(Allocator.TempJob);

            // Создаем меш для каждой ячейки
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                
                // Находим сайт для этой ячейки
                float2 sitePosition = new float2(0, 0);
                for (int j = 0; j < sites.Length; j++)
                {
                    if (sites[j].Index == cell.SiteIndex)
                    {
                        sitePosition = sites[j].Position;
                        break;
                    }
                }

                CreateCellMeshEntity(cell, sitePosition, edges);
            }
            
            cells.Dispose();
            sites.Dispose();
            edges.Dispose();
        }

        private void CreateCellMeshEntity(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            // Создаем новую сущность для меша
            var meshEntity = EntityManager.CreateEntity();
            
            // Добавляем компоненты трансформации
            EntityManager.AddComponentData(meshEntity, new LocalTransform
            {
                Position = new float3(sitePosition.x, sitePosition.y, 0),
                Rotation = quaternion.identity,
                Scale = 1.0f
            });

            // Добавляем тег меша
            EntityManager.AddComponentData(meshEntity, new VoronoiMeshTag
            {
                CellIndex = cell.SiteIndex
            });

            // Создаем и добавляем меш через BlobAssetReference (новый способ)
            var mesh = CreateCellMesh(cell, sitePosition, allEdges);
            if (mesh != null)
            {
                // Создаем RenderMeshArray для нового API
                var meshArray = new RenderMeshArray(new[] { mesh }, new[] { GetDefaultMaterial() });
                
                EntityManager.AddComponentData(meshEntity, new RenderMeshArrayComponent
                {
                    RenderMeshArray = meshArray,
                    MaterialIndices = new int[] { 0 },
                    MeshIndices = new int[] { 0 }
                });
            }
        }

        private UnityEngine.Mesh CreateCellMesh(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            // Собираем ребра, принадлежащие этой ячейке
            var cellEdges = new NativeList<Components.VoronoiEdge>(32, Allocator.Temp);
            
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == cell.SiteIndex || edge.SiteB == cell.SiteIndex)
                {
                    cellEdges.Add(edge);
                }
            }

            // Если нет ребер, создаем простой квадрат
            if (cellEdges.Length == 0)
            {
                cellEdges.Dispose();
                return MeshGenerationUtility.CreateQuadMesh(1.0f, 1.0f);
            }

            // Создаем меш из ребер (пока упрощенная версия)
            var mesh = MeshGenerationUtility.CreateQuadMesh(1.0f, 1.0f);
            
            cellEdges.Dispose();
            return mesh;
        }

        private Material GetDefaultMaterial()
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            
            var material = new Material(shader);
            material.color = new Color(
                UnityEngine.Random.Range(0.3f, 0.8f),
                UnityEngine.Random.Range(0.3f, 0.8f),
                UnityEngine.Random.Range(0.3f, 0.8f)
            );
            return material;
        }
    }

    // Компонент для нового RenderMesh API
    public struct RenderMeshArrayComponent : IComponentData
    {
        public RenderMeshArray RenderMeshArray;
        
        public int[] MaterialIndices;
        public int[] MeshIndices;
    }
}