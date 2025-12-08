using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

namespace VoronoiMapGen.Systems.Rendering
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class UnifiedProceduralRenderSystem : SystemBase
    {
        // Словарь: Entity -> Живой Unity Mesh
        private Dictionary<Entity, Mesh> _activeMeshes = new Dictionary<Entity, Mesh>();

        protected override void OnCreate()
        {
            RequireForUpdate<UnifiedRenderTag>();
        }

        protected override void OnDestroy()
        {
            var manager = UnifiedResourceManager.TryGetInstance();
            foreach (var mesh in _activeMeshes.Values)
            {
                if (manager != null) manager.SafeDestroy(mesh);
                else if (mesh != null) Object.DestroyImmediate(mesh);
            }
            _activeMeshes.Clear();
        }

        protected override void OnUpdate()
        {
            var manager = UnifiedResourceManager.Instance;
            if (manager == null) return;

            // --------------------------------------------------------
            // 1. ОЧИСТКА (CLEANUP)
            // --------------------------------------------------------
            // Находим сущности, у которых есть Reference (меш был создан),
            // но больше нет Request (или сущность удалена, но компонент очистки остался)
            
            var cleanupQuery = SystemAPI.QueryBuilder()
                .WithAll<ProceduralMeshReference>()
                .WithNone<ProceduralMeshRequest>()
                .Build();

            if (!cleanupQuery.IsEmpty)
            {
                // Собираем в массив, чтобы не менять структуру во время итерации
                var entitiesToClean = cleanupQuery.ToEntityArray(Allocator.Temp);
                
                for (int i = 0; i < entitiesToClean.Length; i++)
                {
                    Entity e = entitiesToClean[i];
                    if (_activeMeshes.TryGetValue(e, out var mesh))
                    {
                        manager.SafeDestroy(mesh);
                        _activeMeshes.Remove(e);
                    }
                    EntityManager.RemoveComponent<ProceduralMeshReference>(e);
                }
                entitiesToClean.Dispose();
            }

            // --------------------------------------------------------
            // 2. СБОР КАНДИДАТОВ НА ОБНОВЛЕНИЕ
            // --------------------------------------------------------
            // Ищем сущности с данными, которые требуют отрисовки
            
            var drawQuery = SystemAPI.QueryBuilder()
                .WithAll<ProceduralMeshRequest, ProceduralVertex, ProceduralIndex>()
                .Build();

            if (drawQuery.IsEmpty) return;

            // Мы не можем менять компоненты внутри foreach, поэтому сначала собираем ID
            var entities = drawQuery.ToEntityArray(Allocator.Temp);
            var requests = drawQuery.ToComponentDataArray<ProceduralMeshRequest>(Allocator.Temp);
            
            var entitiesToUpdate = new NativeList<Entity>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                // Фильтр: если меш уже есть и он НЕ грязный -> пропускаем
                if (_activeMeshes.ContainsKey(entities[i]) && !requests[i].IsDirty)
                    continue;

                entitiesToUpdate.Add(entities[i]);
            }

            // --------------------------------------------------------
            // 3. ОБРАБОТКА (ГЕНЕРАЦИЯ МЕШЕЙ)
            // --------------------------------------------------------
            // Теперь безопасно выполняем структурные изменения
            
            for (int i = 0; i < entitiesToUpdate.Length; i++)
            {
                Entity e = entitiesToUpdate[i];
                var req = EntityManager.GetComponentData<ProceduralMeshRequest>(e);

                var verts = EntityManager.GetBuffer<ProceduralVertex>(e);
                var inds = EntityManager.GetBuffer<ProceduralIndex>(e);

                if (verts.Length < 3 || inds.Length < 3) continue;

                // --- Работа с Unity Mesh ---
                Mesh mesh;
                bool isNew = false;

                if (!_activeMeshes.TryGetValue(e, out mesh))
                {
                    mesh = new Mesh();
                    mesh.name = $"Procedural_{e.Index}";
                    mesh.MarkDynamic();
                    _activeMeshes[e] = mesh;
                    isNew = true;
                }
                else
                {
                    mesh.Clear(keepVertexLayout: false);
                }

                // Загрузка вершин
                var layout = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
                layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
                layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
                layout[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

                mesh.SetVertexBufferParams(verts.Length, layout);
                layout.Dispose();

                mesh.SetVertexBufferData(verts.AsNativeArray(), 0, 0, verts.Length);

                // Загрузка индексов
                mesh.SetIndexBufferParams(inds.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(inds.AsNativeArray(), 0, 0, inds.Length);

                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, inds.Length, MeshTopology.Triangles));
                mesh.RecalculateBounds();

                // --- Настройка ECS Rendering ---
                string matName = req.MaterialName.ToString();
                if (string.IsNullOrEmpty(matName)) matName = "Universal Render Pipeline/Lit";
                Material mat = manager.GetMaterial(matName, req.Color, req.Smoothness);

                // ВАЖНО: RenderMeshUtility.AddComponents требует EntityManager и делает Structural Changes
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

                // Снимаем флаг Dirty
                req.IsDirty = false;
                EntityManager.SetComponentData(e, req);
            }

            entities.Dispose();
            requests.Dispose();
            entitiesToUpdate.Dispose();
        }
    }
    
    public struct UnifiedRenderTag : IComponentData {}
}