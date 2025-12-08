using UnityEditor;
using UnityEngine;

namespace VoronoiMapGen.Bootstrap
{
    [CustomEditor(typeof(MapGeneratorBootstrap))]
    public class MapGeneratorBootstrapEditor : Editor
    {
        private SerializedProperty debugColorsProp;
        private SerializedProperty debugLevelsProp;

        private GUIStyle headerStyle;

        // Configuration
        private SerializedProperty levelConfigsProp;
        private SerializedProperty mapSizeProp;

        // Colors
        private SerializedProperty oceanColorProp,
            coastColorProp,
            iceColorProp,
            desertColorProp,
            grasslandColorProp,
            forestColorProp,
            mountainColorProp,
            snowColorProp;

        private SerializedProperty renderLevelsProp;
        private SerializedProperty riverDebugLevelsProp;

        private SerializedProperty riverRenderLevelsProp;

        // Core
        private SerializedProperty seedProp;

        // Debug & Gizmos
        private SerializedProperty showRiverGizmosProp;

        // Rendering & Rivers
        private SerializedProperty showRiversProp;
        private SerializedProperty showWireframeProp;
        private SerializedProperty terrainHeightScaleProp;
        private SerializedProperty useAutoLodProp; // <-- Новая галочка
        private SerializedProperty useCacheProp;
        private SerializedProperty visualConfigsProp;

        private void OnEnable()
        {
            // Core
            seedProp = serializedObject.FindProperty("Seed");
            useCacheProp = serializedObject.FindProperty("UseCache");
            mapSizeProp = serializedObject.FindProperty("MapSize");
            terrainHeightScaleProp = serializedObject.FindProperty("TerrainHeightScale");

            // Logic & Styles
            levelConfigsProp = serializedObject.FindProperty("LevelConfigs");
            visualConfigsProp = serializedObject.FindProperty("VisualConfigs");
            useAutoLodProp = serializedObject.FindProperty("UseAutoLOD"); // <-- Связываем

            // Render
            showRiversProp = serializedObject.FindProperty("ShowRivers");
            riverRenderLevelsProp = serializedObject.FindProperty("RiverRenderLevels");
            renderLevelsProp = serializedObject.FindProperty("RenderLevels");

            // Debug
            showRiverGizmosProp = serializedObject.FindProperty("ShowRiverGizmos");
            riverDebugLevelsProp = serializedObject.FindProperty("RiverDebugLevels");
            showWireframeProp = serializedObject.FindProperty("ShowWireframe");
            debugLevelsProp = serializedObject.FindProperty("DebugLevels");
            debugColorsProp = serializedObject.FindProperty("DebugColors");

            // Colors
            oceanColorProp = serializedObject.FindProperty("oceanColor");
            coastColorProp = serializedObject.FindProperty("coastColor");
            iceColorProp = serializedObject.FindProperty("iceColor");
            desertColorProp = serializedObject.FindProperty("desertColor");
            grasslandColorProp = serializedObject.FindProperty("grasslandColor");
            forestColorProp = serializedObject.FindProperty("forestColor");
            mountainColorProp = serializedObject.FindProperty("mountainColor");
            snowColorProp = serializedObject.FindProperty("snowColor");
        }

        public override void OnInspectorGUI()
        {
            var bootstrap = (MapGeneratorBootstrap)target;
            serializedObject.Update();

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                margin = new RectOffset(0, 0, 12, 4),
                alignment = TextAnchor.MiddleLeft
            };

            EditorGUI.BeginChangeCheck();

            DrawHeader("WORLD GENERATION (Require Rebuild)");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(seedProp);
                if (GUILayout.Button("Dice", GUILayout.Width(45)))
                {
                    seedProp.intValue = Random.Range(0, 99999);
                    GUI.FocusControl(null);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(useCacheProp);
                EditorGUILayout.PropertyField(mapSizeProp);
                EditorGUILayout.PropertyField(terrainHeightScaleProp);
            }
            EditorGUILayout.EndVertical();

            DrawHeader("CONFIGURATIONS");

            // Галочка Авто-ЛOД (Важно)
            EditorGUILayout.PropertyField(useAutoLodProp);
            if (useAutoLodProp.boolValue)
                EditorGUILayout.HelpBox(
                    "LOD is managed by Camera Height (System driven). Manual layer controls are disabled.",
                    MessageType.Info);

            if (levelConfigsProp != null)
            {
                levelConfigsProp.isExpanded = EditorGUILayout.Foldout(levelConfigsProp.isExpanded,
                    $"Logic Rules (Count: {levelConfigsProp.arraySize})", true);
                if (levelConfigsProp.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    // true = показывать детей (чтобы видеть поле LOD Threshold)
                    EditorGUILayout.PropertyField(levelConfigsProp, true);
                    EditorGUI.indentLevel--;
                }
            }

            if (visualConfigsProp != null)
            {
                visualConfigsProp.isExpanded = EditorGUILayout.Foldout(visualConfigsProp.isExpanded,
                    $"Visual Styles (Count: {visualConfigsProp.arraySize})", true);
                if (visualConfigsProp.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(visualConfigsProp, true);
                    EditorGUI.indentLevel--;
                }
            }

            DrawHeader("RENDER VISIBILITY");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // Если AutoLOD включен, блокируем ручной выбор уровней, чтобы не путать
                EditorGUI.BeginDisabledGroup(useAutoLodProp.boolValue);
                {
                    EditorGUILayout.LabelField("Terrain Meshes", EditorStyles.miniBoldLabel);
                    DrawCompactLevelMask(renderLevelsProp, "Terrain Layers");

                    EditorGUILayout.Space(6);

                    EditorGUILayout.LabelField("River Meshes", EditorStyles.miniBoldLabel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(showRiversProp, new GUIContent("Enable Generation"));
                    EditorGUILayout.EndHorizontal();

                    if (showRiversProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        DrawCompactLevelMask(riverRenderLevelsProp, "River Layers");
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndVertical();

            // Если были изменения -> ребилд
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                if (Application.isPlaying && GUIUtility.hotControl == 0) bootstrap.ResetVisualization();
            }

            DrawHeader("DEBUG TOOLS (Scene View)");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // Grid
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(showWireframeProp, new GUIContent("Show Terrain Grid"));
                EditorGUILayout.EndHorizontal();

                if (showWireframeProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    DrawCompactLevelMask(debugLevelsProp, "Grid Levels");
                    if (debugColorsProp != null)
                        EditorGUILayout.PropertyField(debugColorsProp, new GUIContent("Grid Colors"), false);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(6);

                // River Graph
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(showRiverGizmosProp, new GUIContent("Show River Graph"));
                EditorGUILayout.EndHorizontal();

                if (showRiverGizmosProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    DrawCompactLevelMask(riverDebugLevelsProp, "Graph Levels");
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);
            var showColors = EditorPrefs.GetBool("MapGen_ShowColors", false);
            showColors = EditorGUILayout.Foldout(showColors, "Biome Palette Colors", true);
            if (showColors)
            {
                EditorGUI.indentLevel++;
                DrawProp(oceanColorProp);
                DrawProp(coastColorProp);
                DrawProp(iceColorProp);
                DrawProp(desertColorProp);
                DrawProp(grasslandColorProp);
                DrawProp(forestColorProp);
                DrawProp(mountainColorProp);
                DrawProp(snowColorProp);
                EditorGUI.indentLevel--;
            }

            EditorPrefs.SetBool("MapGen_ShowColors", showColors);

            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(15);
            GUI.backgroundColor = new Color(0.9f, 0.9f, 1f);
            if (GUILayout.Button("FORCE REBUILD", GUILayout.Height(30)))
                if (Application.isPlaying)
                    bootstrap.ResetVisualization();
            GUI.backgroundColor = Color.white;
            GUILayout.Space(10);
        }

        // --- Helpers ---

        private void DrawHeader(string title)
        {
            GUILayout.Space(5);
            GUILayout.Label(title, headerStyle);
        }

        private void DrawProp(SerializedProperty prop)
        {
            if (prop != null) EditorGUILayout.PropertyField(prop);
        }

        private void DrawCompactLevelMask(SerializedProperty listProp, string label)
        {
            if (listProp == null || !listProp.isArray) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);

            var originalIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            for (var i = 0; i < listProp.arraySize; i++)
            {
                var element = listProp.GetArrayElementAtIndex(i);
                var val = element.boolValue;
                var newVal = EditorGUILayout.ToggleLeft($"L{i}", val, GUILayout.Width(45));
                if (newVal != val) element.boolValue = newVal;
            }

            EditorGUI.indentLevel = originalIndent;
            EditorGUILayout.EndHorizontal();
        }
    }
}