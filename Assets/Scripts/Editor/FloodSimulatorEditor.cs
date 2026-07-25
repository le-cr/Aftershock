using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="FloodSimulator"/> that adds a button to auto-assign the
/// WaterWorks water plane prefab, plus Play-mode Trigger/Stop buttons for quick testing.
/// </summary>
[CustomEditor(typeof(FloodSimulator))]
public class FloodSimulatorEditor : Editor
{
    private const string WaterPlanePath = "Assets/WaterWorks/Water_Plane.prefab";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Auto-Find Water Plane Prefab"))
        {
            AutoFindWaterPlanePrefab();
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Trigger Flood"))
            {
                ((FloodSimulator)target).TriggerFlood();
            }

            if (GUILayout.Button("Stop Flood"))
            {
                ((FloodSimulator)target).StopFlood();
            }
        }
    }

    private void AutoFindWaterPlanePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPlanePath);

        if (prefab == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("Water_Plane t:GameObject"))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null)
                {
                    break;
                }
            }
        }

        if (prefab == null)
        {
            Debug.LogWarning("FloodSimulatorEditor: could not find Water_Plane prefab. " +
                             "Is the WaterWorks asset imported?");
            return;
        }

        SerializedProperty prop = serializedObject.FindProperty("waterPlanePrefab");
        prop.objectReferenceValue = prefab;
        serializedObject.ApplyModifiedProperties();

        Debug.Log($"FloodSimulator: assigned water plane prefab '{prefab.name}'.");
    }
}
