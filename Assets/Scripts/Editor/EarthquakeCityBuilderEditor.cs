using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="EarthquakeCityBuilder"/> that adds a button to auto-populate
/// the building model list from the project's Kenney city-kit FBX files, plus Build/Clear buttons.
/// </summary>
[CustomEditor(typeof(EarthquakeCityBuilder))]
public class EarthquakeCityBuilderEditor : Editor
{
    // Only pull from the FBX copies so we don't add the OBJ/GLB duplicates of the same building.
    private const string ModelsRoot = "Assets/Models";
    private const string FbxFolderMarker = "/FBX format/";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Auto-Find Building Models"))
        {
            AutoFindBuildingModels();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build City"))
            {
                var builder = (EarthquakeCityBuilder)target;
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Build City");
                builder.Build();
                MarkSceneDirty();
            }

            if (GUILayout.Button("Clear City"))
            {
                var builder = (EarthquakeCityBuilder)target;
                Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Clear City");
                builder.Clear();
                MarkSceneDirty();
            }
        }
    }

    private void AutoFindBuildingModels()
    {
        var models = new List<Object>();
        foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelsRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains(FbxFolderMarker))
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            // Whole buildings only: skip detail props, fences, trees, paths, low-detail LODs.
            if (!name.StartsWith("building"))
            {
                continue;
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null)
            {
                models.Add(model);
            }
        }

        models.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        SerializedProperty prop = serializedObject.FindProperty("buildingModels");
        prop.arraySize = models.Count;
        for (int i = 0; i < models.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
        }
        serializedObject.ApplyModifiedProperties();

        Debug.Log($"EarthquakeCityBuilder: assigned {models.Count} building models.");
    }

    private static void MarkSceneDirty()
    {
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
