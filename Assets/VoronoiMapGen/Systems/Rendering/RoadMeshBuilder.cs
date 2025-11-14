using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Rendering;
namespace VoronoiMapGen.Systems.Rendering
{
    public static class RoadMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            if (!settings.DrawRoads) return;
            Debug.Log("[Roads] Starting road network generation...");
            // 1. Собираем все города (L2)
            var cityEntities = GetCityEntities(em);
            if (cityEntities.Length == 0)
            {
                Debug.LogWarning("[Roads] No cities found for road generation");
                return;
            }
            Debug.Log($"[Roads] Found {cityEntities.Length} cities for road network");
            // 2. Строим сеть дорог между городами
            BuildRoadNetwork(em, material, settings, cityEntities);
            cityEntities.Dispose();
        }
        private static NativeArray<Entity> GetCityEntities(EntityManager em)
        {
            EntityQuery cityQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<DetailLevelData>(),
                ComponentType.ReadOnly<VoronoiCellMeshTag>()
            );
            using var entities = cityQuery.ToEntityArray(Allocator.Temp);
            var cityEntities = new NativeList<Entity>(Allocator.Temp);
            foreach (var entity in entities)
            {
                var levelData = em.GetComponentData<DetailLevelData>(entity);
                if (levelData.Level == DetailLevel.Settlement) // L2 - города
                {
                    cityEntities.Add(entity);
                }
            }
            return cityEntities.ToArray(Allocator.TempJob);
        }
        private static void BuildRoadNetwork(EntityManager em, Material material, 
            MapSettings settings, NativeArray<Entity> cityEntities)
        {
            // Создаем граф для алгоритма минимального остовного дерева
            var graph = new NativeList<RoadConnection>(Allocator.Temp);
            // 1. Собираем все возможные соединения между городами
            for (int i = 0; i < cityEntities.Length; i++)
            {
                var cityA = em.GetComponentData<VoronoiCell>(cityEntities[i]);
                var posA = new float3(cityA.Centroid.x, 0, cityA.Centroid.y);
                for (int j = i + 1; j < cityEntities.Length; j++)
                {
                    var cityB = em.GetComponentData<VoronoiCell>(cityEntities[j]);
                    var posB = new float3(cityB.Centroid.x, 0, cityB.Centroid.y);
                    float distance = math.distance(posA, posB);
                    // Добавляем соединение в граф
                    graph.Add(new RoadConnection {
                        CityA = cityEntities[i],
                        CityB = cityEntities[j],
                        Distance = distance
                    });
                }
            }
            // 2. Сортируем по расстоянию (для Kruskal) - ИСПРАВЛЕНО
            graph.Sort(new RoadConnectionComparer());
            
            // 3. Строим минимальное остовное дерево
            var mst = new NativeList<RoadConnection>(Allocator.Temp);
            var uf = new UnionFind(cityEntities.Length);
            foreach (var connection in graph)
            {
                int indexA = GetCityIndex(cityEntities, connection.CityA);
                int indexB = GetCityIndex(cityEntities, connection.CityB);
                if (uf.Find(indexA) != uf.Find(indexB))
                {
                    uf.Union(indexA, indexB);
                    mst.Add(connection);
                    // Добавляем дорогу
                    CreateRoadSegment(em, material, settings, connection);
                }
            }
            // 4. Добавляем дополнительные дороги для связности (5-10% от MST)
            AddSecondaryRoads(em, material, settings, graph, mst, cityEntities);
            Debug.Log($"[Roads] Generated {mst.Length} primary roads and {graph.Length - mst.Length} secondary roads");
            graph.Dispose();
            mst.Dispose();
            uf.Dispose();
        }
        
        // ДОБАВЛЕН КОМПАРАТОР
        private struct RoadConnectionComparer : IComparer<RoadConnection>
        {
            public int Compare(RoadConnection x, RoadConnection y)
            {
                return x.Distance.CompareTo(y.Distance);
            }
        }
        
        private static void CreateRoadSegment(EntityManager em, Material material, 
            MapSettings settings, RoadConnection connection)
        {
            var cityA = em.GetComponentData<VoronoiCell>(connection.CityA);
            var cityB = em.GetComponentData<VoronoiCell>(connection.CityB);
            float3 posA = new float3(cityA.Centroid.x, GetRoadHeight(em, cityA), cityA.Centroid.y);
            float3 posB = new float3(cityB.Centroid.x, GetRoadHeight(em, cityB), cityB.Centroid.y);
            float3 center = (posA + posB) * 0.5f;
            // Создаем меш дороги
            Mesh roadMesh = CreateRoadMesh(posA, posB, center, settings.RoadWidth);
            // Создаем сущность для дороги
            Entity roadEntity = em.CreateEntity();
            RenderMeshArray renderMeshArray = new RenderMeshArray(new[] { material }, new[] { roadMesh });
            RenderMeshDescription desc = new RenderMeshDescription(ShadowCastingMode.On, false);
            RenderMeshUtility.AddComponents(roadEntity, em, desc, renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            em.AddComponentData(roadEntity, new LocalTransform { 
                Position = center,
                Rotation = quaternion.identity,
                Scale = 1.0f
            });
            em.AddComponent<LocalToWorld>(roadEntity);
            em.AddComponent<RoadEntityTag>(roadEntity);
            // Добавляем информацию о дороге
            em.AddComponentData(roadEntity, new RoadData {
                StartCity = connection.CityA,
                EndCity = connection.CityB,
                Length = connection.Distance,
                Type = RoadType.Primary
            });
        }
        private static Mesh CreateRoadMesh(float3 start, float3 end, float3 center, float width)
        {
            // Преобразуем в локальное пространство
            float3 startLocal = start - center;
            float3 endLocal = end - center;
            if (math.lengthsq(endLocal - startLocal) < 1e-8f)
            {
                endLocal += new float3(0.0001f, 0f, 0f);
            }
            float3 direction = math.normalize(endLocal - startLocal);
            float3 perpendicular = new float3(-direction.z, 0f, direction.x) * (width * 0.5f);
            // Создаем 4 вершины для прямоугольника
            Vector3[] vertices = new Vector3[4] {
                startLocal + perpendicular,
                startLocal - perpendicular,
                endLocal - perpendicular,
                endLocal + perpendicular
            };
            // Индексы для двух треугольников
            int[] triangles = new int[6] {
                0, 1, 3,  // первый треугольник
                1, 2, 3   // второй треугольник
            };
            Mesh mesh = new Mesh {
                name = "RoadSegment",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }
        private static float GetRoadHeight(EntityManager em, VoronoiCell cell)
        {
            // Высота дороги = средняя высота двух городов + небольшой подъем
            if (em.HasComponent<FinalHeightData>(em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>()).GetSingletonEntity()))
            {
                return em.GetComponentData<FinalHeightData>(em.CreateEntityQuery(ComponentType.ReadOnly<VoronoiCell>()).GetSingletonEntity()).FinalElevation + 1.0f;
            }
            return 1.0f; // Небольшой подъем над землей
        }
        private static void AddSecondaryRoads(EntityManager em, Material material, 
            MapSettings settings, NativeList<RoadConnection> allConnections, 
            NativeList<RoadConnection> mst, NativeArray<Entity> cityEntities)
        {
            // Добавляем 10-15% дополнительных дорог для связности
            int secondaryRoadCount = math.min(
                (int)(allConnections.Length * 0.15f),
                allConnections.Length - mst.Length
            );
            // Выбираем самые короткие оставшиеся соединения
            for (int i = 0, added = 0; i < allConnections.Length && added < secondaryRoadCount; i++)
            {
                var connection = allConnections[i];
                // Проверяем, есть ли уже дорога в MST
                bool existsInMST = false;
                foreach (var mstConn in mst)
                {
                    if ((mstConn.CityA == connection.CityA && mstConn.CityB == connection.CityB) ||
                        (mstConn.CityA == connection.CityB && mstConn.CityB == connection.CityA))
                    {
                        existsInMST = true;
                        break;
                    }
                }
                if (!existsInMST)
                {
                    CreateRoadSegment(em, material, settings, connection);
                    added++;
                }
            }
        }
        private static int GetCityIndex(NativeArray<Entity> cities, Entity city)
        {
            for (int i = 0; i < cities.Length; i++)
            {
                if (cities[i] == city) return i;
            }
            return -1;
        }
        // Вспомогательные структуры
        private struct RoadConnection
        {
            public Entity CityA;
            public Entity CityB;
            public float Distance;
        }
        private struct UnionFind
        {
            private NativeArray<int> parent;
            private NativeArray<int> rank;
            public UnionFind(int size)
            {
                parent = new NativeArray<int>(size, Allocator.Temp);
                rank = new NativeArray<int>(size, Allocator.Temp);
                for (int i = 0; i < size; i++)
                {
                    parent[i] = i;
                    rank[i] = 0;
                }
            }
            public int Find(int x)
            {
                if (parent[x] != x)
                {
                    parent[x] = Find(parent[x]);
                }
                return parent[x];
            }
            public void Union(int x, int y)
            {
                int rootX = Find(x);
                int rootY = Find(y);
                if (rootX == rootY) return;
                if (rank[rootX] < rank[rootY])
                {
                    parent[rootX] = rootY;
                }
                else if (rank[rootX] > rank[rootY])
                {
                    parent[rootY] = rootX;
                }
                else
                {
                    parent[rootY] = rootX;
                    rank[rootX]++;
                }
            }
            public void Dispose()
            {
                parent.Dispose();
                rank.Dispose();
            }
        }
        // Новые компоненты для дорог
        public struct RoadData : IComponentData
        {
            public Entity StartCity;
            public Entity EndCity;
            public float Length;
            public RoadType Type;
        }
        public enum RoadType
        {
            Primary,    // Основные дороги между городами
            Secondary,  // Дополнительные дороги
            Bridge,     // Мосты через реки
            Tunnel      // Тоннели через горы
        }
    }
}