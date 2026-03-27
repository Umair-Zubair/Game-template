using UnityEngine;
using System.IO;
using System.Collections.Generic;

public enum AITestCondition
{
    Layer1Only,
    Layer1Plus2,
    Layer1Plus2Plus3
}

/// <summary>
/// Records per-fight metrics to CSV for evaluation of the adaptive AI system.
///
/// Metrics captured per fight:
///   - Fight duration, win/loss outcome
///   - Damage dealt / taken / efficiency (boss perspective)
///   - Action counts and strategy diversity (distinct action types used)
///   - Layer attribution % (Layer 1 heuristic vs Layer 2 weighted)
///   - Strategy weight shift (total |W_end - W_start| across all style/action pairs)
///   - Style change count + dominant player style detected
///   - Reaction speed: seconds from player style change to first different boss decision
///
/// Setup:
///   1. Attach to any persistent GameObject in the scene (e.g. the boss or a manager).
///   2. Set AICondition in the Inspector before each test run ("Layer1Only" or "Layer1+2").
///   3. CSV is written to Application.persistentDataPath — check the Console on Start for the path.
/// </summary>
public class AISessionLogger : MonoBehaviour
{
    public static AISessionLogger Instance { get; private set; }

    [Header("Test Configuration")]
    [Tooltip("Select the AI configuration being tested. Change before each run.")]
    [SerializeField] private AITestCondition aiCondition = AITestCondition.Layer1Plus2;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- References (auto-found) ----
    private AIDecisionEngine          decisionEngine;
    private VoidbornAdaptationManager adaptationManager;
    private PlayerBehaviorTracker     behaviorTracker;
    private Health                    playerHealth;
    private BossController            bossController;

    // ---- Fight state ----
    private bool  fightActive;
    private float fightStartTime;
    private int   fightId;

    // ---- Action / layer counters ----
    private int meleeCount;
    private int artilleryCount;
    private int chaseCount;
    private int layer1Count;
    private int layer2Count;
    private int layer3Count;
    private HashSet<BossActionType> actionsUsed = new HashSet<BossActionType>();

    // ---- Weight snapshot (for shift computation) ----
    private Dictionary<PlayerStyle, Dictionary<BossActionType, float>> weightsAtFightStart;

    // ---- Reaction speed ----
    private float         styleChangeTime     = -1f;
    private BossActionType decisionAtStyleChange;
    private List<float>   reactionSpeeds      = new List<float>();

    // ---- Style tracking ----
    private Dictionary<PlayerStyle, int> styleSamples = new Dictionary<PlayerStyle, int>();
    private int styleChangeCount;

    // ---- Damage baseline (tracker is cumulative — subtract at fight start) ----
    private float damageDealtAtStart;
    private float damageTakenAtStart;

    // ---- CSV file path ----
    private string csvPath;

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        string logsFolder = Path.Combine(Application.dataPath, "..", "Logs", "Evaluation");
        Directory.CreateDirectory(logsFolder);

        // Use Pakistan Standard Time (UTC+5) for consistent naming
        System.TimeZoneInfo pkt = System.TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
        System.DateTime pktNow  = System.TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.UtcNow, pkt);
        string filename = $"ai_eval_{pktNow:yyyyMMdd_HHmmss}_PKT.csv";
        csvPath = Path.GetFullPath(Path.Combine(logsFolder, filename));
        WriteHeader();
        Debug.Log($"[AISessionLogger] CSV: {csvPath}");
    }

    private void Start()
    {
        decisionEngine    = FindFirstObjectByType<AIDecisionEngine>();
        adaptationManager = FindFirstObjectByType<VoidbornAdaptationManager>();
        behaviorTracker   = FindFirstObjectByType<PlayerBehaviorTracker>();
        bossController    = FindFirstObjectByType<BossController>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerHealth = playerObj.GetComponent<Health>();

        if (decisionEngine    != null) decisionEngine.OnDecisionChanged += HandleDecisionChanged;
        if (adaptationManager != null) adaptationManager.OnStyleChanged  += HandleStyleChanged;
        if (playerHealth      != null) playerHealth.OnDamageTaken        += HandlePlayerDamage;
        if (bossController    != null) bossController.OnDied             += HandleBossDied;

        Debug.Log($"[AISessionLogger] Ready — boss={bossController != null}, player={playerHealth != null}, engine={decisionEngine != null}");
    }

    private void OnDestroy()
    {
        if (fightActive) EndFight(false); // safety flush on scene unload

        if (decisionEngine    != null) decisionEngine.OnDecisionChanged -= HandleDecisionChanged;
        if (adaptationManager != null) adaptationManager.OnStyleChanged  -= HandleStyleChanged;
        if (playerHealth      != null) playerHealth.OnDamageTaken        -= HandlePlayerDamage;
        if (bossController    != null) bossController.OnDied             -= HandleBossDied;
    }

    private void OnApplicationQuit()
    {
        if (fightActive)
            EndFight(false);
    }

    // =========================================================
    // Fight Lifecycle  (called by VoidbornGoddessController)
    // =========================================================

    /// <summary>
    /// Call once when the boss first detects the player and the fight begins.
    /// </summary>
    public void StartFight()
    {
        if (fightActive) return;
        fightActive = true;
        fightId++;
        fightStartTime = Time.time;

        // Reset all counters
        meleeCount = artilleryCount = chaseCount = 0;
        layer1Count = layer2Count = layer3Count  = 0;
        actionsUsed.Clear();
        reactionSpeeds.Clear();
        styleSamples.Clear();
        styleChangeCount = 0;
        styleChangeTime  = -1f;

        // Snapshot weights so we can measure drift at fight end
        weightsAtFightStart = SnapshotWeights();

        // Snapshot damage baseline (PlayerBehaviorTracker accumulates across fights)
        if (behaviorTracker != null)
        {
            damageDealtAtStart = behaviorTracker.Profile.totalDamageDealt;
            damageTakenAtStart = behaviorTracker.Profile.totalDamageTaken;
        }

        if (DebugMode) Debug.Log($"[AISessionLogger] === Fight #{fightId} started ({aiCondition}) ===");
    }

    /// <summary>
    /// Call when the fight ends (boss or player dies).
    /// bossWon = true means the boss killed the player.
    /// </summary>
    public void EndFight(bool bossWon)
    {
        if (!fightActive) return;
        fightActive = false;

        float duration = Time.time - fightStartTime;

        // Damage this fight only
        float dealt = 0f, taken = 0f;
        if (behaviorTracker != null)
        {
            dealt = behaviorTracker.Profile.totalDamageDealt - damageDealtAtStart;
            taken = behaviorTracker.Profile.totalDamageTaken - damageTakenAtStart;
        }
        float efficiency = taken > 0f ? dealt / taken : dealt;

        // Layer attribution %
        int totalDecisions = layer1Count + layer2Count + layer3Count;
        float l1pct = totalDecisions > 0 ? (float)layer1Count / totalDecisions * 100f : 0f;
        float l2pct = totalDecisions > 0 ? (float)layer2Count / totalDecisions * 100f : 0f;
        float l3pct = totalDecisions > 0 ? (float)layer3Count / totalDecisions * 100f : 0f;

        WriteRow(
            fightId, aiCondition.ToString(), duration, bossWon,
            meleeCount, artilleryCount, chaseCount,
            dealt, taken, efficiency,
            actionsUsed.Count, l1pct, l2pct, l3pct,
            ComputeWeightShift(), styleChangeCount,
            AverageList(reactionSpeeds), GetDominantStyle()
        );

        if (DebugMode)
            Debug.Log($"[AISessionLogger] Fight #{fightId} | BossWon={bossWon} | " +
                      $"Duration={duration:F1}s | Efficiency={efficiency:F2} | " +
                      $"WeightShift={ComputeWeightShift():F4} | " +
                      $"L1={l1pct:F0}% L2={l2pct:F0}%");
    }

    // =========================================================
    // Public: called by AIDecisionEngine on action start
    // =========================================================

    /// <summary>
    /// Called by AIDecisionEngine.OnBossActionStarted to log which action was taken and which layer chose it.
    /// </summary>
    public void RecordAction(BossActionType action, string layer)
    {
        if (!fightActive) return;

        actionsUsed.Add(action);

        switch (action)
        {
            case BossActionType.MeleeAttack:     meleeCount++;     break;
            case BossActionType.ArtilleryAttack: artilleryCount++; break;
            case BossActionType.Chase:           chaseCount++;     break;
        }

        if (layer == null) return;

        if      (layer.StartsWith("Heuristic")) layer1Count++;
        else if (layer.StartsWith("Weighted"))  layer2Count++;
        else                                    layer3Count++;
    }

    // =========================================================
    // Event Handlers
    // =========================================================

    private void HandleStyleChanged(PlayerStyle from, PlayerStyle to)
    {
        if (!fightActive) return;

        styleChangeCount++;
        styleChangeTime = Time.time;

        // Record what decision the boss was making just before the style changed
        if (decisionEngine != null)
            decisionAtStyleChange = decisionEngine.CurrentDecision.action;

        // Track style distribution for dominant style computation
        if (!styleSamples.ContainsKey(to)) styleSamples[to] = 0;
        styleSamples[to]++;

        if (DebugMode) Debug.Log($"[AISessionLogger] Style change #{styleChangeCount}: {from} → {to}");
    }

    private void HandleDecisionChanged(BossDecision decision)
    {
        if (!fightActive || styleChangeTime < 0f) return;

        // If the boss picked a different action after a style change, record reaction speed
        if (decision.action != decisionAtStyleChange)
        {
            float reactionSpeed = Time.time - styleChangeTime;
            reactionSpeeds.Add(reactionSpeed);
            styleChangeTime = -1f; // Reset — wait for next style change

            if (DebugMode) Debug.Log($"[AISessionLogger] Reaction speed: {reactionSpeed:F2}s");
        }
    }

    private void HandleBossDied()
    {
        EndFight(false); // player won — boss died
    }

    private void HandlePlayerDamage(float damage)
    {
        if (!fightActive || playerHealth == null) return;

        if (playerHealth.currentHealth <= 0)
            EndFight(true); // boss won — player died
    }

    // =========================================================
    // Helpers
    // =========================================================

    private Dictionary<PlayerStyle, Dictionary<BossActionType, float>> SnapshotWeights()
    {
        if (decisionEngine == null) return null;

        var snapshot = new Dictionary<PlayerStyle, Dictionary<BossActionType, float>>();
        foreach (PlayerStyle style in System.Enum.GetValues(typeof(PlayerStyle)))
        {
            var w = decisionEngine.GetWeightsForStyle(style);
            if (w != null)
                snapshot[style] = new Dictionary<BossActionType, float>(w);
        }
        return snapshot;
    }

    private float ComputeWeightShift()
    {
        if (weightsAtFightStart == null || decisionEngine == null) return 0f;

        float total = 0f;
        foreach (PlayerStyle style in System.Enum.GetValues(typeof(PlayerStyle)))
        {
            var current = decisionEngine.GetWeightsForStyle(style);
            if (current == null || !weightsAtFightStart.ContainsKey(style)) continue;

            foreach (var kvp in current)
            {
                if (weightsAtFightStart[style].TryGetValue(kvp.Key, out float startW))
                    total += Mathf.Abs(kvp.Value - startW);
            }
        }
        return total;
    }

    private float AverageList(List<float> list)
    {
        if (list.Count == 0) return -1f;
        float sum = 0f;
        foreach (float v in list) sum += v;
        return sum / list.Count;
    }

    private string GetDominantStyle()
    {
        if (styleSamples.Count == 0) return "None";
        PlayerStyle best = PlayerStyle.Balanced;
        int max = 0;
        foreach (var kvp in styleSamples)
            if (kvp.Value > max) { max = kvp.Value; best = kvp.Key; }
        return best.ToString();
    }

    // =========================================================
    // CSV
    // =========================================================

    private void WriteHeader()
    {
        File.WriteAllText(csvPath,
            "FightID,AICondition,Duration_s,BossWon," +
            "MeleeCount,ArtilleryCount,ChaseCount," +
            "DamageDealt,DamageTaken,DamageEfficiency," +
            "StrategyDiversity,L1_Pct,L2_Pct,L3_Pct," +
            "WeightShift,StyleChanges,AvgReactionSpeed_s,DominantStyle\n");
    }

    private void WriteRow(
        int id, string condition, float duration, bool bossWon,
        int melee, int artillery, int chase,
        float dealt, float taken, float efficiency,
        int diversity, float l1, float l2, float l3,
        float weightShift, int styleChanges, float reactionSpeed, string dominant)
    {
        File.AppendAllText(csvPath,
            $"{id},{condition},{duration:F2},{(bossWon ? 1 : 0)}," +
            $"{melee},{artillery},{chase}," +
            $"{dealt:F1},{taken:F1},{efficiency:F2}," +
            $"{diversity},{l1:F1},{l2:F1},{l3:F1}," +
            $"{weightShift:F4},{styleChanges},{reactionSpeed:F2},{dominant}\n");
    }
}
