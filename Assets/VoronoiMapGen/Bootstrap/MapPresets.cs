// ============================================================
// FILE: Assets\VoronoiMapGen\Bootstrap\MapPresets.cs
// ============================================================
using VoronoiMapGen.Features.Rendering.Components;

namespace VoronoiMapGen.Bootstrap
{
    public static class MapPresets
    {
        public static MapLevelProfile[] GetDefault5Levels()
        {
            return new MapLevelProfile[]
            {
                // L0: CONTINENT / GLOBAL (Огромные полигоны, грубый ландшафт)
                new MapLevelProfile
                {
                    ProfileName = "L0 - Global (Continent)",
                    LODThreshold = 3500f, 
                    RenderThreshold = 5000f,
                    
                    MinSites = 60, MaxSites = 120, // Немного крупных плит
                    ScaleFactor = 1.0f,
                    RelaxationIterations = 2,
                    
                    Style = TerrainStyle.Smooth,
                    HeightScale = 120f,
                    BottomDepth = 80f,
                    TopSurfaceNoise = 0.5f, // Сильный шум (горы)
                    TextureScale = 0.01f,
                    
                    RiverWidthMultiplier = 8.0f, // Широченные реки
                    GenerateRoads = false // Нет дорог, только торговые пути (условно)
                },

                // L1: REGION (Провинции, области)
                new MapLevelProfile
                {
                    ProfileName = "L1 - Region",
                    LODThreshold = 1800f,
                    RenderThreshold = 3500f,

                    MinSites = 10, MaxSites = 15, // Делим L0 на 10-15 кусков
                    ScaleFactor = 0.35f,
                    RelaxationIterations = 3, // Более ровная сетка

                    Style = TerrainStyle.Blocky,
                    HeightScale = 60f,
                    BottomDepth = 40f,
                    TopSurfaceNoise = 0.3f,
                    TextureScale = 0.03f,

                    RiverWidthMultiplier = 4.0f,
                    GenerateRoads = true,
                    MainRoadWidth = 4.0f // Магистрали
                },

                // L2: SETTLEMENT (Городская черта, природа вокруг)
                new MapLevelProfile
                {
                    ProfileName = "L2 - Settlement Area",
                    LODThreshold = 800f,
                    RenderThreshold = 1800f,

                    MinSites = 8, MaxSites = 12,
                    ScaleFactor = 0.4f,
                    RelaxationIterations = 3,

                    Style = TerrainStyle.Stratified,
                    HeightScale = 30f,
                    BottomDepth = 20f,
                    TopSurfaceNoise = 0.15f,
                    RockLayers = 2, LayerInset = 0.2f,
                    
                    RiverWidthMultiplier = 2.0f,
                    GenerateRoads = true,
                    MainRoadWidth = 2.5f,
                    SecondaryRoadWidth = 1.5f
                },

                // L3: URBAN DISTRICT (Районы города, поля)
                new MapLevelProfile
                {
                    ProfileName = "L3 - Urban District",
                    LODThreshold = 300f,
                    RenderThreshold = 800f,

                    MinSites = 10, MaxSites = 20, // Много мелких кварталов
                    ScaleFactor = 0.5f,
                    RelaxationIterations = 4, // "Квадратные" кварталы

                    Style = TerrainStyle.Blocky,
                    HeightScale = 10f, // Плоский город
                    BottomDepth = 10f,
                    TopSurfaceNoise = 0.05f, // Почти ровно
                    TextureScale = 0.1f,

                    RiverWidthMultiplier = 1.0f,
                    MeanderAmplitude = 0.5f, // Каналы прямые
                    
                    GenerateRoads = true,
                    MainRoadWidth = 1.8f, // Улицы
                    SecondaryRoadWidth = 1.0f // Переулки
                },

                // L4: DETAIL / BUILDING (Здания, интерьеры дворов)
                new MapLevelProfile
                {
                    ProfileName = "L4 - Architecture/Detail",
                    LODThreshold = 0f, // Видно вплоть до земли
                    RenderThreshold = 300f,

                    MinSites = 4, MaxSites = 8,
                    ScaleFactor = 0.4f,
                    RelaxationIterations = 1, // Хаос во дворах

                    Style = TerrainStyle.Blocky,
                    HeightScale = 5f,
                    BottomDepth = 5f,
                    TopSurfaceNoise = 0.02f,
                    TextureScale = 0.25f,

                    GenerateRoads = true,
                    MainRoadWidth = 0.8f, // Тропинки
                    SecondaryRoadWidth = 0.5f
                }
            };
        }
    }
}