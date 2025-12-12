using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.MapGeneration.Utils
{
    public static class ZoningBuilder
    {
        /// <summary>
        /// Принимает ячейки L3 и данные об их родителях (L2), заполняет массив DistrictData.
        /// </summary>
        public static void CalculateDistricts(
            NativeArray<VoronoiCell> l3Cells,       // Ячейки текущего уровня
            NativeArray<VoronoiSite> l3Meta,        // Метаданные (где хранится ParentIndex)
            NativeArray<SettlementData> l2Settlements, // Данные "Отцов" (Settlements)
            NativeArray<float2> l2Centers,          // Координаты "Отцов"
            ref NativeArray<DistrictData> districts, // Выходной массив
            int seed
        )
        {
            var rng = new Random((uint)seed + 333);

            for (int i = 0; i < l3Cells.Length; i++)
            {
                // Ищем "Папу" (L2 ячейку)
                int parentIdx = l3Meta[i].ParentIndex;
                
                // Проверка валидности папы
                if (parentIdx < 0 || parentIdx >= l2Settlements.Length)
                {
                    // Сирота (или глобальный уровень) -> Парк/Пустошь
                    districts[i] = CreateWilderness(ref rng);
                    continue;
                }

                var parentCiv = l2Settlements[parentIdx];

                // Если папа - Дикая местность, то и дети - дикари
                if (parentCiv.Type == SettlementType.Wilderness)
                {
                    districts[i] = CreateWilderness(ref rng);
                    continue;
                }

                // --- ЛОГИКА ГОРОДА ---
                
                // Считаем дистанцию от центра Района (L3) до центра Города (L2)
                float2 districtPos = l3Cells[i].Centroid;
                float2 cityCenter = l2Centers[parentIdx];
                float dist = math.distance(districtPos, cityCenter);

                // Радиус города зависит от типа (Мегаполис больше)
                float cityRadius = parentCiv.Type == SettlementType.Metropolis ? 400f : 150f;
                if (parentCiv.Type == SettlementType.Outpost) cityRadius = 50f;

                // Нормализованная дистанция (0 = центр, 1 = окраина)
                float t = math.clamp(dist / cityRadius, 0f, 1f);

                // Определение типа района
                DistrictType dType;
                float density;
                int floors;

                if (t < 0.3f) 
                {
                    // === ЦЕНТР (Core) ===
                    dType = DistrictType.Commercial;
                    density = 1.0f; // Плотная застройка
                    floors = parentCiv.Type == SettlementType.Metropolis ? 20 : 6;
                }
                else if (t < 0.7f)
                {
                    // === СРЕДНЯЯ ЗОНА (Mid) ===
                    // 70% жилья, 30% индустрии/магазинов
                    float roll = rng.NextFloat();
                    if (roll > 0.3f) dType = DistrictType.Residential;
                    else dType = DistrictType.Industrial;
                    
                    density = 0.7f;
                    floors = parentCiv.Type == SettlementType.Metropolis ? 8 : 4;
                }
                else
                {
                    // === ОКРАИНА (Edge) ===
                    float roll = rng.NextFloat();
                    if (roll > 0.8f) dType = DistrictType.Park; // Иногда парки на краю
                    else if (roll > 0.6f) dType = DistrictType.Industrial; // Склады
                    else dType = DistrictType.Residential; // Субурбия (частные дома)

                    density = 0.35f;
                    floors = 2; // Малоэтажка
                }

                districts[i] = new DistrictData
                {
                    Type = dType,
                    BuildingDensity = density,
                    MaxFloors = floors,
                    MainRoadWidth = parentCiv.Type == SettlementType.Metropolis ? 8 : 6, // В центре улицы шире
                    InternalAlleyWidth = 4,
                    Seed = (uint)(i * 9283 + seed)
                };
            }
        }

        private static DistrictData CreateWilderness(ref Random rng)
        {
            return new DistrictData 
            { 
                Type = DistrictType.Park, // Парк = Лес/Поле в данном контексте
                BuildingDensity = 0,
                MaxFloors = 0
            };
        }
    }
}