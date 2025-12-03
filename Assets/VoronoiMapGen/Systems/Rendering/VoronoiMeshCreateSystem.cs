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
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VoronoiMeshCreateSystem : SystemBase
    {
        private Material _defaultMaterial;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MyVertex
        {
            public float3 Position;
            public float3 Normal;
        }

        private struct BakeData
        {
            public Entity Entity;
            public int MeshIndex;
            public LocalTransform Transform;
            public float4 Color;
        }

        protected override void OnCreate()
        {
            // Используем стандартный шейдер с отключенным кулингом и z-write
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");

            if (!shader)
            {
                // Если нет нормальных, берем Error shader чтобы было видно хоть что-то
                shader = Shader.Find("Hidden/Internal-ErrorShader");
                Debug.LogWarning("Using Fallback shader");
            }

            _defaultMaterial = new Material(shader);
            _defaultMaterial.enableInstancing = true;
            
            // ОТКЛЮЧАЕМ ОТСЕЧЕНИЕ ЗАДНИХ ГРАНЕЙ (чтобы видно было с любой стороны)
            _defaultMaterial.SetFloat("_Cull", (float)CullMode.Off); 

            if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
            {
                _defaultMaterial.SetFloat("_Smoothness", 0.0f);
                _defaultMaterial.SetFloat("_Metallic", 0.0f);
            }

            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;

            int debugMask = settings.DebugLevelMask;
            if (debugMask == 0) debugMask = -1;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>() 
                .Build();

            if (query.IsEmpty) return;

            var entities = query.ToEntityArray(Allocator.TempJob);
            
            // Вспомогательные данные
            NativeArray<VoronoiSite> sitesArr = default;
            NativeParallelHashMap<int, float2> sitePosMap = default;
            NativeList<BakeData> bakeList = default;
            Mesh.MeshDataArray mda = default;
            Mesh[] meshes = null;
            bool mdaAllocated = false;

            try
            {
                int count = math.min(entities.Length, 2000); 

                var siteQuery = SystemAPI.QueryBuilder().WithAll<VoronoiSite>().Build();
                sitesArr = siteQuery.ToComponentDataArray<VoronoiSite>(Allocator.TempJob);
                sitePosMap = new NativeParallelHashMap<int, float2>(sitesArr.Length, Allocator.TempJob);
                
                for(int i = 0; i < sitesArr.Length; i++)
                {
                    var s = sitesArr[i];
                    sitePosMap.TryAdd(s.Index, s.Position);
                }

                var biomeLookup = SystemAPI.GetComponentLookup<CellBiome>(isReadOnly: true);
                biomeLookup.Update(ref CheckedStateRef);

                mda = Mesh.AllocateWritableMeshData(count);
                mdaAllocated = true;
                meshes = new Mesh[count];
                
                bakeList = new NativeList<BakeData>(count, Allocator.TempJob);

                for (int i = 0; i < count; i++)
                {
                    var e = entities[i];
                    meshes[i] = new Mesh { name = $"Cell_{e.Index}" };
                    var md = mda[i];

                    var levelData = EntityManager.GetComponentData<DetailLevelData>(e);
                    
                    if ((debugMask & (1 << (int)levelData.Level)) == 0)
                    {
                        md.subMeshCount = 0;
                        continue; 
                    }

                    var verts = EntityManager.GetBuffer<CellPolygonVertex>(e);
                    var tris = EntityManager.GetBuffer<CellTriIndex>(e);

                    if (verts.Length < 3)
                    {
                        md.subMeshCount = 0;
                        continue;
                    }

                    float2 center = float2.zero;
                    var cell = EntityManager.GetComponentData<VoronoiCell>(e);
                    if (sitePosMap.TryGetValue(cell.SiteIndex, out float2 p)) center = p;

                    // Настройка меша (Position + Normal)
                    md.SetVertexBufferParams(verts.Length, 
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 0)
                    );
                    md.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);

                    var vb = md.GetVertexData<MyVertex>(stream: 0);
                    // Пишем данные 1 в 1 из буфера без модификаций!
                    for (int v = 0; v < verts.Length; v++)
                    {
                        vb[v] = new MyVertex 
                        {
                            Position = new float3(verts[v].Value.x - center.x, 0, verts[v].Value.z - center.y),
                            Normal = new float3(0, 1, 0)
                        };
                    }

                    var ib = md.GetIndexData<int>();
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        ib[t] = tris[t].Value;
                        ib[t + 1] = tris[t + 2].Value;
                        ib[t + 2] = tris[t + 1].Value;
                    }

                    md.subMeshCount = 1;
                    md.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), MeshUpdateFlags.DontRecalculateBounds);

                    // Цвет
                    float4 color = new float4(0.5f, 0.5f, 0.5f, 1); 
                    if (biomeLookup.HasComponent(e))
                    {
                        var biome = biomeLookup[e];
                        color = RenderUtils.GetBiomeColor(biome.Type);
                        // Тинт
                        float t = biome.Temperature * 0.1f;
                        color = math.lerp(color, new float4(color.x+t, color.y+t, color.z, 1), 0.2f);
                    }

                    // --- СУЩЕСТВЕННЫЙ ПОДЪЕМ ВЫСОТЫ (ANTI-Z-FIGHTING) ---
                    // Разносим уровни на 1 метр по высоте, чтобы L1 не перекрывал L2.
                    // При размере карты 1000 это визуально незаметно сверху, но решает проблему.
                    float yOffset = (int)levelData.Level * 1.0f; 
                    
                    bakeList.Add(new BakeData
                    {
                        Entity = e,
                        MeshIndex = i,
                        Color = color,
                        Transform = new LocalTransform 
                        { 
                            Position = new float3(center.x, yOffset, center.y), 
                            Rotation = quaternion.identity, 
                            Scale = 1f 
                        }
                    });
                }

                Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);
                mdaAllocated = false;

                // Создание дескриптора для рендера
                var rma = new RenderMeshArray(new[] { _defaultMaterial }, meshes);
                var desc = new RenderMeshDescription(ShadowCastingMode.Off, false);

                // Запись изменений
                for (int k = 0; k < bakeList.Length; k++)
                {
                    var data = bakeList[k];
                    var e = data.Entity;
                    
                    EntityManager.AddComponent<VoronoiCellMeshTag>(e);

                    meshes[data.MeshIndex].RecalculateBounds();
                    var b = meshes[data.MeshIndex].bounds;
                    b.extents += new Vector3(0, 50, 0); // Большой bounds
                    EntityManager.AddComponentData(e, new RenderBounds { Value = b.ToAABB() });

                    if (!EntityManager.HasComponent<URPMaterialPropertyBaseColor>(e))
                        EntityManager.AddComponentData(e, new URPMaterialPropertyBaseColor { Value = data.Color });
                    else
                        EntityManager.SetComponentData(e, new URPMaterialPropertyBaseColor { Value = data.Color });

                    EntityManager.SetComponentData(e, data.Transform);

                    RenderMeshUtility.AddComponents(e, EntityManager, desc, rma, 
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, data.MeshIndex));
                }

                if (bakeList.Length > 0)
                    Debug.Log($"[MeshSystem] Meshes Built: {bakeList.Length}");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                if (mdaAllocated) mda.Dispose();
            }
            finally
            {
                if (entities.IsCreated) entities.Dispose();
                if (sitesArr.IsCreated) sitesArr.Dispose();
                if (sitePosMap.IsCreated) sitePosMap.Dispose();
                if (bakeList.IsCreated) bakeList.Dispose();
            }
        }
    }
}