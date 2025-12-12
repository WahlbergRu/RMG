using System;
using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Features.MapGeneration.Components
{
    // Типы зонирования районов. Определяют алгоритм нарезки и внешний вид зданий.
    public enum DistrictType : byte
    {
        Residential = 0, // Жилой: плотная нарезка, дворы
        Commercial = 1,  // Деловой: крупные участки, небоскребы
        Industrial = 2,  // Промзона: ангары, заборы, пустыри
        Park = 3,        // Парк: нет зданий, только дорожки и деревья
        Plaza = 4,       // Площадь: открытое пространство
        Military = 5     // Форт/База: стены по периметру
    }

    // --- КОНФИГУРАЦИЯ РАЙОНА (Входные данные) ---
    // Этот компонент вешается на ячейку Вороного уровня L3.
    // Его генерирует ZoningBuilder на основе родительского Мегаполиса.
    [Serializable]
    public struct DistrictData : IComponentData
    {
        public DistrictType Type;
        public uint Seed;
        
        // Плотность застройки (0.0 - редкие домики, 1.0 - стена к стене)
        public float BuildingDensity; 
        
        // Максимальная высота зданий в этажах (влияет на генерацию меша L5)
        public int MaxFloors;            

        // Ширина отступа от границ ячейки Вороного (Дороги + Тротуары)
        public float MainRoadWidth;      
        
        // Ширина зазоров между парцеллами внутри района
        public float InternalAlleyWidth; 
    }

    // --- ПАРЦЕЛЛА (Результат нарезки) ---
    // Система DistrictPlanningSystem "разрезает" район и спавнит много таких сущностей.
    // Каждая сущность - это площадка под одно будущее здание.
    public struct ParcelData : IComponentData
    {
        public int ParentDistrictId; // ID ячейки Вороного, откуда этот кусок
        
        // Геометрия прямоугольника (OBB)
        public float2 Center;        // World Space XZ
        public float2 Size;          // Width, Depth
        public quaternion Rotation;  // Ориентация здания (Лицом к улице)
        
        // Вектор к главной улице (Чтобы знать, где делать Вход/Витрины)
        public float3 RoadDirection; 
        
        // Данные для генератора зданий (L5)
        public DistrictType Zoning;  // Тип здания
        public int HeightConstraint; // Ограничение для конкретного дома
    }

    // Тэг-запрос: "На этой парцелле нужно построить здание"
    // Когда система L5 (Building System) видит этот тэг, она начинает строить воксели/меши.
    public struct PendingBuildingRequestTag : IComponentData, IEnableableComponent { }
}
