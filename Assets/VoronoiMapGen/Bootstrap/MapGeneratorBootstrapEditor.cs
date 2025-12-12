using UnityEditor;
using UnityEngine;

namespace VoronoiMapGen.Bootstrap
{
    [CustomEditor(typeof(MapGeneratorBootstrap))]
    public class MapGeneratorBootstrapEditor : Editor
    {
        // General
        private SerializedProperty seedProp, useCacheProp, mapSizeProp, terrainHeightScaleProp;
        // Climate
        private SerializedProperty baseTempProp, tempHeightProp, baseMoistureProp, rainIntProp, fluxThresholdProp;
        // Civ
        private SerializedProperty globalPopScalarProp, minPopOutpostProp, minPopTownProp, minPopMetropolisProp, showSettlementsProp;
        private SerializedProperty minSiteQualityProp, townSpawnChanceProp, outpostSpawnChanceProp;
        // Logic
        private SerializedProperty useAutoLodProp, levelsProp;
        // Render
        private SerializedProperty showRiversProp, riverRenderLevelsProp, renderLevelsProp;
        private SerializedProperty showWireframeProp, debugLevelsProp, debugColorsProp, showRiverGizmosProp, riverDebugLevelsProp;

        private void OnEnable()
        {
            seedProp = serializedObject.FindProperty("Seed");
            useCacheProp = serializedObject.FindProperty("UseCache");
            mapSizeProp = serializedObject.FindProperty("MapSize");
            terrainHeightScaleProp = serializedObject.FindProperty("TerrainHeightScale");

            baseTempProp = serializedObject.FindProperty("BaseTemperature");
            tempHeightProp = serializedObject.FindProperty("TempHeightImpact");
            baseMoistureProp = serializedObject.FindProperty("BaseMoisture");
            rainIntProp = serializedObject.FindProperty("RainIntensity");
            fluxThresholdProp = serializedObject.FindProperty("RiverFluxThreshold");

            showSettlementsProp = serializedObject.FindProperty("ShowSettlements");
            globalPopScalarProp = serializedObject.FindProperty("GlobalPopScalar");
            minPopOutpostProp = serializedObject.FindProperty("MinPopOutpost");
            minPopTownProp = serializedObject.FindProperty("MinPopTown");
            minPopMetropolisProp = serializedObject.FindProperty("MinPopMetropolis");
            minSiteQualityProp = serializedObject.FindProperty("MinSiteQuality");
            townSpawnChanceProp = serializedObject.FindProperty("TownSpawnChance");
            outpostSpawnChanceProp = serializedObject.FindProperty("OutpostSpawnChance");

            useAutoLodProp = serializedObject.FindProperty("UseAutoLOD");
            levelsProp = serializedObject.FindProperty("Levels"); 

            showRiversProp = serializedObject.FindProperty("ShowRivers");
            renderLevelsProp = serializedObject.FindProperty("RenderLevels");
            riverRenderLevelsProp = serializedObject.FindProperty("RiverRenderLevels");

            showWireframeProp = serializedObject.FindProperty("ShowWireframe");
            debugLevelsProp = serializedObject.FindProperty("DebugLevels");
            debugColorsProp = serializedObject.FindProperty("DebugColors");
            showRiverGizmosProp = serializedObject.FindProperty("ShowRiverGizmos");
            riverDebugLevelsProp = serializedObject.FindProperty("RiverDebugLevels");
        }

        public override void OnInspectorGUI()
        {
            MapGeneratorBootstrap bootstrap = (MapGeneratorBootstrap)target;
            serializedObject.Update();
            
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 11, margin = new RectOffset(0, 0, 12, 4), alignment = TextAnchor.MiddleLeft
            };
            var bgStyle = EditorStyles.helpBox;

            EditorGUI.BeginChangeCheck();

            // 1. WORLD CORE
            DrawSection("CORE", headerStyle, bgStyle, () => {
                EditorGUILayout.PropertyField(seedProp);
                // Dice
                var r = GUILayoutUtility.GetLastRect(); 
                if (GUI.Button(new Rect(r.width - 25, r.y, 45, r.height), "Dice")) {
                     seedProp.intValue = Random.Range(0, 99999); GUI.FocusControl(null); 
                }
                
                EditorGUILayout.PropertyField(useCacheProp);
                EditorGUILayout.PropertyField(mapSizeProp);
                EditorGUILayout.PropertyField(terrainHeightScaleProp);
            });

            // 2. SIMULATION
            DrawSection("SIMULATION PARAMETERS", headerStyle, bgStyle, () => {
                EditorGUILayout.LabelField("Climate", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(baseTempProp);
                EditorGUILayout.PropertyField(tempHeightProp);
                EditorGUILayout.PropertyField(baseMoistureProp);
                
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Hydrology", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(rainIntProp);
                EditorGUILayout.PropertyField(fluxThresholdProp);
                
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Civilization", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(showSettlementsProp);
                if (showSettlementsProp.boolValue)
                {
                    EditorGUILayout.PropertyField(globalPopScalarProp);
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Spawn Chances (Filters)", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(minSiteQualityProp);
                    EditorGUILayout.PropertyField(townSpawnChanceProp);
                    EditorGUILayout.PropertyField(outpostSpawnChanceProp);
                    
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Thresholds", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(minPopOutpostProp);
                    EditorGUILayout.PropertyField(minPopTownProp);
                    EditorGUILayout.PropertyField(minPopMetropolisProp);
                }
            });

            // 3. MAP HIERARCHY (Профили)
            DrawSection("MAP HIERARCHY", headerStyle, bgStyle, () => {
                EditorGUILayout.PropertyField(useAutoLodProp);
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(levelsProp, true); 
            });

            // 4. RENDERING & DEBUG
            DrawSection("VISIBILITY & DEBUG", headerStyle, bgStyle, () => {
                
                // Terrain Layers
                EditorGUI.BeginDisabledGroup(useAutoLodProp.boolValue);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Terrain");
                DrawCompactLevelMask(renderLevelsProp);
                EditorGUILayout.EndHorizontal();
                
                // Rivers
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(showRiversProp, GUIContent.none, GUILayout.Width(20));
                EditorGUILayout.LabelField("Rivers", GUILayout.Width(50));
                if(showRiversProp.boolValue) DrawCompactLevelMask(riverRenderLevelsProp);
                EditorGUILayout.EndHorizontal();
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(8);
                EditorGUILayout.LabelField("Gizmos (Editor Only)", EditorStyles.miniBoldLabel);

                // Wireframe
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(showWireframeProp, GUIContent.none, GUILayout.Width(20));
                EditorGUILayout.LabelField("Wireframe", GUILayout.Width(80));
                if (showWireframeProp.boolValue) {
                     DrawCompactLevelMask(debugLevelsProp);
                }
                EditorGUILayout.EndHorizontal();
                
                // River Gizmos
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(showRiverGizmosProp, GUIContent.none, GUILayout.Width(20));
                EditorGUILayout.LabelField("River Graph", GUILayout.Width(80));
                if (showRiverGizmosProp.boolValue) {
                    DrawCompactLevelMask(riverDebugLevelsProp); // <-- Теперь эта переменная существует и отрисовывается
                }
                EditorGUILayout.EndHorizontal();
            });
            
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
                if (Application.isPlaying && GUIUtility.hotControl == 0) bootstrap.ResetVisualization();
            }
            
            GUILayout.Space(10);
            if (GUILayout.Button("FORCE REBUILD", GUILayout.Height(30))) 
                 if (Application.isPlaying) bootstrap.ResetVisualization();
        }

        private void DrawSection(string title, GUIStyle style, GUIStyle boxStyle, System.Action content)
        {
            GUILayout.Space(5);
            GUILayout.Label(title, style);
            EditorGUILayout.BeginVertical(boxStyle);
            content.Invoke();
            EditorGUILayout.EndVertical();
        }

        private void DrawCompactLevelMask(SerializedProperty listProp) {
            if (listProp == null || !listProp.isArray) return;
            var originalIndent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
            for (int i = 0; i < listProp.arraySize; i++) {
                var el = listProp.GetArrayElementAtIndex(i);
                el.boolValue = EditorGUILayout.ToggleLeft($"L{i}", el.boolValue, GUILayout.Width(35));
            }
            EditorGUI.indentLevel = originalIndent;
        }
    }
}