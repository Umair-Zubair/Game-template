using UnityEngine;

/// <summary>
/// On-screen debug overlay for any boss using the adaptation system.
/// Shows player style, FSM state, player metrics, and AI Decision Engine info.
///
/// Attach to: Boss GameObject (same one with BossController + adaptation manager).
/// Toggle: Disable this component in the Inspector to hide the HUD.
/// </summary>
public class BossAIDebugHUD : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private Vector2 offset = new Vector2(10, 10);
    [SerializeField] private float panelWidth = 300f;

    // ---- References (auto-found) ----
    private BossController boss;
    private IBossAdaptationManager adaptationManager;
    private AIDecisionEngine decisionEngine;
    private PlayerBehaviorTracker tracker;

    // ---- Styles (built once) ----
    private GUIStyle labelStyle;
    private GUIStyle headerStyle;
    private GUIStyle valueStyle;
    private Texture2D bgTexture;
    private bool stylesBuilt = false;

    // ---- Style colors ----
    private static readonly Color AggressiveColor = new Color(1f, 0.3f, 0.3f);
    private static readonly Color DefensiveColor  = new Color(0.3f, 1f, 0.3f);
    private static readonly Color BalancedColor   = new Color(0.4f, 0.7f, 1f);
    private static readonly Color AerialColor     = new Color(1f, 1f, 0.3f);
    private static readonly Color RangedColor     = new Color(1f, 0.6f, 0.2f);
    private static readonly Color FairnessColor   = new Color(1f, 0.4f, 0.8f);
    private static readonly Color EngineColor     = new Color(0.3f, 1f, 0.9f);

    private void Awake()
    {
        boss = GetComponent<BossController>();
        adaptationManager = GetComponent<IBossAdaptationManager>();
        decisionEngine = GetComponent<AIDecisionEngine>();
    }

    private void Start()
    {
        tracker = FindFirstObjectByType<PlayerBehaviorTracker>();
    }

    private void Update()
    {
        if (tracker == null)
            tracker = FindFirstObjectByType<PlayerBehaviorTracker>();
    }

    private void BuildStyles()
    {
        bgTexture = new Texture2D(1, 1);
        bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
        bgTexture.Apply();

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };

        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight,
            normal = { textColor = Color.white }
        };

        stylesBuilt = true;
    }

    private void OnGUI()
    {
        if (boss == null || adaptationManager == null) return;

        if (!stylesBuilt)
            BuildStyles();

        PlayerStyle style = adaptationManager.CurrentStyle;

        // Strip class name noise: "VoidbornChaseState" → "Chase"
        string fsmState = boss.StateMachine?.CurrentState?.GetType().Name ?? "None";
        fsmState = fsmState.Replace("Voidborn", "").Replace("Boss", "").Replace("Enemy", "").Replace("State", "");

        PlayerProfile profile = tracker != null ? tracker.Profile : new PlayerProfile();

        // Count rows dynamically based on whether engine is attached
        bool hasEngine = decisionEngine != null;
        int lineCount = 7; // header + 6 base rows
        if (hasEngine) lineCount += 4; // separator + active layer + decision + fairness

        // ---- Layout ----
        float lineHeight = 22f;
        float padding = 10f;
        float panelHeight = (lineCount * lineHeight) + (padding * 2) + 4f;

        Rect panelRect = new Rect(Screen.width - panelWidth - offset.x, offset.y, panelWidth, panelHeight);
        GUI.DrawTexture(panelRect, bgTexture);

        float x = panelRect.x + padding;
        float y = panelRect.y + padding;
        float labelW = panelWidth * 0.55f;
        float valueW = panelWidth * 0.40f;

        GUI.Label(new Rect(x, y, panelWidth - padding * 2, lineHeight), "─ Boss AI ─", headerStyle);
        y += lineHeight + 2f;

        DrawRow(ref y, x, lineHeight, labelW, valueW, "Style:",      style.ToString().ToUpper(),                    GetStyleColor(style));
        DrawRow(ref y, x, lineHeight, labelW, valueW, "FSM State:",  fsmState,                                      Color.white);
        DrawRow(ref y, x, lineHeight, labelW, valueW, "Aggression:", profile.aggressionScore.ToString("F2"),        Color.white);
        DrawRow(ref y, x, lineHeight, labelW, valueW, "Jump Freq:",  profile.jumpFrequency.ToString("F1") + "/s",   Color.white);
        DrawRow(ref y, x, lineHeight, labelW, valueW, "Atk Freq:",   profile.attackFrequency.ToString("F1") + "/s", Color.white);
        DrawRow(ref y, x, lineHeight, labelW, valueW, "Distance:",   profile.averageDistance.ToString("F1"),        Color.white);

        // ---- AI Decision Engine section ----
        if (hasEngine)
        {
            y += 4f; // Small gap
            GUI.Label(new Rect(x, y, panelWidth - padding * 2, lineHeight), "─ Decision Engine ─", headerStyle);
            y += lineHeight + 2f;

            BossDecision decision = decisionEngine.CurrentDecision;
            string layerName = decisionEngine.ActiveLayer;
            string actionName = decision.action.ToString();

            DrawRow(ref y, x, lineHeight, labelW, valueW, "Active Layer:", layerName,                              EngineColor);
            DrawRow(ref y, x, lineHeight, labelW, valueW, "Decision:",     actionName + $" ({decision.confidence:F2})", Color.white);

            // Fairness Guardian status
            string fairnessStatus = decisionEngine.IsFairnessActive ? "ACTIVE" : "Off";
            Color fairnessStatusColor = decisionEngine.IsFairnessActive ? FairnessColor : Color.gray;
            DrawRow(ref y, x, lineHeight, labelW, valueW, "Fairness:",     fairnessStatus, fairnessStatusColor);
        }
    }

    private void DrawRow(ref float y, float x, float lineHeight, float labelW, float valueW,
                         string label, string value, Color valueColor)
    {
        GUI.Label(new Rect(x, y, labelW, lineHeight), label, labelStyle);
        var coloredValue = new GUIStyle(valueStyle) { normal = { textColor = valueColor } };
        GUI.Label(new Rect(x + labelW, y, valueW, lineHeight), value, coloredValue);
        y += lineHeight;
    }

    private Color GetStyleColor(PlayerStyle style)
    {
        switch (style)
        {
            case PlayerStyle.Aggressive: return AggressiveColor;
            case PlayerStyle.Defensive:  return DefensiveColor;
            case PlayerStyle.Balanced:   return BalancedColor;
            case PlayerStyle.Aerial:     return AerialColor;
            case PlayerStyle.Ranged:     return RangedColor;
            default:                     return Color.white;
        }
    }
}
