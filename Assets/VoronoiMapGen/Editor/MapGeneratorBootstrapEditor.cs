using UnityEditor;
using UnityEngine;
using VoronoiMapGen.Bootstrap;
using VoronoiMapGen.Components;

[CustomEditor(typeof(MapGeneratorBootstrap))]
public class MapGeneratorBootstrapEditor : Editor
{
    private string[] _levelNames = System.Enum.GetNames(typeof(DetailLevel));

    private SerializedProperty oceanColorProp;
    private SerializedProperty coastColorProp;
    private SerializedProperty iceColorProp;
    private SerializedProperty desertColorProp;
    private SerializedProperty grasslandColorProp;
    private SerializedProperty forestColorProp;
    private SerializedProperty mountainColorProp;
    private SerializedProperty snowColorProp;
    private SerializedProperty levelConfigsProp;

    private void OnEnable()
    {
        oceanColorProp = serializedObject.FindProperty("oceanColor");
        coastColorProp = serializedObject.FindProperty("coastColor");
        iceColorProp = serializedObject.FindProperty("iceColor");
        desertColorProp = serializedObject.FindProperty("desertColor");
        grasslandColorProp = serializedObject.FindProperty("grasslandColor");
        forestColorProp = serializedObject.FindProperty("forestColor");
        mountainColorProp = serializedObject.FindProperty("mountainColor");
        snowColorProp = serializedObject.FindProperty("snowColor");
        levelConfigsProp = serializedObject.FindProperty("LevelConfigs"); 
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("Seed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MapSize"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Multi-Level Settings", EditorStyles.boldLabel);

        SerializedProperty levelsCountProp = serializedObject.FindProperty("LevelsCount");
        int oldLevelsCount = levelsCountProp.intValue;

        EditorGUILayout.PropertyField(levelsCountProp);
        int newLevelsCount = levelsCountProp.intValue;

        newLevelsCount = Mathf.Clamp(newLevelsCount, 1, 7);
        if (newLevelsCount != levelsCountProp.intValue)
        {
            levelsCountProp.intValue = newLevelsCount;
            EditorUtility.SetDirty(target);
        }

        if (levelConfigsProp != null)
        {
            EditorGUILayout.PropertyField(levelConfigsProp, true);
        }
        else
        {
            EditorGUILayout.HelpBox("LevelConfigs property not found.", MessageType.Error);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("EdgeWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadColor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BorderColor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("DrawRoads"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("DrawBorders"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Biome Colors", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("These colors are defined in the MapGeneratorBootstrap component and stored in MapSettings.", MessageType.Info);

        EditorGUILayout.PropertyField(oceanColorProp);
        EditorGUILayout.PropertyField(coastColorProp);
        EditorGUILayout.PropertyField(iceColorProp);
        EditorGUILayout.PropertyField(desertColorProp);
        EditorGUILayout.PropertyField(grasslandColorProp);
        EditorGUILayout.PropertyField(forestColorProp);
        EditorGUILayout.PropertyField(mountainColorProp);
        EditorGUILayout.PropertyField(snowColorProp);

        serializedObject.ApplyModifiedProperties();
    }

    private string GetLevelName(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= _levelNames.Length)
            return $"Custom Level {levelIndex}";

        return _levelNames[levelIndex];
    }
}