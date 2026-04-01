using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Text;

public enum AITestCondition
{
    Layer1Only,
    Layer1Plus2,
    Layer1Plus2Plus3
}

/// <summary>
/// Records per-fight metrics to CSV for evaluation of the adaptive AI system.
/// Two CSVs are produced:
///   ai_eval_*.csv      — one row per fight (summary metrics)
///   ai_decisions_*.csv — one row per AI decision tick (per-decision log, via AIDecisionLogger)
/// </summary>
public class AISessionLogger : MonoBehaviour
{
    public static AISessionLogger Instance { get; private set; }

    [Header("Test Configuration")]
    [Tooltip("Select the AI configuration being tested.")]
    [SerializeField] private AITestCondition aiCondition = AITestCondition.Layer1Plus2;

    [Header("Debug")]
    public bool DebugMode = true;

    // ---- References ----
    private AIDecisionEngine          decisionEngine;
    private VoidbornAdaptationManager adaptationManager;
    private PlayerBehaviorTracker     behaviorTracker;
    private Health                    playerHealth;
    private Health                    bossHealth;
    private BossController            bossController;
    private PlayerShield              playerShield;
    private VoidbornGoddessController goddess;

    // ---- Fight state ----
    private bool  fightActive;
    private float fightStartTime;
    private int   fightId;

    // ---- Outcome ----
    private float playerHPMin;
    private bool  firstBossHitDone;
    private bool  firstPlayerHitDone;
    private float timeToFirstBossHit;
    private float timeToFirstPlayerHit;

    // ---- Action / layer counters ----
    private int meleeCount, artilleryCount, chaseCount;
    private int layer1Count, layer2Count, layer3Count;
    private int attacksAttempted, attacksLanded;
    private int playerBlockCount;
    private HashSet<BossActionType> actionsUsed = new HashSet<BossActionType>();
    private List<float> actionTimestamps = new List<float>();

    // ---- Decision engine baselines (snapshot at fight start, delta at end) ----
    private int   totalDecisionsAtStart;
    private float confidenceSumAtStart;
    private int   lowConfAtStart;
    private int   l2CountAtStart;

    // ---- Fairness ----
    private int fairnessActivations;

    // ---- Weight snapshot ----
    private Dictionary<PlayerStyle, Dictionary<BossActionType, float>> weightsAtStart;

    // ---- Reaction speed ----
    private float         styleChangeTime = -1f;
    private BossActionType decisionAtStyleChange;
    private List<float>   reactionSpeeds = new List<float>();

    // ---- Style tracking (time-based) ----
    private PlayerStyle currentStyle;
    private float       styleStartTime;
    private Dictionary<PlayerStyle, float> styleTimeAccum = new Dictionary<PlayerStyle, float>();
    private int         styleChangeCount;
    private List<string> styleTransitionLog = new List<string>();

    // ---- Damage baseline ----
    private float damageDealtAtStart;
    private float damageTakenAtStart;

    // ---- Distance ----
    private float distanceSum;
    private int   distanceSamples;
    private float timeAtCloseRange;

    // ---- CSV ----
    private string csvPath;

    // =========================================================
    // Unity Lifecycle
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        string logsFolder = Path.Combine(Application.dataPath, "Evaluation");
        Directory.CreateDirectory(logsFolder);

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
        goddess           = FindFirstObjectByType<VoidbornGoddessController>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerHealth = playerObj.GetComponent<Health>();
            playerShield = playerObj.GetComponent<PlayerShield>();
        }

        if (bossController != null) bossHealth = bossController.Health;

        if (decisionEngine    != null) decisionEngine.OnDecisionChanged   += HandleDecisionChanged;
        if (decisionEngine    != null) decisionEngine.OnFairnessActivated += HandleFairnessActivated;
        if (adaptationManager != null) adaptationManager.OnStyleChanged   += HandleStyleChanged;
        if (playerHealth      != null) playerHealth.OnDamageTaken         += HandlePlayerDamage;
        if (bossHealth        != null) bossHealth.OnDamageTaken           += HandleBossDamage;
        if (bossController    != null) bossController.OnDied              += HandleBossDied;
        if (playerShield      != null) playerShield.OnBlockStarted        += HandlePlayerBlock;
        if (goddess           != null)
        {
            goddess.OnMeleeAttempted    += () => { if (fightActive) attacksAttempted++; };
            goddess.OnMeleeResult       += (landed) => { if (fightActive && landed) attacksLanded++; };
            goddess.OnArtilleryLaunched += () => { if (fightActive) attacksAttempted++; };
        }

        Debug.Log($"[AISessionLogger] Ready — boss={bossController != null}, player={playerHealth != null}");
    }

    private void Update()
    {
        if (!fightActive || bossController == null) return;
        float dist = bossController.GetDistanceToPlayer();
        if (dist < Mathf.Infinity)
        {
            distanceSum += dist;
            distanceSamples++;
            if (dist <= bossController.attackRange)
                timeAtCloseRange += Time.deltaTime;
        }
    }

    private void OnDestroy()
    {
        if (fightActive) EndFight(false);
        UnsubscribeAll();
    }

    private void OnApplicationQuit()
    {
        if (fightActive) EndFight(false);
    }

    private void UnsubscribeAll()
    {
        if (decisionEngine    != null) { decisionEngine.OnDecisionChanged   -= HandleDecisionChanged; decisionEngine.OnFairnessActivated -= HandleFairnessActivated; }
        if (adaptationManager != null) adaptationManager.OnStyleChanged     -= HandleStyleChanged;
        if (playerHealth      != null) playerHealth.OnDamageTaken           -= HandlePlayerDamage;
        if (bossHealth        != null) bossHealth.OnDamageTaken             -= HandleBossDamage;
        if (bossController    != null) bossController.OnDied                -= HandleBossDied;
        if (playerShield      != null) playerShield.OnBlockStarted          -= HandlePlayerBlock;
    }

    // =========================================================
    // Fight Lifecycle
    // =========================================================

    public void StartFight()
    {
        if (fightActive) return;
        fightActive    = true;
        fightId++;
        fightStartTime = Time.time;

        meleeCount = artilleryCount = chaseCount = 0;
        layer1Count = layer2Count = layer3Count = 0;
        attacksAttempted = attacksLanded = playerBlockCount = 0;
        actionsUsed.Clear();
        actionTimestamps.Clear();
        reactionSpeeds.Clear();
        styleTransitionLog.Clear();
        styleChangeCount = 0;
        styleChangeTime  = -1f;
        fairnessActivations = 0;

        playerHPMin          = playerHealth != null ? playerHealth.currentHealth : 0f;
        firstBossHitDone     = firstPlayerHitDone = false;
        timeToFirstBossHit   = -1f;
        timeToFirstPlayerHit = -1f;

        styleTimeAccum.Clear();
        foreach (PlayerStyle s in System.Enum.GetValues(typeof(PlayerStyle)))
            styleTimeAccum[s] = 0f;
        currentStyle   = adaptationManager != null ? adaptationManager.CurrentStyle : PlayerStyle.Balanced;
        styleStartTime = Time.time;

        distanceSum = 0f; distanceSamples = 0; timeAtCloseRange = 0f;

        if (decisionEngine != null)
        {
            totalDecisionsAtStart = decisionEngine.TotalDecisions;
            confidenceSumAtStart  = decisionEngine.ConfidenceSum;
            lowConfAtStart        = decisionEngine.LowConfidenceCount;
            l2CountAtStart        = decisionEngine.L2DecisionCount;
        }

        weightsAtStart = SnapshotWeights();

        if (behaviorTracker != null)
        {
            damageDealtAtStart = behaviorTracker.Profile.totalDamageDealt;
            damageTakenAtStart = behaviorTracker.Profile.totalDamageTaken;
        }

        if (DebugMode) Debug.Log($"[AISessionLogger] === Fight #{fightId} started ({aiCondition}) ===");
    }

    public void EndFight(bool bossWon)
    {
        if (!fightActive) return;
        fightActive = false;

        float duration = Time.time - fightStartTime;

        // Finalize style timing
        if (styleTimeAccum.ContainsKey(currentStyle))
            styleTimeAccum[currentStyle] += Time.time - styleStartTime;
        float totalStyleTime = 0f;
        foreach (var v in styleTimeAccum.Values) totalStyleTime += v;

        // Outcome HP
        float playerHPEnd = playerHealth != null ? playerHealth.currentHealth : 0f;
        float bossHPEnd   = bossHealth   != null ? bossHealth.currentHealth   : 0f;
        float playerMaxHP = playerHealth != null && playerHealth.MaxHealth > 0 ? playerHealth.MaxHealth : 100f;
        float bossMaxHP   = bossHealth   != null && bossHealth.MaxHealth   > 0 ? bossHealth.MaxHealth   : 100f;
        float playerHPPct = playerHPEnd / playerMaxHP;
        float bossHPPct   = bossHPEnd   / bossMaxHP;
        float closeness   = 1f - Mathf.Abs(playerHPPct - bossHPPct);

        // Damage
        float dealt = 0f, taken = 0f;
        if (behaviorTracker != null)
        {
            dealt = behaviorTracker.Profile.totalDamageDealt - damageDealtAtStart;
            taken = behaviorTracker.Profile.totalDamageTaken - damageTakenAtStart;
        }
        float efficiency   = taken > 0f ? dealt / taken : dealt;
        float dpm          = duration > 0f ? dealt / (duration / 60f) : 0f;

        // Attack rates
        float hitRate = attacksAttempted > 0 ? (float)attacksLanded / attacksAttempted : 0f;

        // Pacing
        float longestPassive = ComputeLongestPassive();
        float avgInterval    = actionTimestamps.Count > 1
            ? (actionTimestamps[actionTimestamps.Count - 1] - actionTimestamps[0]) / (actionTimestamps.Count - 1)
            : -1f;

        // Style percentages
        float sPctAgg = StylePct(PlayerStyle.Aggressive, totalStyleTime);
        float sPctDef = StylePct(PlayerStyle.Defensive,  totalStyleTime);
        float sPctAer = StylePct(PlayerStyle.Aerial,     totalStyleTime);
        float sPctRng = StylePct(PlayerStyle.Ranged,     totalStyleTime);
        float sPctBal = StylePct(PlayerStyle.Balanced,   totalStyleTime);
        float styleEntropy = ShannonEntropy(new[] { sPctAgg, sPctDef, sPctAer, sPctRng, sPctBal });

        // Decision engine deltas
        int   totalDec  = 0;
        float avgConf   = 0f;
        int   lowConf   = 0;
        int   l2Dec     = 0;
        if (decisionEngine != null)
        {
            totalDec = decisionEngine.TotalDecisions    - totalDecisionsAtStart;
            float cs = decisionEngine.ConfidenceSum     - confidenceSumAtStart;
            avgConf  = totalDec > 0 ? cs / totalDec : 0f;
            lowConf  = decisionEngine.LowConfidenceCount - lowConfAtStart;
            l2Dec    = decisionEngine.L2DecisionCount    - l2CountAtStart;
        }

        int   totalLayered = layer1Count + layer2Count + layer3Count;
        float l1pct = totalLayered > 0 ? (float)layer1Count / totalLayered * 100f : 0f;
        float l2pct = totalLayered > 0 ? (float)layer2Count / totalLayered * 100f : 0f;
        float l3pct = totalLayered > 0 ? (float)layer3Count / totalLayered * 100f : 0f;

        // Weights
        var   endW        = SnapshotWeights();
        float weightShift = ComputeWeightShift(endW);
        float weightEntropy = ComputeWeightEntropy(endW);

        // Distance
        float avgDist  = distanceSamples > 0 ? distanceSum / distanceSamples : -1f;
        float pctClose = duration > 0f ? timeAtCloseRange / duration * 100f : 0f;

        var sb = new StringBuilder();
        sb.Append(fightId).Append(',');
        sb.Append(aiCondition).Append(',');
        sb.Append(duration.F2()).Append(',');
        sb.Append(bossWon ? 1 : 0).Append(',');
        // Outcome
        sb.Append(playerHPEnd.F1()).Append(',');
        sb.Append(bossHPEnd.F1()).Append(',');
        sb.Append(playerHPMin.F1()).Append(',');
        sb.Append(closeness.F3()).Append(',');
        // Damage
        sb.Append(dealt.F1()).Append(',');
        sb.Append(taken.F1()).Append(',');
        sb.Append(efficiency.F2()).Append(',');
        sb.Append(dpm.F1()).Append(',');
        // Attacks
        sb.Append(attacksAttempted).Append(',');
        sb.Append(attacksLanded).Append(',');
        sb.Append(hitRate.F3()).Append(',');
        sb.Append(playerBlockCount).Append(',');
        // Actions
        sb.Append(meleeCount).Append(',');
        sb.Append(artilleryCount).Append(',');
        sb.Append(chaseCount).Append(',');
        // Timing
        sb.Append(timeToFirstBossHit.F2()).Append(',');
        sb.Append(timeToFirstPlayerHit.F2()).Append(',');
        sb.Append(longestPassive.F2()).Append(',');
        sb.Append(avgInterval.F2()).Append(',');
        // Style %
        sb.Append(sPctAgg.F1()).Append(',');
        sb.Append(sPctDef.F1()).Append(',');
        sb.Append(sPctAer.F1()).Append(',');
        sb.Append(sPctRng.F1()).Append(',');
        sb.Append(sPctBal.F1()).Append(',');
        sb.Append(styleEntropy.F3()).Append(',');
        sb.Append(styleChangeCount).Append(',');
        sb.Append('"').Append(string.Join("|", styleTransitionLog)).Append('"').Append(',');
        sb.Append(DominantStyle()).Append(',');
        // Weights (5 styles x 3 actions = 15)
        foreach (PlayerStyle s in new[] { PlayerStyle.Aggressive, PlayerStyle.Defensive, PlayerStyle.Aerial, PlayerStyle.Ranged, PlayerStyle.Balanced })
            foreach (BossActionType a in new[] { BossActionType.MeleeAttack, BossActionType.ArtilleryAttack, BossActionType.Chase })
                sb.Append(W(endW, s, a).F4()).Append(',');
        sb.Append(weightShift.F4()).Append(',');
        sb.Append(weightEntropy.F3()).Append(',');
        // Strategy
        sb.Append(actionsUsed.Count).Append(',');
        sb.Append(l1pct.F1()).Append(',');
        sb.Append(l2pct.F1()).Append(',');
        sb.Append(l3pct.F1()).Append(',');
        // Decision engine
        sb.Append(totalDec).Append(',');
        sb.Append(avgConf.F3()).Append(',');
        sb.Append(lowConf).Append(',');
        sb.Append(l2Dec).Append(',');
        sb.Append(fairnessActivations).Append(',');
        // Spatial
        sb.Append(avgDist.F2()).Append(',');
        sb.Append(pctClose.F1()).Append(',');
        // Reaction
        sb.Append(AvgList(reactionSpeeds).F2());
        sb.AppendLine();

        File.AppendAllText(csvPath, sb.ToString());

        if (DebugMode)
            Debug.Log($"[AISessionLogger] Fight #{fightId} | BossWon={bossWon} | Duration={duration:F1}s | WeightShift={weightShift:F4}");
    }

    // =========================================================
    // Public: called by AIDecisionEngine
    // =========================================================

    public void RecordAction(BossActionType action, string layer)
    {
        if (!fightActive) return;
        actionsUsed.Add(action);
        actionTimestamps.Add(Time.time);

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
        if (styleTimeAccum.ContainsKey(from))
            styleTimeAccum[from] += Time.time - styleStartTime;
        currentStyle   = to;
        styleStartTime = Time.time;
        styleChangeCount++;
        styleTransitionLog.Add($"{from}->{to}");
        styleChangeTime = Time.time;
        if (decisionEngine != null) decisionAtStyleChange = decisionEngine.CurrentDecision.action;
        if (DebugMode) Debug.Log($"[AISessionLogger] Style: {from} → {to}");
    }

    private void HandleDecisionChanged(BossDecision decision)
    {
        if (!fightActive || styleChangeTime < 0f) return;
        if (decision.action != decisionAtStyleChange)
        {
            reactionSpeeds.Add(Time.time - styleChangeTime);
            styleChangeTime = -1f;
        }
    }

    private void HandlePlayerDamage(float damage)
    {
        if (!fightActive || playerHealth == null) return;
        if (!firstBossHitDone) { firstBossHitDone = true; timeToFirstBossHit = Time.time - fightStartTime; }
        float hp = playerHealth.currentHealth;
        if (hp < playerHPMin) playerHPMin = hp;
        if (hp <= 0) EndFight(true);
    }

    private void HandleBossDamage(float damage)
    {
        if (!fightActive || firstPlayerHitDone) return;
        firstPlayerHitDone   = true;
        timeToFirstPlayerHit = Time.time - fightStartTime;
    }

    private void HandleBossDied()     => EndFight(false);
    private void HandlePlayerBlock()  { if (fightActive) playerBlockCount++; }
    private void HandleFairnessActivated() { if (fightActive) fairnessActivations++; }

    // =========================================================
    // Helpers
    // =========================================================

    private float StylePct(PlayerStyle s, float total)
    {
        if (total <= 0f || !styleTimeAccum.ContainsKey(s)) return 0f;
        return styleTimeAccum[s] / total * 100f;
    }

    private string DominantStyle()
    {
        PlayerStyle best = PlayerStyle.Balanced;
        float max = -1f;
        foreach (var kvp in styleTimeAccum)
            if (kvp.Value > max) { max = kvp.Value; best = kvp.Key; }
        return best.ToString();
    }

    private float ComputeLongestPassive()
    {
        if (actionTimestamps.Count < 2) return -1f;
        float longest = 0f;
        for (int i = 1; i < actionTimestamps.Count; i++)
        {
            float gap = actionTimestamps[i] - actionTimestamps[i - 1];
            if (gap > longest) longest = gap;
        }
        return longest;
    }

    private Dictionary<PlayerStyle, Dictionary<BossActionType, float>> SnapshotWeights()
    {
        if (decisionEngine == null) return null;
        var snap = new Dictionary<PlayerStyle, Dictionary<BossActionType, float>>();
        foreach (PlayerStyle s in System.Enum.GetValues(typeof(PlayerStyle)))
        {
            var w = decisionEngine.GetWeightsForStyle(s);
            if (w != null) snap[s] = new Dictionary<BossActionType, float>(w);
        }
        return snap;
    }

    private float ComputeWeightShift(Dictionary<PlayerStyle, Dictionary<BossActionType, float>> endW)
    {
        if (weightsAtStart == null || endW == null) return 0f;
        float total = 0f;
        foreach (PlayerStyle s in System.Enum.GetValues(typeof(PlayerStyle)))
        {
            if (!endW.ContainsKey(s) || !weightsAtStart.ContainsKey(s)) continue;
            foreach (var kvp in endW[s])
                if (weightsAtStart[s].TryGetValue(kvp.Key, out float sw))
                    total += Mathf.Abs(kvp.Value - sw);
        }
        return total;
    }

    private float ComputeWeightEntropy(Dictionary<PlayerStyle, Dictionary<BossActionType, float>> weights)
    {
        if (weights == null) return 0f;
        var vals = new List<float>();
        foreach (var d in weights.Values)
            foreach (var v in d.Values)
                vals.Add(v);
        if (vals.Count == 0) return 0f;
        float maxV = float.MinValue;
        foreach (float v in vals) if (v > maxV) maxV = v;
        float sumExp = 0f;
        var exps = new List<float>();
        foreach (float v in vals) { float e = Mathf.Exp(v - maxV); exps.Add(e); sumExp += e; }
        float entropy = 0f;
        foreach (float e in exps) { float p = e / sumExp; if (p > 0f) entropy -= p * Mathf.Log(p, 2f); }
        return entropy;
    }

    private float ShannonEntropy(float[] pcts)
    {
        float total = 0f;
        foreach (float p in pcts) total += p;
        if (total <= 0f) return 0f;
        float h = 0f;
        foreach (float p in pcts) { float pr = p / total; if (pr > 0f) h -= pr * Mathf.Log(pr, 2f); }
        return h;
    }

    private float W(Dictionary<PlayerStyle, Dictionary<BossActionType, float>> w, PlayerStyle s, BossActionType a)
    {
        if (w == null || !w.TryGetValue(s, out var d)) return 0f;
        return d.TryGetValue(a, out float v) ? v : 0f;
    }

    private float AvgList(List<float> list)
    {
        if (list.Count == 0) return -1f;
        float sum = 0f;
        foreach (float v in list) sum += v;
        return sum / list.Count;
    }

    // =========================================================
    // CSV Header
    // =========================================================

    private void WriteHeader()
    {
        File.WriteAllText(csvPath,
            "FightID,AICondition,Duration_s,BossWon," +
            "PlayerHP_End,BossHP_End,PlayerHP_Min,ClosenessScore," +
            "DamageDealt,DamageTaken,DamageEfficiency,DamagePerMinute," +
            "AttacksAttempted,AttacksLanded,HitRate," +
            "PlayerBlockCount," +
            "MeleeCount,ArtilleryCount,ChaseCount," +
            "TimeToFirstBossHit_s,TimeToFirstPlayerHit_s," +
            "LongestPassivePeriod_s,AvgActionInterval_s," +
            "StylePct_Aggressive,StylePct_Defensive,StylePct_Aerial,StylePct_Ranged,StylePct_Balanced," +
            "StyleEntropy,StyleChanges,StyleTransitions,DominantStyle," +
            "W_Agg_Melee,W_Agg_Artillery,W_Agg_Chase," +
            "W_Def_Melee,W_Def_Artillery,W_Def_Chase," +
            "W_Aer_Melee,W_Aer_Artillery,W_Aer_Chase," +
            "W_Rng_Melee,W_Rng_Artillery,W_Rng_Chase," +
            "W_Bal_Melee,W_Bal_Artillery,W_Bal_Chase," +
            "WeightShift,WeightEntropy," +
            "StrategyDiversity,L1_Pct,L2_Pct,L3_Pct," +
            "TotalDecisions,AvgConfidence,LowConfidenceCount,L2DecisionCount,FairnessActivations," +
            "AvgDistance,PctTimeCloseRange," +
            "AvgReactionSpeed_s\n");
    }
}

// Extension methods for compact float formatting
internal static class FloatFmt
{
    public static string F1(this float f) => f.ToString("F1");
    public static string F2(this float f) => f.ToString("F2");
    public static string F3(this float f) => f.ToString("F3");
    public static string F4(this float f) => f.ToString("F4");
}
