// ============================================================
// FILE: Assets\VoronoiMapGen\UI\MapLoaderUI.cs
// ============================================================
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using VoronoiMapGen.Components;

namespace VoronoiMapGen.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MapLoaderUI : MonoBehaviour
    {
        private VisualElement _container;
        private ProgressBar _progressBar;
        private Label _statusLabel;

        private World _world;
        private EntityManager _em;
        private EntityQuery _statusQuery;
        private bool _ecsReady = false;

        private void OnEnable()
        {
            var uiDoc = GetComponent<UIDocument>();
            var root = uiDoc != null ? uiDoc.rootVisualElement : null;

            if (root == null) {
                Debug.LogError("[UI] Ошибка: UIDocument пуст или не назначен UXML!");
                return;
            }

            // === 1. ИЩЕМ ПО ИМЕНАМ ИЗ UXML ===
            // Внимание: имена должны быть как в LoaderView.uxml
            _container = root.Q<VisualElement>("MainContainer");
            _progressBar = root.Q<ProgressBar>("MainBar");
            _statusLabel = root.Q<Label>("StatusTxt");

            // Диагностика (если забыли переназначить файл)
            if (_container == null) Debug.LogError("[UI] Не найден элемент 'MainContainer'. Проверьте UXML.");
            if (_progressBar == null) Debug.LogWarning("[UI] Не найден 'MainBar'");
            
            // Если контейнер не нашли, пытаемся скрыть хоть что-то (сам корень),
            // чтобы окно не висело вечно.
            if (_container == null) _container = root;

            SetVisible(true);
        }

        private void Update()
        {
            // === 2. ПОДКЛЮЧЕНИЕ ECS ===
            if (!_ecsReady) {
                _world = World.DefaultGameObjectInjectionWorld;
                if (_world != null && _world.IsCreated) {
                    _em = _world.EntityManager;
                    _statusQuery = _em.CreateEntityQuery(typeof(GenerationStatus));
                    _ecsReady = true;
                }
                return;
            }

            if (!_world.IsCreated) return;

            // Если в ECS еще нет компонента статуса - ждем
            if (_statusQuery.IsEmpty) return;

            // === 3. ЛОГИКА ===
            var status = _statusQuery.GetSingleton<GenerationStatus>();

            if (status.IsCompleted)
            {
                SetVisible(false);
                return;
            }

            // Идет процесс
            SetVisible(true);

            // Заполняем данными
            if (_progressBar != null)
            {
                float v = Mathf.Clamp01(status.TotalProgress);
                _progressBar.value = v;
                _progressBar.title = $"{(int)(v * 100)}%";
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = status.CurrentStepName.ToString();
            }
        }

        private void SetVisible(bool show)
        {
            if (_container == null) return;
            var target = show ? DisplayStyle.Flex : DisplayStyle.None;
            
            // Меняем, только если состояние изменилось (оптимизация)
            if (_container.style.display.value != target)
            {
                // Debug.Log($"[UI] Changing visibility to: {show}");
                _container.style.display = target;
            }
        }
    }
}