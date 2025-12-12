using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using VoronoiMapGen.Features.MapInspector.Components;
using VoronoiMapGen.Features.Civilization.Components;
using VoronoiMapGen.Features.MapGeneration.Components;

namespace VoronoiMapGen.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MapInspectorController : MonoBehaviour
    {
        private VisualElement _container;
        private Label _headerLabel;
        private Label _bodyLabel;
        
        private EntityManager _em;
        private EntityQuery _cursorQuery;

        private void OnEnable()
        {
            // Подключаем верстку
            var doc = GetComponent<UIDocument>();
            var root = doc.rootVisualElement;

            if (root == null)
            {
                Debug.LogError("[UI] Root is null. Assign MapInspector.uxml to Source Asset!");
                return;
            }

            // Находим элементы по именам из UXML
            _container = root.Q<VisualElement>("Container");
            _headerLabel = root.Q<Label>("InfoTitle");
            _bodyLabel = root.Q<Label>("InfoBody");

            if (_container == null || _headerLabel == null || _bodyLabel == null)
            {
                Debug.LogError("[UI] Elements not found! Check UXML names: Container, InfoTitle, InfoBody.");
                return;
            }

            // ECS
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null) {
                _em = world.EntityManager;
                _cursorQuery = _em.CreateEntityQuery(typeof(MapCursorData));
            }
        }

        private void Update()
        {
            if (_cursorQuery == default || _cursorQuery.IsEmpty) return;
            var data = _cursorQuery.GetSingleton<MapCursorData>();

            // Если не было изменений и мы уже наведены — не грузим layout
            if (!data.IsDirty && data.IsHovering) return;

            if (data.IsHovering)
            {
                _container.style.opacity = 1f; // Полная видимость
                _container.style.display = DisplayStyle.Flex;
                UpdateContent(data);
            }
            else
            {
                // Эффект "спящего режима": делаем полупрозрачным
                _container.style.opacity = 0.5f; 
                _headerLabel.text = "SCANNING...";
                _bodyLabel.text = "Hover over map";
            }
        }

        private void UpdateContent(MapCursorData data)
        {
            string colorBiome = GetBiomeColorHex(data.CachedBiome);
            string colorCiv = GetCivColorHex(data.CachedSettlement);
            string lvlPrefix = $"L{data.LevelIndex}";

            // 1. ЗАГОЛОВОК
            _headerLabel.text = $"{lvlPrefix} : CELL {data.CellID}";

            // 2. КОНВЕРТАЦИЯ В ЧЕЛОВЕЧЕСКИЕ ЗНАЧЕНИЯ
            
            // Температура: 0..1 маппим в диапазон -35..+45 Цельсия
            float tempCelsius = Mathf.Lerp(-35f, 45f, data.Temperature);
            
            // Влажность: 0..1 маппим в Осадки (см/год), 0..300
            // Или можно показывать проценты. Выберем осадки для "научности".
            float rainFallCm = Mathf.Lerp(0f, 300f, data.Moisture);

            // Оформление цветом (Холодно = синий, Жарко = красный)
            string colorTemp = tempCelsius < 0 ? "#88ccff" : (tempCelsius > 30 ? "#ffaa88" : "#ffffff");

            // 3. ТЕЛО
            string body = "";
    
            // Иерархия
            body += $"<b>PARENT ID:</b> <color=#aaaaaa>{data.ParentID}</color>\n\n";
    
            // Биом
            body += $"<b>BIOME:</b> <color={colorBiome}>{data.CachedBiome.ToString().ToUpper()}</color>\n";
    
            // Климат (Новое!)
            body += $"<b>TEMP:</b> <color={colorTemp}>{tempCelsius:F1}°C</color>   ";
            body += $"<b>RAIN:</b> <color=#aaddee>{rainFallCm:F0} cm/y</color>\n\n";
            
            // Вода/Высота
            string waterInfo = data.IsOcean ? "OCEAN" : (data.IsRiver ? "RIVER" : "DRY");
            string colorWater = data.IsOcean || data.IsRiver ? "#44ccff" : "#888888";
    
            body += $"<b>TERRAIN:</b> <color={colorWater}>{waterInfo}</color> / {(data.CachedElevation * 1000):F0}m\n"; // *1000 чтобы были "метры"
            
            body += "----------------\n";

            // Цивилизация
            string civName = data.CachedSettlement == SettlementType.Wilderness 
                ? "<color=#666666>WILDERNESS</color>" 
                : $"<color={colorCiv}>{data.CachedSettlement.ToString().ToUpper()}</color>";
    
            body += $"<b>CIV:</b> {civName}\n";
            body += $"<b>SUITABILITY:</b> {data.CachedScore:P0}\n"; // Проценты
            body += $"<b>POPULATION:</b> <color=white>{data.CachedPopulation:N0}</color>\n";

            _bodyLabel.text = body;
        }

        // Хелпер для красоты (Tier города)
        private int GetTier(SettlementType s) {
            switch(s) {
                case SettlementType.Metropolis: return 3;
                case SettlementType.Town: return 2;
                case SettlementType.Outpost: return 1;
                default: return 0;
            }
        }

        private string GetBiomeColorHex(BiomeType b) {
            switch(b) {
                case BiomeType.Forest: return "#22dd22";
                case BiomeType.Desert: return "#eedd44";
                case BiomeType.Snow: return "#ffffff";
                case BiomeType.Ice: return "#aaccff";
                case BiomeType.Coast: return "#ffcc88";
                case BiomeType.Ocean: return "#2244ff";
                case BiomeType.Mountain: return "#888888";
                default: return "#bbbbbb";
            }
        }

        private string GetCivColorHex(SettlementType s) {
            switch(s) {
                case SettlementType.Metropolis: return "#ff4444";
                case SettlementType.Town: return "#ffaa00";
                case SettlementType.Outpost: return "#66ff66";
                default: return "#666666";
            }
        }
    }
}
