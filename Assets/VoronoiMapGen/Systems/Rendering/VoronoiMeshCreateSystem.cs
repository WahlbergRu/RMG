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

        protected override void OnCreate()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            
            if (shader == null) 
            {
                Debug.LogError("CRITICAL: URP Shaders not found.");
                return;
            }

            _defaultMaterial = new Material(shader);
            _defaultMaterial.enableInstancing = true; 
            _defaultMaterial.SetFloat("_Smoothness", 0.1f); 
            _defaultMaterial.SetFloat("_Metallic", 0.0f);   

            RequireForUpdate<GeometryBuiltTag>();
            RequireForUpdate<MapGeneratedTag>();
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<MapSettings>(out var settings)) return;
            if (_defaultMaterial == null) return; 

            int debugMask = settings.DebugLevelMask;
            if (debugMask == 0) debugMask = -1;

            var query = SystemAPI.QueryBuilder()
                .WithAll<VoronoiCell, CellPolygonVertex, CellTriIndex, DetailLevelData>()
                .WithNone<VoronoiCellMeshTag>() 
                .Build();

            if (query.IsEmpty) return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            int totalEntities = entities.Length;
            
            // Списки для кэширования данных, чтобы избежать обращения к Lookup после структурных изменений
            var validIndices = new NativeList<int>(totalEntities, Allocator.Temp);
            var validColors = new NativeList<float4>(totalEntities, Allocator.Temp); // <-- КЭШ ЦВЕТОВ
            
            var biomeLookup = SystemAPI.GetComponentLookup<CellBiome>(true);
            var levelLookup = SystemAPI.GetComponentLookup<DetailLevelData>(true);
            var bufferLookup = SystemAPI.GetBufferLookup<CellPolygonVertex>(true);

            biomeLookup.Update(ref CheckedStateRef);
            levelLookup.Update(ref CheckedStateRef);
            bufferLookup.Update(ref CheckedStateRef);

            // --- ПРОХОД 1: Фильтрация и подготовка данных (Пока Lookup валидны) ---
            for (int i = 0; i < totalEntities; i++)
            {
                Entity e = entities[i];
                
                var levelData = levelLookup[e];
                if ((debugMask & (1 << (int)levelData.Level)) == 0) continue;

                // Отключаем океан
                bool isOcean = false;
                if (biomeLookup.HasComponent(e))
                {
                    if (biomeLookup[e].Type == BiomeType.Ocean) isOcean = true;
                }
                if (isOcean) continue;

                if (bufferLookup[e].Length < 3) continue;

                // --- РАСЧЕТ ЦВЕТА ЗАРАНЕЕ ---
                float4 color = new float4(0.5f, 0.5f, 0.5f, 1); 
                if (biomeLookup.HasComponent(e))
                {
                    var biome = biomeLookup[e];
                    color = RenderUtils.GetBiomeColor(biome.Type);
                    
                    if (levelData.Level == 0) color *= 0.6f;
                    else if (levelData.Level == DetailLevel.Regional) color *= 0.8f;

                    var random = new Unity.Mathematics.Random((uint)e.Index + 1);
                    float tint = random.NextFloat(-0.05f, 0.05f);
                    color += new float4(tint, tint, tint, 0);
                }

                validIndices.Add(i);
                validColors.Add(color); // Сохраняем цвет
            }

            int count = math.min(validIndices.Length, 2000); 
            if (count == 0) return;

            var mda = Mesh.AllocateWritableMeshData(count);
            var meshes = new Mesh[count];

            // --- ПРОХОД 2: Генерация данных меша (Native Arrays) ---
            for (int k = 0; k < count; k++)
            {
                int index = validIndices[k];
                var e = entities[index];
                // Здесь мы добавляем компонент - это СТРУКТУРНОЕ ИЗМЕНЕНИЕ.
                // После этой строки biomeLookup становится невалидным!
                EntityManager.AddComponent<VoronoiCellMeshTag>(e); 

                meshes[k] = new Mesh { name = $"Cell_{entities[index].Index}" };
                var md = mda[k];

                var verts = EntityManager.GetBuffer<CellPolygonVertex>(e);
                var tris = EntityManager.GetBuffer<CellTriIndex>(e);

                md.SetVertexBufferParams(verts.Length, 
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 0)
                );
                md.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);

                var vb = md.GetVertexData<MyVertex>(stream: 0);
                
                for (int v = 0; v < verts.Length; v++)
                {
                    vb[v] = new MyVertex 
                    {
                        Position = verts[v].Value, 
                        Normal = new float3(0, 1, 0) 
                    };
                }

                var ib = md.GetIndexData<int>();
                for (int t = 0; t < tris.Length; t++)
                {
                    ib[t] = tris[t].Value;
                }

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), MeshUpdateFlags.DontRecalculateBounds);

                // Чтобы не читать levelLookup, можно было закэшировать и offset, 
                // но DetailLevelData мы можем прочитать через EntityManager безопасно, так как он на MainThread.
                // Но лучше взять DetailLevelData до изменений, если возможно. 
                // В данном случае мы просто пересчитаем offset снова или закэшируем.
                // Для простоты оставим чтение компонента, это безопасно через EM, но не через Lookup.
                
                var levelData = EntityManager.GetComponentData<DetailLevelData>(e);
                int lvl = (int)levelData.Level;
                int targetLevel = settings.LevelsCount - 1; 
                float yOffset = (lvl - targetLevel) * 25.0f; 

                EntityManager.SetComponentData(e, new LocalTransform 
                { 
                    Position = new float3(0, yOffset, 0), 
                    Rotation = quaternion.identity, 
                    Scale = 1f 
                });
            }

            Mesh.ApplyAndDisposeWritableMeshData(mda, meshes, MeshUpdateFlags.DontRecalculateBounds);
            
            var rma = new RenderMeshArray(new[] { _defaultMaterial }, meshes);
            var desc = new RenderMeshDescription(ShadowCastingMode.On, true);

            // --- ПРОХОД 3: Настройка рендера (Структурные изменения продолжаются) ---
            for (int k = 0; k < count; k++)
            {
                int originalIndex = validIndices[k];
                var e = entities[originalIndex];
                
                meshes[k].RecalculateNormals();
                meshes[k].RecalculateBounds();
                
                EntityManager.AddComponentData(e, new RenderBounds { Value = meshes[k].bounds.ToAABB() });

                // Берем цвет из КЭША, а не из Lookup!
                float4 color = validColors[k];
                
                if (!EntityManager.HasComponent<URPMaterialPropertyBaseColor>(e))
                    EntityManager.AddComponentData(e, new URPMaterialPropertyBaseColor { Value = color });
                else
                    EntityManager.SetComponentData(e, new URPMaterialPropertyBaseColor { Value = color });

                RenderMeshUtility.AddComponents(e, EntityManager, desc, rma, 
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, k));
            }
            
            if (count > 0)
                Debug.Log($"[MeshSystem] Built {count} Land Meshes.");
        }
    }
}