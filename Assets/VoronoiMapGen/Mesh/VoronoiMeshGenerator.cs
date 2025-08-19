using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    public class VoronoiMeshGenerator : MonoBehaviour
    {
        [Header("Mesh Settings")]
        public Material cellMaterial;
        public float cellHeight = 0.1f;
        public bool generateMeshOnStart = true;

        private EntityManager entityManager;
        private EntityQuery cellQuery;
        private bool isInitialized = false;

        void Start()
        {
            if (generateMeshOnStart)
            {
                GenerateMeshes();
            }
        }

        public void GenerateMeshes()
        {
            Initialize();
            if (!isInitialized) return;

            // Получаем все ячейки
            var cells = cellQuery.ToComponentDataArray<Components.VoronoiCell>(Allocator.TempJob);
            var sites = GetSitesForCells(cells.Length);
            var biomes = GetBiomesForCells(cells.Length);

            // Генерируем меш для каждой ячейки
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                var site = sites.IsCreated ? (i < sites.Length ? sites[i] : new float2(0, 0)) : new float2(0, 0);
                var biome = biomes.IsCreated ? (i < biomes.Length ? biomes[i] : new Components.CellBiome()) : new Components.CellBiome();
                
                GenerateCellMesh(cell, site, biome);
            }

            cells.Dispose();
            if (sites.IsCreated) sites.Dispose();
            if (biomes.IsCreated) biomes.Dispose();
        }

        void Initialize()
        {
            if (isInitialized) return;
            
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                entityManager = world.EntityManager;
                if (!Equals(entityManager, default(EntityManager)))
                {
                    cellQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Components.VoronoiCell>());
                    isInitialized = true;
                }
            }
        }

        private NativeArray<float2> GetSitesForCells(int cellCount)
        {
            var siteQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Components.VoronoiSite>());
            if (siteQuery.CalculateEntityCount() > 0)
            {
                return siteQuery.ToComponentDataArray<Components.VoronoiSite>(Allocator.TempJob)
                    .Reinterpret<float2>();
            }
            return new NativeArray<float2>(0, Allocator.TempJob);
        }

        private NativeArray<Components.CellBiome> GetBiomesForCells(int cellCount)
        {
            var biomeQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Components.VoronoiCell>(),
                ComponentType.ReadOnly<Components.CellBiome>()
            );
            if (biomeQuery.CalculateEntityCount() > 0)
            {
                return biomeQuery.ToComponentDataArray<Components.CellBiome>(Allocator.TempJob);
            }
            return new NativeArray<Components.CellBiome>(0, Allocator.TempJob);
        }

        private void GenerateCellMesh(Components.VoronoiCell cell, float2 site, Components.CellBiome biome)
        {
            // Создаем меш для ячейки
            var mesh = CreateVoronoiCellMesh(cell, site);
            if (mesh == null) return;

            // Создаем сущность для меша
            var meshEntity = entityManager.CreateEntity();
            
            // Добавляем компоненты для рендеринга
            entityManager.AddComponentData(meshEntity, new LocalToWorld());
            entityManager.AddComponentData(meshEntity, new LocalTransform
            {
                Position = new float3(site.x, site.y, 0),
                Rotation = quaternion.identity,
                Scale = 1.0f
            });

            // Добавляем рендер меш
            if (cellMaterial != null)
            {
                entityManager.AddSharedComponent(meshEntity, new RenderMesh
                {
                    mesh = mesh,
                    material = cellMaterial
                });
            }
        }

        private UnityEngine.Mesh CreateVoronoiCellMesh(Components.VoronoiCell cell, float2 site)
        {
            // Пока создаем простой квадрат для тестирования
            return CreateQuadMesh(1.0f, cellHeight);
        }

        private UnityEngine.Mesh CreateQuadMesh(float size, float height)
        {
            var mesh = new UnityEngine.Mesh();
            
            // Вершины квадрата
            var vertices = new Vector3[]
            {
                new Vector3(-size/2, -size/2, 0),
                new Vector3(size/2, -size/2, 0),
                new Vector3(size/2, size/2, 0),
                new Vector3(-size/2, size/2, 0)
            };

            // Треугольники
            var triangles = new int[]
            {
                0, 1, 2,
                0, 2, 3
            };

            // UV координаты
            var uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            // Нормали
            var normals = new Vector3[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.normals = normals;
            
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            
            return mesh;
        }
    }
}