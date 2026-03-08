using UnityEngine;

/// <summary>
/// Manages the custom game cursor.
/// Attach this to an object in your first scene.
/// </summary>
public class CursorManager : MonoBehaviour
{
    public static CursorManager Instance;

    [Header("Cursor Settings")]
    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Color cursorColor = Color.white;
    [SerializeField] private Vector2 hotSpot = Vector2.zero;
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        SetCustomCursor();
    }

    public void SetCustomCursor()
    {
        if (defaultCursor != null)
        {
            Texture2D tintedCursor = TintTexture(defaultCursor, cursorColor);
            Cursor.SetCursor(tintedCursor, hotSpot, cursorMode);
        }
        else
        {
            Debug.LogWarning("[CursorManager] No cursor texture assigned!");
        }
    }

    private Texture2D TintTexture(Texture2D source, Color tint)
    {
        if (tint == Color.white) return source;

        try
        {
            // Create a new texture so we don't mess up the original asset
            Texture2D newTex = new Texture2D(source.width, source.height, source.format, false);
            Color[] pixels = source.GetPixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixels[i] * tint;
            }

            newTex.SetPixels(pixels);
            newTex.Apply();
            return newTex;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CursorManager] Error tinting cursor: {e.Message}. \nMAKE SURE 'Read/Write Enabled' is checked in the Sprite's Import Settings!");
            return source;
        }
    }

    public void ResetCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, cursorMode);
    }
}
