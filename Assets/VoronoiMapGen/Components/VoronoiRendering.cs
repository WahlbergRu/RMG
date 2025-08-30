using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Components
{
    /// <summary> Сущность имеет сгенерированный и привязанный меш ячейки. </summary>
    public struct VoronoiCellMeshTag : IComponentData {}

    /// <summary> Геометрия/топология ячейки изменилась — нужно обновить меш. </summary>
    public struct CellDirtyFlag : IComponentData {}

    /// <summary> Контур ячейки (XZ, Y=0 при рендере). </summary>
    [InternalBufferCapacity(6)]
    public struct CellPolygonVertex : IBufferElementData
    {
        public float2 Value;
    }

    /// <summary> Триангуляция фаном: пары (i, i+1), нулевой индекс вставляется при записи. </summary>
    [InternalBufferCapacity(12)]
    public struct CellTriIndex : IBufferElementData
    {
        public int Value;
    }

    /// <summary> Опциональная локальная позиция ячейки. Если не задана — берем позицию сайта. </summary>
    public struct CellLocalPosition : IComponentData
    {
        public float3 Value;
    }
    
    public struct GeometryBuiltTag : IComponentData { }
    
}
