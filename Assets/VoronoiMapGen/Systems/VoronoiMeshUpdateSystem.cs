using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems
{
    /// <summary>
    /// Обновляет геометрию только у тех ячеек, где стоит CellDirtyFlag.
    /// Перезаписывает существующие UnityEngine.Mesh через MeshData (без перевешивания рендер-компонентов).
    /// Также обновляет RenderBounds на сущности.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(VoronoiMeshCreateSystem))]
    public partial struct VoronoiMeshUpdateSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var query = SystemAPI.QueryBuilder()
                .WithAll<CellDirtyFlag, VoronoiCellMeshTag, CellPolygonVertex, CellTriIndex, MaterialMeshInfo, RenderMeshArray>()
                .Build();

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0) return;

            var validEntities = new NativeList<Entity>(entities.Length, Allocator.Temp);
            var targetMeshes   = new System.Collections.Generic.List<UnityEngine.Mesh>(entities.Length);
            var aabbs          = new NativeList<AABB>(entities.Length, Allocator.Temp);

            // Сбор валидных сущностей и ссылок на их Mesh из RenderMeshArray
            foreach (var entity in entities)
            {
                if (!state.EntityManager.Exists(entity)) continue;

                var verts = state.EntityManager.GetBuffer<CellPolygonVertex>(entity);
                var tris  = state.EntityManager.GetBuffer<CellTriIndex>(entity);
                if (verts.Length < 3 || tris.Length < 3) continue;

                var mmi = state.EntityManager.GetComponentData<MaterialMeshInfo>(entity);
                var rma = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(entity);

                int meshIndex = MaterialMeshInfo.StaticIndexToArrayIndex(mmi.Mesh);

                if (meshIndex < 0 || meshIndex >= rma.MeshReferences.Length) continue;

                var meshRef = rma.MeshReferences[meshIndex];
                if (!meshRef.IsValid() || meshRef.Value == null) continue;

                validEntities.Add(entity);
                targetMeshes.Add(meshRef.Value);

                // Предзаполним AABB заглушкой, пересчитаем ниже при записи вершин
                aabbs.Add(new AABB { Center = float3.zero, Extents = new float3(0.5f) });
            }

            if (validEntities.Length == 0) return;

            // Выделяем записи для всех мешей
            var meshDataArray   = UnityEngine.Mesh.AllocateWritableMeshData(validEntities.Length);
            var targetMeshArray = targetMeshes.ToArray();

            // Заполнение новых данных мешей
            for (int i = 0; i < validEntities.Length; i++)
            {
                var entity = validEntities[i];
                var verts  = state.EntityManager.GetBuffer<CellPolygonVertex>(entity);
                var tris   = state.EntityManager.GetBuffer<CellTriIndex>(entity);

                var md = meshDataArray[i];

                md.SetVertexBufferParams(
                    verts.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3)
                );

                md.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);

                // Пишем позиции (X,0,Y) и одновременно считаем AABB
                var positions = md.GetVertexData<UnityEngine.Vector3>();

                float3 min = new float3(float.PositiveInfinity);
                float3 max = new float3(float.NegativeInfinity);

                for (int j = 0; j < verts.Length; j++)
                {
                    float2 v = verts[j].Value;
                    var p = new float3(v.x, 0f, v.y);

                    // в MeshData — UnityEngine.Vector3
                    positions[j] = new UnityEngine.Vector3(p.x, p.y, p.z);

                    min = math.min(min, p);
                    max = math.max(max, p);
                }

                // Индексы
                var indices = md.GetIndexData<int>();
                for (int j = 0; j < tris.Length; j++)
                    indices[j] = tris[j].Value;

                // Один сабмеш
                md.subMeshCount = 1;
                md.SetSubMesh(
                    0,
                    new SubMeshDescriptor(0, tris.Length) { topology = MeshTopology.Triangles },
                    MeshUpdateFlags.Default
                );

                // Сохраняем AABB для установки в RenderBounds после применения
                var center  = (min + max) * 0.5f;
                var extents = (max - min) * 0.5f;
                aabbs[i] = new AABB { Center = center, Extents = extents };
            }

            // Применяем ко всем целевым UnityEngine.Mesh
            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, targetMeshArray, MeshUpdateFlags.Default);

            // Обновляем RenderBounds для корректного кулинга/теней
            for (int i = 0; i < validEntities.Length; i++)
            {
                var aabb = aabbs[i];
                if (state.EntityManager.HasComponent<RenderBounds>(validEntities[i]))
                {
                    state.EntityManager.SetComponentData(validEntities[i], new RenderBounds { Value = aabb });
                }
                else
                {
                    state.EntityManager.AddComponentData(validEntities[i], new RenderBounds { Value = aabb });
                }
            }

            // Убираем флаг "грязности"
            var ecb = SystemAPI.GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            for (int i = 0; i < validEntities.Length; i++)
                ecb.RemoveComponent<CellDirtyFlag>(validEntities[i]);
        }
    }
}
