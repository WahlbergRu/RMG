using UnityEditor;
using UnityEngine;
using VoronoiMapGen.Bootstrap;

[CustomEditor(typeof(MapGeneratorBootstrap))]
public class MapGeneratorBootstrapEditor : Editor
{
    // Properties
    private SerializedProperty seedProp;
    private SerializedProperty mapSizeProp;
    private SerializedProperty levelConfigsProp;
    
    // Rendering
    private SerializedProperty edgeWidthProp;
    private SerializedProperty roadWidthProp;
    private SerializedProperty roadColorProp;
    private SerializedProperty borderColorProp;
    private SerializedProperty drawRoadsProp;
    private SerializedProperty drawBordersProp;

    // Biome Colors
    private SerializedProperty oceanColorProp;
    private SerializedProperty coastColorProp;
    private SerializedProperty iceColorProp;
    private SerializedProperty desertColorProp;
    private SerializedProperty grasslandColorProp;
    private SerializedProperty forestColorProp;
    private SerializedProperty mountainColorProp;
    private SerializedProperty snowColorProp;

    private void OnEnable()
    {
        seedProp = serializedObject.FindProperty("Seed");
        mapSizeProp = serializedObject.FindProperty("MapSize");
        levelConfigsProp = serializedObject.FindProperty("LevelConfigs");

        edgeWidthProp = serializedObject.FindProperty("EdgeWidth");
        roadWidthProp = serializedObject.FindProperty("RoadWidth");
        roadColorProp = serializedObject.FindProperty("RoadColor");
        borderColorProp = serializedObject.FindProperty("BorderColor");
        drawRoadsProp = serializedObject.FindProperty("DrawRoads");
        drawBordersProp = serializedObject.FindProperty("DrawBorders");

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
        serializedObject.Update();

        // --- General Settings ---
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(seedProp);
        EditorGUILayout.PropertyField(mapSizeProp);
        EditorGUILayout.Space();

        // --- Level Configurations ---
        // Отрисовываем массив. Unity сама предоставит поле "Size" и элементы.
        // Заголовок берется из атрибута [Header] или названия переменной,
        // но так как мы рисуем PropertyField для всего массива, он будет выглядеть стандартно.
        EditorGUILayout.LabelField($"Multi-Level Settings (Total: {levelConfigsProp.arraySize})", EditorStyles.boldLabel);
        
        // true означает, что мы рисуем и дочерние элементы (раскрываем массив)
        EditorGUILayout.PropertyField(levelConfigsProp, new GUIContent("Levels Configuration"), true); 
        
        EditorGUILayout.Space();

        // --- Rendering Settings ---
        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(edgeWidthProp);
        EditorGUILayout.PropertyField(roadWidthProp);
        EditorGUILayout.PropertyField(roadColorProp);
        EditorGUILayout.PropertyField(borderColorProp);
        EditorGUILayout.PropertyField(drawRoadsProp);
        EditorGUILayout.PropertyField(drawBordersProp);
        EditorGUILayout.Space();

        // --- Biome Colors ---
        EditorGUILayout.LabelField("Biome Colors", EditorStyles.boldLabel);
        
        // Можно свернуть цвета в Foldout, чтобы не занимали много места
        bool showColors = EditorPrefs.GetBool("MapGen_ShowColors", true);
        showColors = EditorGUILayout.Foldout(showColors, "Biome Palette");
        if (showColors)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(oceanColorProp);
            EditorGUILayout.PropertyField(coastColorProp);
            EditorGUILayout.PropertyField(iceColorProp);
            EditorGUILayout.PropertyField(desertColorProp);
            EditorGUILayout.PropertyField(grasslandColorProp);
            EditorGUILayout.PropertyField(forestColorProp);
            EditorGUILayout.PropertyField(mountainColorProp);
            EditorGUILayout.PropertyField(snowColorProp);
            EditorGUI.indentLevel--;
        }
        EditorPrefs.SetBool("MapGen_ShowColors", showColors);

        serializedObject.ApplyModifiedProperties();
    }
}