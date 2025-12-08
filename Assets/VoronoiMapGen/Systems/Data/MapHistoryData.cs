using Unity.Collections;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Data
{
    public class MapHistoryData
    {
        // Храним только массив структур данных
        private MapLevelData[] _levels;

        public MapHistoryData(int levelsCount)
        {
            _levels = new MapLevelData[levelsCount];
        }

        public void StoreLevel(MapLevelData data)
        {
            int lvl = data.LevelIndex;
            // Освобождаем старое, если было (на всякий случай)
            if (_levels[lvl].IsCreated) _levels[lvl].Dispose();

            // === DEEP COPY (Глубокое копирование) ===
            // Мы обязаны скопировать данные, так как исходные массивы могут быть
            // помечены как TempJob/Persistent и удалены в вызывающем коде.
            
            var stored = new MapLevelData
            {
                LevelIndex = lvl,
                Sites = Copy(data.Sites),
                Meta = Copy(data.Meta),
                Cells = Copy(data.Cells),
                // Edges часто огромные, их можно не хранить в истории, если они нужны только для ECS создания
                // Но если нужны для логики - копируй. Допустим, не нужны для генерации детей.
                
                Tectonics = Copy(data.Tectonics),
                Climate = Copy(data.Climate),
                Hydrology = Copy(data.Hydrology),
                Biomes = Copy(data.Biomes) // Если биомы нужны детям
            };

            _levels[lvl] = stored;
        }

        public bool TryGetLevel(int level, out MapLevelData data)
        {
            data = default;
            if (level < 0 || level >= _levels.Length) return false;
            if (!_levels[level].IsCreated) return false;

            data = _levels[level];
            return true;
        }

        public void Dispose()
        {
            if (_levels == null) return;
            for (int i = 0; i < _levels.Length; i++)
            {
                _levels[i].Dispose();
            }
        }

        // Хелпер копирования
        private NativeArray<T> Copy<T>(NativeArray<T> source) where T : struct
        {
            if (!source.IsCreated) return default;
            var newArr = new NativeArray<T>(source.Length, Allocator.Persistent);
            newArr.CopyFrom(source);
            return newArr;
        }
    }
}