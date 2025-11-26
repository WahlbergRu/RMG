using Unity.Entities;
using Unity.Mathematics;

namespace VoronoiMapGen.Components
{
    // L0: Тектоника
    public struct TectonicPlateData : IComponentData
    {
        public bool IsOcean;        // Океаническая или материковая
        public float2 Velocity;     // Вектор движения плиты
        public float BaseHeight;    // Базовая высота (-1000 для океана, +500 для суши)
        public float CrustAge;      // Возраст коры (влияет на эрозию)
    }

    // L1: Климат (Рассчитывается на основе L0)
    public struct ClimateData : IComponentData
    {
        public float Temperature;   // 0..1 (Холод -> Жара)
        public float Moisture;      // 0..1 (Сухо -> Влажно)
        public float WindDirection; // Угол ветра (радианы)
    }

    // Итоговый Биом (Результат L0 + L1)
    public struct BiomeData : IComponentData
    {
        public BiomeType Type;
    }
}