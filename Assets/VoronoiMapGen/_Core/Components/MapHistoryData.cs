using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics; // if needed
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.Features.Data
{
    public class MapHistoryData
    {
        // Container for history snapshots
        private readonly MapLevelData[] _levels;

        public MapHistoryData(int levelsCount)
        {
            _levels = new MapLevelData[levelsCount];
        }

        // --- OPTIMIZATION: TRANSFER OWNERSHIP ---
        /// <summary>
        /// Stores the level data by taking ownership of the NativeArrays.
        /// Warning: Do NOT dispose of data inside MapLevelData after calling this.
        /// </summary>
        public void StoreLevel(MapLevelData data)
        {
            int lvl = data.LevelIndex;
            
            // Safety: If there is old data at this index, we must dispose it before overwriting.
            if (_levels[lvl].IsCreated) 
            {
                _levels[lvl].Dispose();
            }

            // Assign directly. No copying.
            _levels[lvl] = data;
        }

        public bool TryGetLevel(int level, out MapLevelData data)
        {
            data = default;
            if (level < 0 || level >= _levels.Length) return false;
            
            // Return struct copy (points to same pointers)
            // It is strictly Read-Only for the caller
            if (!_levels[level].IsCreated) return false;

            data = _levels[level];
            return true;
        }

        public void Dispose()
        {
            if (_levels == null) return;
            for (int i = 0; i < _levels.Length; i++)
            {
                if (_levels[i].IsCreated)
                {
                    _levels[i].Dispose();
                }
            }
        }
    }
}