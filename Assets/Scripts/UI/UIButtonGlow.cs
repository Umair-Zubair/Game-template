using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Adds a glow or highlight effect when the mouse hovers over a UI element.
/// Attach this to any button or selectable object.
/// </summary>
public class UIButtonGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Glow Settings")]
    [SerializeField] private GameObject glowOverlay; // A child object with a glow sprite
    [SerializeField] private bool useScaleEffect = true;
    [SerializeField] private float scaleMultiplier = 1.05f;
    [SerializeField] private float transitionSpeed = 10f;

    [Header("Audio")]
    [SerializeField] private AudioClip hoverSound;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private bool isHovered = false;

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        // Ensure glow is hidden at start
        if (glowOverlay != null)
            glowOverlay.SetActive(false);
    }

    private void Update()
    {
        // Smoothly scale the button
        if (useScaleEffect)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * transitionSpeed);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        
        if (glowOverlay != null)
            glowOverlay.SetActive(true);

        if (useScaleEffect)
            targetScale = originalScale * scaleMultiplier;

        if (hoverSound != null && SoundManager.instance != null)
            SoundManager.instance.PlaySound(hoverSound);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;

        if (glowOverlay != null)
            glowOverlay.SetActive(false);

        if (useScaleEffect)
            targetScale = originalScale;
    }

    private void OnDisable()
    {
        // Reset state if button is hidden
        if (glowOverlay != null)
            glowOverlay.SetActive(false);
            
        transform.localScale = originalScale;
        targetScale = originalScale;
    }
}
