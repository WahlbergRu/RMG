using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoronoiMapGen.Mesh
{
    public static class MeshGenerationUtility
    {
        public static UnityEngine.Mesh CreateQuadMesh(float width, float height)
        {
            var mesh = new UnityEngine.Mesh();
            
            var vertices = new Vector3[]
            {
                new Vector3(-width/2, -height/2, 0),
                new Vector3(width/2, -height/2, 0),
                new Vector3(width/2, height/2, 0),
                new Vector3(-width/2, height/2, 0)
            };

            var triangles = new int[]
            {
                0, 1, 2,
                0, 2, 3
            };

            var uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

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

        public static UnityEngine.Mesh CreateCircleMesh(float radius, int segments)
        {
            var mesh = new UnityEngine.Mesh();
            
            var vertices = new NativeArray<Vector3>(segments + 1, Allocator.Temp);
            var triangles = new NativeList<int>(segments * 3, Allocator.Temp);
            
            // Центральная вершина
            vertices[0] = Vector3.zero;
            
            // Вершины окружности
            for (int i = 0; i < segments; i++)
            {
                float angle = (2 * math.PI * i) / segments;
                vertices[i + 1] = new Vector3(
                    math.cos(angle) * radius,
                    math.sin(angle) * radius,
                    0
                );
            }
            
            // Треугольники
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments + 1;
                triangles.Add(0);      // Центр
                triangles.Add(i + 1);  // Текущая вершина
                triangles.Add(next);   // Следующая вершина
            }
            
            // UV координаты
            var uv = new NativeArray<Vector2>(vertices.Length, Allocator.Temp);
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = (2 * math.PI * i) / segments;
                uv[i + 1] = new Vector2(
                    (math.cos(angle) + 1) * 0.5f,
                    (math.sin(angle) + 1) * 0.5f
                );
            }
            
            mesh.SetVertices(vertices.ToArray());
            mesh.SetTriangles(triangles.AsArray().ToArray(), 0);
            mesh.SetUVs(0, uv.ToArray());
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            
            vertices.Dispose();
            triangles.Dispose();
            uv.Dispose();
            
            return mesh;
        }

        public static Color GetBiomeColor(Components.BiomeType biome)
        {
            switch (biome)
            {
                case Components.BiomeType.Ocean: return Color.blue;
                case Components.BiomeType.Coast: return new Color(0.5f, 0.5f, 1f);
                case Components.BiomeType.Ice: return Color.white;
                case Components.BiomeType.Desert: return new Color(1f, 0.8f, 0.2f);
                case Components.BiomeType.Grassland: return Color.green;
                case Components.BiomeType.Forest: return Color.Lerp(Color.green, Color.black, 0.3f);
                case Components.BiomeType.Mountain: return new Color(0.6f, 0.4f, 0.2f);
                case Components.BiomeType.Snow: return Color.white;
                default: return Color.gray;
            }
        }

        // Метод для создания полигонального меша из точек
        public static UnityEngine.Mesh CreatePolygonMesh(NativeArray<float2> points)
        {
            if (points.Length < 3)
                return CreateQuadMesh(1.0f, 1.0f);

            var mesh = new UnityEngine.Mesh();
            
            // Преобразуем точки в 3D вершины
            var vertices = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i] = new Vector3(points[i].x, points[i].y, 0);
            }

            // Создаем треугольники (простая триангуляция)
            var triangles = new int[(points.Length - 2) * 3];
            for (int i = 0; i < points.Length - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            // UV координаты
            var uv = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                uv[i] = new Vector2(
                    (points[i].x + 100) / 200, // Нормализация
                    (points[i].y + 100) / 200
                );
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
    }
}