using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoronoiMapGen.Components;
using VoronoiMapGen.Utils;

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
            var settings = LevelSettings[level];
        
            // Для L0 генерируем глобально
            if (ParentLevel == -1)
            {
                GenerateGlobalSites(level, settings);
                return;
            }
        
            // Для L1+ генерируем внутри ячеек предыдущего уровня
            if (!ParentCells.IsCreated || ParentCells.Length == 0)
            {
                GenerateGlobalSites(level, settings);
                return;
            }
        
            GenerateChildSites(level, settings);
        }
    
        private void GenerateGlobalSites(int level, LevelSettings settings)
        {
            for (int i = 0; i < Sites.Length; i++)
            {
                uint randomSeed = (uint)(BaseSeed + i * 397);
                if (randomSeed == 0) randomSeed = 1;
                var random = new Unity.Mathematics.Random(randomSeed);
            
                float2 position = new float2(
                    random.NextFloat(0, MapSize.x),
                    random.NextFloat(0, MapSize.y)
                );
            
                // Рассчитываем "ценность точки"
                float value = settings.ValueBias + 
                              SimplexNoise(position * 0.001f, BaseSeed) * settings.ValueScale;
            
                Sites[i] = position;
                SiteMetadata[i] = new VoronoiSite
                {
                    Position = position,
                    Index = i,
                    Level = level, // ИСПРАВЛЕНО: уровень как индекс
                    ParentIndex = -1,
                    Value = math.saturate(value)
                };
            }
        }
        
        private void GenerateChildSites(int level, LevelSettings settings)
        {
            int sitesGenerated = 0;
            
            for (int parentIndex = 0; parentIndex < ParentCells.Length; parentIndex++)
            {
                var parentCell = ParentCells[parentIndex];
                // ИЗМЕНЕНО: Используем Level поле из VoronoiCell
                if (parentCell.Level != ParentLevel) continue;
                
                // Количество точек для этой ячейки
                int cellSiteCount = CalculateCellSiteCount(parentCell, settings);
                
                // Генерируем точки внутри ячейки
                for (int i = 0; i < cellSiteCount; i++)
                {
                    if (sitesGenerated >= Sites.Length) break;
                    
                    float2 position = GeneratePointInCell(parentCell, parentIndex, i, settings);
                    
                    // Наследуем "ценность" от родителя + вариации
                    float parentValue = GetParentCellValue(parentIndex);
                    float value = parentValue * 0.7f + 
                                 SimplexNoise(position * 0.01f, BaseSeed + i) * 0.3f;
                    
                    Sites[sitesGenerated] = position;
                    SiteMetadata[sitesGenerated] = new VoronoiSite
                    {
                        Position = position,
                        Index = sitesGenerated,
                        Level = level,
                        ParentIndex = parentIndex,
                        Value = math.saturate(value)
                    };
                    
                    sitesGenerated++;
                }
            }
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
            var random = new Unity.Mathematics.Random(randomSeed);
            
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