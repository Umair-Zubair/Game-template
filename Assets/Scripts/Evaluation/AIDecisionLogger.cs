using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Records one CSV row per AI decision tick for per-decision analysis and ML training.
/// Saved to Logs/Evaluation/ai_decisions_*.csv
///
/// Key ML training column: HitDealtInNext3s — 1 if the boss damaged the player
/// within 3 seconds of this decision, 0 otherwise. Rows are buffered for 3s before writing.
/// </summary>
public class AIDecisionLogger : MonoBehaviour
{
    public static AIDecisionLogger Instance { get; private set; }

    [Header("Debug")]
    public bool DebugMode = false;

    // ---- References ----
    private AIDecisionEngine decisionEngine;
    private Health           playerHealth;
    private BossController   bossController;

    // ---- Fight state ----
    private bool  fightActive;
    private int   fightId;
    private float fightStartTime;
    private int   decisionIdx;

    // ---- Pending decisions (buffered 3s for reward signal) ----
    private struct PendingDecision
    {
        public int    fightId;
        public int    idx;
        public float  timeSinceFightStart;
        public float  evaluatedAt;
        public string playerStyle;
        public float  playerHPPct;
        public float  bossHPPct;
        public float  distance;
        public float  attackFreq;
        public float  jumpFreq;
        public float  blockRate;
        public float  aggressionScore;
        public string source;
        public string action;
        public float  confidence;
        public int    fairnessActive;
    }

    private Queue<PendingDecision> pending = new Queue<PendingDecision>();
    private List<float> bossHitTimes = new List<float>();

    // ---- CSV ----
    private string csvPath;
    private const float RewardWindow = 3f;

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
        string filename = $"ai_decisions_{pktNow:yyyyMMdd_HHmmss}_PKT.csv";
        csvPath = Path.GetFullPath(Path.Combine(logsFolder, filename));
        WriteHeader();
        Debug.Log($"[AIDecisionLogger] CSV: {csvPath}");
    }

    private void Start()
    {
        decisionEngine = FindFirstObjectByType<AIDecisionEngine>();
        bossController = FindFirstObjectByType<BossController>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerHealth = playerObj.GetComponent<Health>();

        if (decisionEngine != null) decisionEngine.OnDecisionEvaluated += HandleDecision;
        if (playerHealth   != null) playerHealth.OnDamageTaken         += HandlePlayerDamaged;
        if (bossController != null) bossController.OnDied              += HandleBossDied;
    }

    private void Update()
    {
        if (pending.Count == 0) return;
        while (pending.Count > 0 && Time.time - pending.Peek().evaluatedAt >= RewardWindow)
            FlushOldestPending();
    }

    private void OnDestroy()
    {
        FlushAll();
        if (decisionEngine != null) decisionEngine.OnDecisionEvaluated -= HandleDecision;
        if (playerHealth   != null) playerHealth.OnDamageTaken         -= HandlePlayerDamaged;
        if (bossController != null) bossController.OnDied              -= HandleBossDied;
    }

    private void OnApplicationQuit() => FlushAll();

    // =========================================================
    // Fight Lifecycle
    // =========================================================

    public void StartFight()
    {
        if (fightActive) return;
        fightActive    = true;
        fightId++;
        fightStartTime = Time.time;
        decisionIdx    = 0;
        bossHitTimes.Clear();
        if (DebugMode) Debug.Log($"[AIDecisionLogger] Fight #{fightId} started.");
    }

    public void EndFight(bool bossWon)
    {
        if (!fightActive) return;
        fightActive = false;
        FlushAll();
        bossHitTimes.Clear();
        if (DebugMode) Debug.Log($"[AIDecisionLogger] Fight #{fightId} ended.");
    }

    // =========================================================
    // Event Handlers
    // =========================================================

    private void HandleDecision(GameContext ctx, BossDecision decision)
    {
        if (!fightActive) return;
        decisionIdx++;

        float playerHPPct = playerHealth != null && playerHealth.MaxHealth > 0
            ? playerHealth.currentHealth / playerHealth.MaxHealth : 0f;
        float bossHPPct = ctx.bossHealthNormalized;

        pending.Enqueue(new PendingDecision
        {
            fightId            = fightId,
            idx                = decisionIdx,
            timeSinceFightStart = Time.time - fightStartTime,
            evaluatedAt        = Time.time,
            playerStyle        = ctx.currentPlayerStyle.ToString(),
            playerHPPct        = playerHPPct,
            bossHPPct          = bossHPPct,
            distance           = ctx.distanceToPlayer,
            attackFreq         = ctx.playerProfile.attackFrequency,
            jumpFreq           = ctx.playerProfile.jumpFrequency,
            blockRate          = ctx.playerProfile.blockRate,
            aggressionScore    = ctx.playerProfile.aggressionScore,
            source             = decision.source ?? "Unknown",
            action             = decision.action.ToString(),
            confidence         = decision.confidence,
            fairnessActive     = (decisionEngine != null && decisionEngine.IsFairnessActive) ? 1 : 0
        });
    }

    private void HandlePlayerDamaged(float damage)
    {
        bossHitTimes.Add(Time.time);
    }

    private void HandleBossDied() => EndFight(false);

    // =========================================================
    // Flush
    // =========================================================

    private void FlushOldestPending()
    {
        if (pending.Count == 0) return;
        var rec = pending.Dequeue();
        int hit = 0;
        foreach (float t in bossHitTimes)
            if (t >= rec.evaluatedAt && t <= rec.evaluatedAt + RewardWindow) { hit = 1; break; }
        WriteRow(rec, hit);
    }

    private void FlushAll()
    {
        while (pending.Count > 0) FlushOldestPending();
    }

    // =========================================================
    // CSV
    // =========================================================

    private void WriteHeader()
    {
        File.WriteAllText(csvPath,
            "FightID,DecisionIdx,Time_s," +
            "PlayerStyle,PlayerHP_Pct,BossHP_Pct,Distance," +
            "AttackFreq_Hz,JumpFreq_Hz,BlockRate,AggressionScore," +
            "Source,Action,Confidence,FairnessActive," +
            "HitDealtInNext3s\n");
    }

    private void WriteRow(PendingDecision r, int hit)
    {
        var sb = new StringBuilder();
        sb.Append(r.fightId).Append(',');
        sb.Append(r.idx).Append(',');
        sb.Append(r.timeSinceFightStart.ToString("F2")).Append(',');
        sb.Append(r.playerStyle).Append(',');
        sb.Append(r.playerHPPct.ToString("F3")).Append(',');
        sb.Append(r.bossHPPct.ToString("F3")).Append(',');
        sb.Append(r.distance.ToString("F2")).Append(',');
        sb.Append(r.attackFreq.ToString("F3")).Append(',');
        sb.Append(r.jumpFreq.ToString("F3")).Append(',');
        sb.Append(r.blockRate.ToString("F3")).Append(',');
        sb.Append(r.aggressionScore.ToString("F3")).Append(',');
        sb.Append(r.source).Append(',');
        sb.Append(r.action).Append(',');
        sb.Append(r.confidence.ToString("F3")).Append(',');
        sb.Append(r.fairnessActive).Append(',');
        sb.Append(hit);
        sb.AppendLine();
        File.AppendAllText(csvPath, sb.ToString());
    }
}
