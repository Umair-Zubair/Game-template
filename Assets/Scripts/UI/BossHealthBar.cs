using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dark Souls-style boss health bar that appears at the bottom of the screen
/// when a boss fight is active. Auto-creates its own UI under the existing Canvas.
/// </summary>
public class BossHealthBar : MonoBehaviour
{
    [Header("Optional — auto-found if left empty")]
    [SerializeField] private BossController bossController;

    [Header("Visual Settings")]
    [SerializeField] private string bossDisplayName = "Voidborn Goddess";
    [SerializeField] private Color barColor = new Color(0.85f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color barBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    [SerializeField] private Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.75f);

    private Health bossHealth;
    private GameObject panelGO;
    private RectTransform fillRT;
    private bool wasActive;
    private bool uiBuilt;

    private void Start()
    {
        TryAcquireBoss();
        BuildUI();
    }

    private void Update()
    {
        if (!uiBuilt) return;

        if (bossController == null || bossHealth == null)
            TryAcquireBoss();

        bool shouldShow = bossController != null
                          && bossHealth != null
                          && !bossController.IsDead
                          && bossHealth.currentHealth > 0;

        if (shouldShow != wasActive)
        {
            panelGO.SetActive(shouldShow);
            wasActive = shouldShow;
        }

        if (!shouldShow || fillRT == null || bossHealth == null) return;

        float maxHP = bossHealth.MaxHealth;
        float ratio = maxHP > 0f ? bossHealth.currentHealth / maxHP : 0f;
        fillRT.anchorMax = new Vector2(ratio, 1f);
    }

    private void TryAcquireBoss()
    {
        if (bossController == null)
        {
            var all = FindObjectsByType<BossController>(FindObjectsSortMode.None);
            foreach (var bc in all)
            {
                if (bc.gameObject.activeInHierarchy)
                {
                    bossController = bc;
                    break;
                }
            }
        }

        if (bossController != null)
            bossHealth = bossController.Health != null
                ? bossController.Health
                : bossController.GetComponent<Health>();
    }

    private void BuildUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BossHealthBar] No Canvas found in scene.");
            return;
        }

        panelGO = new GameObject("BossHealthBarPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);
        panelGO.layer = LayerMask.NameToLayer("UI");

        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.15f, 0.03f);
        panelRT.anchorMax = new Vector2(0.85f, 0.11f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        panelGO.GetComponent<Image>().color = panelColor;

        // Boss name — above the bar
        GameObject nameGO = new GameObject("BossName", typeof(RectTransform), typeof(CanvasRenderer));
        nameGO.transform.SetParent(panelGO.transform, false);
        nameGO.layer = panelGO.layer;

        TextMeshProUGUI nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = bossDisplayName;
        nameTMP.fontSize = 28;
        nameTMP.alignment = TextAlignmentOptions.Center;
        nameTMP.color = new Color(0.9f, 0.85f, 0.7f);
        nameTMP.fontStyle = FontStyles.SmallCaps;

        RectTransform nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 1f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.pivot = new Vector2(0.5f, 0f);
        nameRT.anchoredPosition = new Vector2(0f, 4f);
        nameRT.sizeDelta = new Vector2(0f, 34f);

        // Bar background
        GameObject bgGO = new GameObject("BarBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGO.transform.SetParent(panelGO.transform, false);
        bgGO.layer = panelGO.layer;

        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.02f, 0.15f);
        bgRT.anchorMax = new Vector2(0.98f, 0.85f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        bgGO.GetComponent<Image>().color = barBackgroundColor;

        // Fill bar — anchored left, width controlled by anchorMax.x
        GameObject fillGO = new GameObject("BarFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGO.transform.SetParent(bgGO.transform, false);
        fillGO.layer = panelGO.layer;

        fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;

        fillGO.GetComponent<Image>().color = barColor;

        panelGO.SetActive(false);
        wasActive = false;
        uiBuilt = true;
    }
}
