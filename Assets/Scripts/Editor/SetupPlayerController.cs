#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class SetupPlayerController : MonoBehaviour
{
    [MenuItem("Tools/Auto-Upgrade Player Controller")]
    public static void UpgradePlayerController()
    {
        Debug.Log("Starting Player Controller Upgrade...");

        // 1. Ensure PlayerData exists
        PlayerData data = Resources.Load<PlayerData>("PlayerData");
        if (data == null)
        {
            if (!Directory.Exists(Application.dataPath + "/Resources"))
                Directory.CreateDirectory(Application.dataPath + "/Resources");

            data = ScriptableObject.CreateInstance<PlayerData>();
            AssetDatabase.CreateAsset(data, "Assets/Resources/PlayerData.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Created new PlayerData asset at Assets/Resources/PlayerData.asset");
        }
        else
        {
            Debug.Log("Found existing PlayerData, using that.");
        }

        // 2. Find Prefabs
        string[] ways = new string[] { 
            "Assets/Prefabs/Player/Knight.prefab", 
            "Assets/Prefabs/Player/Dragon.prefab", 
            "Assets/Prefabs/Player/Player.prefab" 
        };

        foreach (string path in ways)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                UpgradePrefab(prefab, path, data);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("<b>Upgrade Complete!</b> You can now Play.");
    }

    private static void UpgradePrefab(GameObject prefab, string path, PlayerData data)
    {
        GameObject instance = PrefabUtility.LoadPrefabContents(path);

        // Remove Old
        PlayerMovement old = instance.GetComponent<PlayerMovement>();
        if (old != null)
        {
            DestroyImmediate(old);
            Debug.Log($"Removed PlayerMovement from {prefab.name}");
        }

        // Remove Deprecated if present
        var deprecated = instance.GetComponent("PlayerMovement_DEPRECATED");
        if (deprecated != null)
        {
             DestroyImmediate(deprecated);
        }

        // Add New
        PlayerController controller = instance.GetComponent<PlayerController>();
        if (controller == null)
        {
            controller = instance.AddComponent<PlayerController>();
            Debug.Log($"Added PlayerController to {prefab.name}");
        }

        // Assign Data - use serialize object to modify fields safely if needed, but direct assignment works for editor scripts often
        // Only if field is public or we use finding
        SerializedObject so = new SerializedObject(controller);
        so.Update();
        SerializedProperty dataProp = so.FindProperty("data");
        if (dataProp != null)
        {
            dataProp.objectReferenceValue = data;
        }
        
        // Find layers
        SerializedProperty groundLayer = so.FindProperty("groundLayer");
        if (groundLayer != null) groundLayer.intValue = LayerMask.GetMask("Ground");

        SerializedProperty wallLayer = so.FindProperty("wallLayer");
        if (wallLayer != null) wallLayer.intValue = LayerMask.GetMask("Wall");

        so.ApplyModifiedProperties();

        // Remove "Missing Script" components (Crucial step since we renamed the file)
        int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(instance);
        if (missingCount > 0)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(instance);
            Debug.Log($"Removed {missingCount} missing scripts from {prefab.name}");
        }

        PrefabUtility.SaveAsPrefabAsset(instance, path);
        PrefabUtility.UnloadPrefabContents(instance);
    }
}
#endif
