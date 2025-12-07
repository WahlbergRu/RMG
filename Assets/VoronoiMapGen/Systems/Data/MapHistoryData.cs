using Unity.Collections;
using Unity.Mathematics;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.Systems.Data
{
    /// <summary>
    /// Исправленная версия: теперь создает глубокие копии для Sites и Meta,
    /// чтобы избежать Dispose() в вызывающей системе.
    /// </summary>
    public class MapHistoryData
    {
        public NativeArray<VoronoiCell>[] Cells;
        public NativeArray<float2>[] Sites;
        public NativeArray<VoronoiSite>[] Meta;
        public NativeArray<HydrologyData>[] Hydrology;
        public NativeArray<TectonicPlateData>[] Tectonics;
        public NativeArray<ClimateData>[] Climate;

        private int _levelsCount;

        public MapHistoryData(int levelsCount)
        {
            _levelsCount = levelsCount;
            Cells = new NativeArray<VoronoiCell>[_levelsCount];
            Sites = new NativeArray<float2>[_levelsCount];
            Meta = new NativeArray<VoronoiSite>[_levelsCount];
            Hydrology = new NativeArray<HydrologyData>[_levelsCount];
            Tectonics = new NativeArray<TectonicPlateData>[_levelsCount];
            Climate = new NativeArray<ClimateData>[_levelsCount];
        }

        public void StoreLevel(int level, 
            NativeArray<float2> sites, 
            NativeArray<VoronoiSite> meta, 
            NativeList<VoronoiCell> cellsList,
            NativeArray<TectonicPlateData> tectonics,
            NativeArray<ClimateData> climate,
            NativeArray<HydrologyData> hydro)
        {
            // === ИСПРАВЛЕНИЕ: ДЕЛАЕМ КОПИИ МАССИВОВ ===
            // Раньше тут было Sites[level] = sites, что сохраняло ссылку, 
            // которая потом уничтожалась системой.
            
            CopyArray(ref Sites[level], sites);
            CopyArray(ref Meta[level], meta);
            CopyArray(ref Tectonics[level], tectonics);
            CopyArray(ref Climate[level], climate);
            CopyArray(ref Hydrology[level], hydro);

            // Cells приходят в виде List, копируем вручную
            if (Cells[level].IsCreated) Cells[level].Dispose();
            Cells[level] = new NativeArray<VoronoiCell>(cellsList.Length, Allocator.Persistent);
            Cells[level].CopyFrom(cellsList.AsArray());
        }

        // Генерик-хелпер для безопасного копирования
        private void CopyArray<T>(ref NativeArray<T> destination, NativeArray<T> source) where T : struct
        {
            if (destination.IsCreated) destination.Dispose();
            destination = new NativeArray<T>(source.Length, Allocator.Persistent);
            destination.CopyFrom(source);
        }

        public void Dispose()
        {
            for (int i = 0; i < _levelsCount; i++)
            {
                if (Cells[i].IsCreated) Cells[i].Dispose();
                if (Sites[i].IsCreated) Sites[i].Dispose();
                if (Meta[i].IsCreated) Meta[i].Dispose();
                if (Hydrology[i].IsCreated) Hydrology[i].Dispose();
                if (Tectonics[i].IsCreated) Tectonics[i].Dispose();
                if (Climate[i].IsCreated) Climate[i].Dispose();
            }
        }
        
        public bool TryGetPreviousLevel(int currentLevel, 
            out NativeArray<VoronoiCell> pCells, 
            out NativeArray<float2> pSites,
            out NativeArray<VoronoiSite> pMeta,
            out NativeArray<HydrologyData> pHydro,
            out NativeArray<TectonicPlateData> pTect,
            out NativeArray<ClimateData> pClim)
        {
            pCells = default; pSites = default; pMeta = default;
            pHydro = default; pTect = default; pClim = default;

            if (currentLevel <= 0) return false;
            int prev = currentLevel - 1;

            // Проверка, что предыдущий уровень вообще существует
            if (!Cells[prev].IsCreated || !Sites[prev].IsCreated || !Meta[prev].IsCreated) return false;

            pCells = Cells[prev];
            pSites = Sites[prev];
            pMeta = Meta[prev];
            pHydro = Hydrology[prev];
            pTect = Tectonics[prev];
            pClim = Climate[prev];
            return true;
        }
    }
}