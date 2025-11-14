using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;
using Random = Unity.Mathematics.Random;

namespace VoronoiMapGen.Jobs
{
    [BurstCompile]
    public struct MultiLevelSiteGenerationJob : IJob
    {
        [ReadOnly] public NativeArray<LevelSettings> LevelSettings;
        [ReadOnly] public float2 MapSize;
        [ReadOnly] public int BaseSeed;
        [ReadOnly] public int ParentLevel;
        [ReadOnly] public NativeArray<VoronoiCell> ParentCells;
        
        public NativeArray<float2> Sites;
        public NativeArray<VoronoiSite> SiteMetadata;
        
        public void Execute()
        {
            int level = ParentLevel + 1;
            LevelSettings settings = LevelSettings[level];
    
            // +++ КРИТИЧЕСКИ ВАЖНО: КОРРЕКТНАЯ ПРОВЕРКА УРОВНЕЙ +++
            if (ParentLevel == -1)
            {
                // L0: всегда глобальная генерация
                GenerateGlobalSites(level, settings);
            }
            else
            {
                // L1+: ВСЕГДА пытаемся генерировать внутри ячеек
                // Даже если ParentCells пустой - это ошибка, которую нужно обработать
                if (ParentCells.IsCreated && ParentCells.Length > 0)
                {
                    GenerateChildSites(level, settings);
                }
                else
                {
                    // +++ ОШИБКА: НЕТ РОДИТЕЛЬСКИХ ЯЧЕЕК ДЛЯ L1+ +++
                    // Но все равно генерируем с ParentIndex = -1 для отладки
                    // GenerateGlobalSites(level, settings);
                
                    Debug.Log("Error");
                    // В продакшене: логировать ошибку или использовать резервную логику
                }
            }
        }
    
        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            for (int i = 0; i < Sites.Length; i++)
            {
                uint randomSeed = (uint)(BaseSeed + i * 397);
                if (randomSeed == 0) randomSeed = 1;
                Random random = new Unity.Mathematics.Random(randomSeed);
            
                float2 position = new float2(
                    random.NextFloat(0, MapSize.x),
                    random.NextFloat(0, MapSize.y)
                );
            
                float value = level == 0 
                    ? CalculateBaseValue(position, settings) 
                    : settings.ValueBias + SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale;
            
                Sites[i] = position;
                SiteMetadata[i] = new VoronoiSite
                {
                    Position = position,
                    Index = i,
                    Level = level,
                    ParentIndex = -1, // +++ КОРРЕКТНО ДЛЯ L0 +++
                    Value = math.saturate(value)
                };
            }
        }
        
        private void GenerateChildSites(int level, LevelSettings settings)
        {
            int sitesGenerated = 0;
            
            for (int parentIndex = 0; parentIndex < ParentCells.Length; parentIndex++)
            {
                VoronoiCell parentCell = ParentCells[parentIndex];
                // +++ ИСПРАВЛЕНИЕ: ПРОВЕРКА УРОВНЯ РОДИТЕЛЬСКОЙ ЯЧЕЙКИ +++
                if (parentCell.Level != ParentLevel) continue;
                
                // +++ УЛУЧШЕННЫЙ РАСЧЕТ КОЛИЧЕСТВА ТОЧЕК +++
                int cellSiteCount = CalculateCellSiteCount(parentCell, settings);
                
                for (int i = 0; i < cellSiteCount && sitesGenerated < Sites.Length; i++)
                {
                    float2 position = GeneratePointInCell(parentCell, parentIndex, i, settings);
                    
                    // +++ КОРРЕКТНОЕ НАСЛЕДОВАНИЕ ЦЕННОСТИ +++
                    float parentValue = GetParentCellValue(parentIndex);
                    float value = parentValue * 0.7f + 
                                 SimplexNoise(position * 0.01f, BaseSeed + parentIndex * 100 + i) * 0.3f;
                    
                    Sites[sitesGenerated] = position;
                    SiteMetadata[sitesGenerated] = new VoronoiSite
                    {
                        Position = position,
                        Index = sitesGenerated,
                        Level = level,
                        ParentIndex = parentIndex, // +++ КОРРЕКТНЫЙ ИНДЕКС РОДИТЕЛЬСКОЙ ЯЧЕЙКИ +++
                        Value = math.saturate(value)
                    };
                    
                    sitesGenerated++;
                }
            }
            
            // +++ ДОПОЛНЕНИЕ: ЗАПОЛНЕНИЕ ОСТАВШИХСЯ ТОЧЕК ЕСЛИ НЕ ХВАТИЛО +++
            if (sitesGenerated < Sites.Length)
            {
                // Заполняем оставшиеся точки глобально
                for (int i = sitesGenerated; i < Sites.Length; i++)
                {
                    uint randomSeed = (uint)(BaseSeed + level * 1000 + i);
                    Random random = new Unity.Mathematics.Random(randomSeed);
                    
                    float2 position = new float2(
                        random.NextFloat(0, MapSize.x),
                        random.NextFloat(0, MapSize.y)
                    );
                    
                    Sites[i] = position;
                    SiteMetadata[i] = new VoronoiSite
                    {
                        Position = position,
                        Index = i,
                        Level = level,
                        ParentIndex = -1, // Без родителя
                        Value = 0.5f
                    };
                }
            }
        }

        private float CalculateBaseValue(float2 position, LevelSettings settings)
        {
            // Специальная логика для базового уровня
            float continentNoise = SimplexNoise(position * 0.0001f, BaseSeed);
            float elevationBase = continentNoise * 0.8f + 0.2f;
            return math.saturate(elevationBase);
        }
        
        private int CalculateCellSiteCount(VoronoiCell parentCell, LevelSettings settings)
        {
            // Распределяем точки пропорционально площади ячейки
            // Упрощенный расчет (реальный должен быть точнее)
            return (int)(settings.SiteCount * 0.1f);
        }
        
        private float2 GeneratePointInCell(VoronoiCell cell, int parentIndex, int index, LevelSettings settings)
        {
            // Используем семя, зависящее от родителя
            int seed = BaseSeed ^ parentIndex ^ index;
            uint randomSeed = (uint)(seed);
            if (randomSeed == 0) randomSeed = 1;
            Random random = new Unity.Mathematics.Random(randomSeed);
            
            // Генерируем внутри ячейки (упрощенный пример)
            float maxOffset = settings.ScaleFactor * 50f;
            float2 offset = new float2(
                random.NextFloat(-maxOffset, maxOffset),
                random.NextFloat(-maxOffset, maxOffset)
            );
            
            return cell.Centroid + offset;
        }
        
        private float GetParentCellValue(int parentIndex)
        {
            // Здесь должен быть запрос к родительским точкам
            // Для примера возвращаем константу
            return 0.5f;
        }
        
        private float SimplexNoise(float2 pos, int seed)
        {
            // Реализация или вызов твоего шума
            return noise.snoise(pos + new float2(seed));
        }
    }
}