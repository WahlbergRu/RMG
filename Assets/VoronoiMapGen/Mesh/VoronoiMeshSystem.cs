using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshSystem : SystemBase
    {
        private EntityQuery _cellQuery;
        private EntityQuery _edgeQuery;
        private EntityQuery _siteQuery;
        private EntityQuery _mapGeneratedQuery;
        private bool meshesGenerated = false;
        private static Material _defaultMaterial;

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

            GenerateCellMeshes();
            meshesGenerated = true;
        }

        private void GenerateCellMeshes()
        {
            Debug.Log("Generating Voronoi cell meshes...");

            var cells = _cellQuery.ToComponentDataArray<Components.VoronoiCell>(Allocator.TempJob);
            var sites = _siteQuery.ToComponentDataArray<Components.VoronoiSite>(Allocator.TempJob);
            var edges = _edgeQuery.ToComponentDataArray<Components.VoronoiEdge>(Allocator.TempJob);

            // Создаем один общий материал
            if (_defaultMaterial == null)
            {
                _defaultMaterial = GetDefaultMaterial();
            }

            for (int i = 0; i < math.min(cells.Length, 50); i++)
            {
                var cell = cells[i];
                
                float2 sitePosition = new float2(0, 0);
                for (int j = 0; j < sites.Length; j++)
                {
                    if (sites[j].Index == cell.SiteIndex)
                    {
                        sitePosition = sites[j].Position;
                        break;
                    }
                }

                CreateCellMeshEntity(cell, sitePosition, edges, _defaultMaterial);
            }
            
            cells.Dispose();
            sites.Dispose();
            edges.Dispose();
        }

        private void CreateCellMeshEntity(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges, Material material)
        {
            var meshEntity = EntityManager.CreateEntity();

            
            EntityManager.AddComponentData(meshEntity, new LocalTransform
            {
                Position = new float3(sitePosition.x, sitePosition.y, 0),
                Rotation = quaternion.identity,
                Scale = 1.0f
            });

            Debug.Log(meshEntity);
            
            var mesh = CreateCellMesh(cell, sitePosition, allEdges);
            if (mesh != null)
            {
                var renderMeshArray = new RenderMeshArray(new[] { material }, new[] { mesh });

                RenderMeshUtility.AddComponents(
                    meshEntity,
                    EntityManager,
                    new RenderMeshDescription
                    {
                        FilterSettings = RenderFilterSettings.Default
                    },
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0) 
                );
            }
        }

        private UnityEngine.Mesh CreateCellMesh(Components.VoronoiCell cell, float2 sitePosition, NativeArray<Components.VoronoiEdge> allEdges)
        {
            var cellEdges = new NativeList<Components.VoronoiEdge>(32, Allocator.Temp);
            
            for (int i = 0; i < allEdges.Length; i++)
            {
                var edge = allEdges[i];
                if (edge.SiteA == cell.SiteIndex || edge.SiteB == cell.SiteIndex)
                {
                    cellEdges.Add(edge);
                }
            }

            if (cellEdges.Length == 0)
            {
                cellEdges.Dispose();
                return MeshGenerationUtility.CreateQuadMesh(1.0f, 1.0f);
            }

            var mesh = MeshGenerationUtility.CreateQuadMesh(1.0f, 1.0f);
            
            cellEdges.Dispose();
            return mesh;
        }

        private Material GetDefaultMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            
            var material = new Material(shader)
            {
                color = new Color(
                    UnityEngine.Random.Range(0.3f, 0.8f),
                    UnityEngine.Random.Range(0.3f, 0.8f),
                    UnityEngine.Random.Range(0.3f, 0.8f)
                )
            };
            return material;
        }
    }
}