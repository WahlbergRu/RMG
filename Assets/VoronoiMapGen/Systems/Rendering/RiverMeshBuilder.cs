using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems.Rendering
{
    public static class RiverMeshBuilder
    {
        public static void Build(EntityManager em, Material material, MapSettings settings)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<VoronoiCell>(),
                ComponentType.ReadOnly<HydrologyData>(),
                ComponentType.ReadOnly<DetailLevelData>()
            );

            if (query.IsEmpty) return;

            var entities = query.ToEntityArray(Allocator.Temp);
            var cells = query.ToComponentDataArray<VoronoiCell>(Allocator.Temp);
            var hydro = query.ToComponentDataArray<HydrologyData>(Allocator.Temp);

            var sitePosMap = new NativeParallelHashMap<int, float3>(cells.Length, Allocator.Temp);
            for (int i = 0; i < cells.Length; i++)
            {
                float3 pos = new float3(cells[i].Centroid.x, 0, cells[i].Centroid.y);
                sitePosMap.TryAdd(cells[i].SiteIndex, pos);
            }

            var meshes = new System.Collections.Generic.List<Mesh>();
            var riverEntities = new System.Collections.Generic.List<Entity>();
            
            int riverCount = 0;
            float maxDrawDistSq = 120.0f * 120.0f; 

            for (int i = 0; i < entities.Length; i++)
            {
                var h = hydro[i];
                
                // Рисуем, если помечено как река
                if (h.IsRiver && h.FlowTargetIndex != -1)
                {
                    float3 startPos = new float3(cells[i].Centroid.x, 0, cells[i].Centroid.y);
                    
                    if (sitePosMap.TryGetValue(h.FlowTargetIndex, out float3 endPos))
                    {
                        // Проверка дистанции (защита от лазеров)
                        if (math.distancesq(startPos, endPos) > maxDrawDistSq) continue;

                        // --- ИЗМЕНЕНИЕ: Плавная ширина ---
                        // Минимальная ширина 0.3 (ручей), Максимальная 5.0 (Амазонка)
                        // Используем логарифм или корень, чтобы ширина не росла бесконечно
                        float width = math.clamp(math.sqrt(h.Flux) * 0.5f, 0.3f, 5.0f);
                        
                        // Поднимаем над землей чуть выше
                        float yOffset = 0.6f + (riverCount % 3) * 0.01f; 
                        startPos.y = yOffset;
                        endPos.y = yOffset;

                        Mesh mesh = CreateRiverSegment(startPos, endPos, width);
                        meshes.Add(mesh);
                        
                        var e = em.CreateEntity();
                        em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
                        em.AddComponentData(e, new WorldRenderBounds { Value = mesh.bounds.ToAABB() });
                        riverEntities.Add(e);
                        
                        riverCount++;
                    }
                }
            }

            if (meshes.Count > 0)
            {
                var rma = new RenderMeshArray(new[] { material }, meshes.ToArray());
                var desc = new RenderMeshDescription(ShadowCastingMode.Off, false);

                for (int i = 0; i < riverEntities.Count; i++)
                {
                    RenderMeshUtility.AddComponents(riverEntities[i], em, desc, rma, 
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, i));
                    // Ярко-голубой цвет
                    em.AddComponentData(riverEntities[i], new URPMaterialPropertyBaseColor { Value = new float4(0.0f, 0.5f, 1.0f, 1) });
                }
            }

            entities.Dispose();
            cells.Dispose();
            hydro.Dispose();
            sitePosMap.Dispose();
            
            Debug.Log($"[RiverBuilder] Built {riverCount} river segments.");
        }

        private static Mesh CreateRiverSegment(float3 start, float3 end, float width)
        {
            Vector3[] verts = new Vector3[4];
            int[] tris = new int[6];
            Vector2[] uvs = new Vector2[4];

            float3 dir = math.normalize(end - start);
            float3 right = math.cross(dir, new float3(0, 1, 0)) * width * 0.5f;

            // Небольшой нахлест, чтобы не было дыр на стыках
            float3 overlap = dir * width * 0.4f; 

            verts[0] = start - right - overlap; 
            verts[1] = start + right - overlap; 
            verts[2] = end - right + overlap;   
            verts[3] = end + right + overlap;   

            tris[0] = 0; tris[1] = 1; tris[2] = 2;
            tris[3] = 2; tris[4] = 1; tris[5] = 3;
            
            uvs[0] = new Vector2(0, 0);
            uvs[1] = new Vector2(1, 0);
            uvs[2] = new Vector2(0, 1);
            uvs[3] = new Vector2(1, 1);

            Mesh m = new Mesh();
            m.vertices = verts;
            m.triangles = tris;
            m.uv = uvs;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}