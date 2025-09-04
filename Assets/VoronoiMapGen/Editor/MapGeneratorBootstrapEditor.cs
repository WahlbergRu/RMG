using UnityEditor;
using UnityEngine;
using VoronoiMapGen.Bootstrap;
using VoronoiMapGen.Components;

[CustomEditor(typeof(MapGeneratorBootstrap))]
public class MapGeneratorBootstrapEditor : Editor
{
    private string[] _levelNames = System.Enum.GetNames(typeof(DetailLevel));
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        // Отображаем стандартные поля
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Seed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MapSize"));
        
        // Multi-Level Settings
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Multi-Level Settings", EditorStyles.boldLabel);
        
        SerializedProperty levelsCountProp = serializedObject.FindProperty("LevelsCount");
        int oldLevelsCount = levelsCountProp.intValue;
        
        EditorGUILayout.PropertyField(levelsCountProp);
        int newLevelsCount = levelsCountProp.intValue;
        
        // Корректируем LevelsCount
        newLevelsCount = Mathf.Clamp(newLevelsCount, 1, 7);
        if (newLevelsCount != levelsCountProp.intValue)
        {
            levelsCountProp.intValue = newLevelsCount;
            EditorUtility.SetDirty(target);
        }
        
        // Отображаем настройки для каждого уровня
        for (int i = 0; i < newLevelsCount; i++)
        {
            DrawLevelSettingsGroup(i);
        }
        
        // Rendering Settings
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("EdgeWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("RoadColor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BorderColor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("DrawRoads"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("DrawBorders"));
        
        serializedObject.ApplyModifiedProperties();
    }
    
    private void DrawLevelSettingsGroup(int levelIndex)
    {
        EditorGUILayout.Space();
        
        // Заголовок группы
        string levelName = GetLevelName(levelIndex);
        EditorGUILayout.LabelField($"Level {levelIndex}: {levelName}", EditorStyles.boldLabel);
        
        EditorGUI.indentLevel++;
        
        // Site Count
        EditorGUILayout.IntField("Site Count", GetDefaultSiteCount(levelIndex));
        
        // Scale Factor
        EditorGUILayout.Slider(
            new GUIContent("Scale Factor"), 
            GetDefaultScaleFactor(levelIndex), 
            0.1f, 1.0f
        );
        
        // LOD Threshold
        EditorGUILayout.FloatField(
            new GUIContent("LOD Threshold"), 
            GetDefaultLODThreshold(levelIndex)
        );
        
        // Render Threshold
        EditorGUILayout.FloatField(
            new GUIContent("Render Threshold"), 
            GetDefaultRenderThreshold(levelIndex)
        );
        
        EditorGUI.indentLevel--;
    }
    
    private string GetLevelName(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= _levelNames.Length)
            return $"Custom Level {levelIndex}";
            
        return _levelNames[levelIndex];
    }
    
    // Значения по умолчанию для уровней
    private int GetDefaultSiteCount(int level)
    {
        int[] defaults = { 10, 50, 100, 300, 600, 1000, 2000 };
        return level < defaults.Length ? defaults[level] : 100;
    }
    
    private float GetDefaultScaleFactor(int level)
    {
        float[] defaults = { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f };
        return level < defaults.Length ? defaults[level] : 0.5f;
    }
    
    private float GetDefaultLODThreshold(int level)
    {
        float[] defaults = { 1000f, 500f, 200f, 100f, 50f, 20f, 5f };
        return level < defaults.Length ? defaults[level] : 10f;
    }
    
    private float GetDefaultRenderThreshold(int level)
    {
        float[] defaults = { 2000f, 1000f, 400f, 200f, 100f, 40f, 10f };
        return level < defaults.Length ? defaults[level] : 20f;
    }
}