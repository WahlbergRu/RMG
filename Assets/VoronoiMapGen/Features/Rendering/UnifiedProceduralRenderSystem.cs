using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Features.Rendering
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class UnifiedProceduralRenderSystem : SystemBase
    {
        private readonly Dictionary<Entity, Mesh> _activeMeshes = new();

        protected override void OnCreate()
        {
            RequireForUpdate<UnifiedRenderTag>();
        }

        protected override void OnDestroy()
        {
            var manager = UnifiedResourceManager.TryGetInstance();
            foreach (var mesh in _activeMeshes.Values)
                if (manager != null) manager.SafeDestroy(mesh);
                else if (mesh != null) Object.DestroyImmediate(mesh);
            _activeMeshes.Clear();
        }

        protected override void OnUpdate()
        {
            var manager = UnifiedResourceManager.Instance;
            if (manager == null) return;

            // 1. CLEANUP
            var cleanupQuery = SystemAPI.QueryBuilder()
                .WithAll<ProceduralMeshReference>()
                .WithNone<ProceduralMeshRequest>()
                .Build();

            if (!cleanupQuery.IsEmpty)
            {
                var entitiesToClean = cleanupQuery.ToEntityArray(Allocator.Temp);
                for (var i = 0; i < entitiesToClean.Length; i++)
                {
                    var e = entitiesToClean[i];
                    if (_activeMeshes.TryGetValue(e, out var mesh))
                    {
                        manager.SafeDestroy(mesh);
                        _activeMeshes.Remove(e);
                    }
                    EntityManager.RemoveComponent<ProceduralMeshReference>(e);
                }
                entitiesToClean.Dispose();
            }

            // 2. RENDER GENERATION
            var drawQuery = SystemAPI.QueryBuilder()
                .WithAll<MeshDirtyTag, ProceduralMeshRequest, ProceduralVertex, ProceduralIndex>()
                .Build();

            if (drawQuery.IsEmpty) return;

            var entities = drawQuery.ToEntityArray(Allocator.Temp);
            var requests = drawQuery.ToComponentDataArray<ProceduralMeshRequest>(Allocator.Temp);

            try
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    var req = requests[i];
                    
                    var verts = EntityManager.GetBuffer<ProceduralVertex>(e);
                    var inds = EntityManager.GetBuffer<ProceduralIndex>(e);

                    if (verts.Length < 3 || inds.Length < 3) 
                    {
                        EntityManager.SetComponentEnabled<MeshDirtyTag>(e, false);
                        continue; 
                    }

                    Mesh mesh;
                    var isNew = false;

                    if (!_activeMeshes.TryGetValue(e, out mesh))
                    {
                        mesh = new Mesh();
                        mesh.name = $"ProceduralChunk_{e.Index}";
                        mesh.MarkDynamic();
                        _activeMeshes[e] = mesh;
                        isNew = true;
                    }
                    else
                    {
                        mesh.Clear(false);
                    }

                    // --- Geometry Setup (Updated Layout) ---
                    // Добавили Color (float4) в структуру меша
                    var layout = new NativeArray<VertexAttributeDescriptor>(4, Allocator.Temp);
                    layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
                    layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
                    layout[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4); // RGBA
                    layout[3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

                    mesh.SetVertexBufferParams(verts.Length, layout);
                    layout.Dispose();

                    mesh.SetVertexBufferData(verts.AsNativeArray(), 0, 0, verts.Length);

                    mesh.SetIndexBufferParams(inds.Length, IndexFormat.UInt32);
                    mesh.SetIndexBufferData(inds.AsNativeArray(), 0, 0, inds.Length);

                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, inds.Length));
                    mesh.RecalculateBounds();

                    // --- Material & Color ---
                    // Ставим Bounds для ECS
                    EntityManager.SetComponentData(e, new RenderBounds { Value = mesh.bounds.ToAABB() });
                    
                    var matName = req.MaterialName.ToString();
                    if (string.IsNullOrEmpty(matName)) matName = "Universal Render Pipeline/Lit";
                    
                    // Цвета теперь в вершинах, материал берем просто белый (чтобы умножать на Vertex Color) 
                    // или используем настройки если вершинный цвет не нужен. 
                    // Важно: Чтобы видеть цвета, URP материал должен иметь галочку "Vertex Color" или шейдер должен поддерживать это.
                    // Для Standard URP Lit часто требуется небольшая настройка или белый цвет материала.
                    var mat = manager.GetMaterial(matName, new float4(1,1,1,1), req.Smoothness);

                    RenderMeshUtility.AddComponents(
                        e,
                        EntityManager,
                        new RenderMeshDescription(ShadowCastingMode.On, true),
                        new RenderMeshArray(new[] { mat }, new[] { mesh }),
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                    );

                    if (isNew)
                    {
                        EntityManager.AddComponentData(e, new ProceduralMeshReference { MeshInstanceID = mesh.GetInstanceID() });
                    }

                    EntityManager.SetComponentEnabled<MeshDirtyTag>(e, false);
                }
            }
            finally
            {
                entities.Dispose();
                requests.Dispose();
            }
        }
    }
}