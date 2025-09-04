using System;
using Unity.Collections;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Contexts
{
    /// <summary>
    /// Контекст для передачи результата генерации уровня (владеет NativeList<VoronoiCell>).
    /// </summary>
    public class LevelGenerationContext : IDisposable
    {
        private NativeList<VoronoiCell> _cells;

        public LevelGenerationContext(NativeList<VoronoiCell> cells)
        {
            _cells = cells;
        }

        public int CellCount => _cells.IsCreated ? _cells.Length : 0;

        public void CopyTo(NativeArray<VoronoiCell> destination)
        {
            if (!destination.IsCreated || destination.Length < _cells.Length)
            {
                UnityEngine.Debug.LogError($"Cannot copy {_cells.Length} cells to array of size {(destination.IsCreated ? destination.Length : 0)}");
                return;
            }

            for (int i = 0; i < _cells.Length; i++)
                destination[i] = _cells[i];
        }

        public void Dispose()
        {
            if (_cells.IsCreated)
                _cells.Dispose();
        }
    }
}