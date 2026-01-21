#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class ForceUpdateData : MonoBehaviour
{
    [MenuItem("Tools/Force Update Jump to 10")]
    public static void UpdateJump()
    {
        PlayerData data = Resources.Load<PlayerData>("PlayerData");
        if (data != null)
        {
            data.jumpForce = 10f;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("<b>Fixed:</b> PlayerData Jump Force is now FORCED to 10.");
        }
        else
        {
            Debug.LogError("Could not find PlayerData in Resources!");
        }
    }
}
#endif
