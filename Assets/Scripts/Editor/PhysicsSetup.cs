#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class PhysicsSetup : MonoBehaviour
{
    // This allows us to call it from PlayerController or automatically
    public static PhysicsMaterial2D GetZeroFriction()
    {
        PhysicsMaterial2D material = Resources.Load<PhysicsMaterial2D>("ZeroFriction");
        if (material == null)
        {
            if (!Directory.Exists(Application.dataPath + "/Resources"))
                Directory.CreateDirectory(Application.dataPath + "/Resources");

            material = new PhysicsMaterial2D("ZeroFriction");
            material.friction = 0f;
            material.bounciness = 0f;
            AssetDatabase.CreateAsset(material, "Assets/Resources/ZeroFriction.physicsMaterial2D");
            AssetDatabase.SaveAssets();
            Debug.Log("Created ZeroFriction PhysicsMaterial2D");
        }
        return material;
    }
}
#endif
