using System;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class LevelState : MonoBehaviour
{
    public static LevelState Instance { get; private set; }

    [Header("Core Kill Threshold")]
    [SerializeField] private int baseCoreKills = 3;
    [SerializeField] private float coreKillGrowthExponent = 0.5f;

    [Header("Spawn Rate Scaling")]
    [SerializeField] private float level1InitialInterval = 15f;
    [SerializeField] private float level1MinInterval = 8f;
    [SerializeField] private float level150InitialInterval = 5f;
    [SerializeField] private float level150MinInterval = 2f;

    [Header("Meteor Size Scaling")]
    [SerializeField] private float level1SizeMin = 0.35f;
    [SerializeField] private float level1SizeMax = 0.6f;
    [SerializeField] private float level150SizeMin = 0.8f;
    [SerializeField] private float level150SizeMax = 1.2f;

    [Header("HP & Value Scaling")]
    [SerializeField] private float hpScalePerLevel = 0.02f;
    [SerializeField] private float valueScalePerLevel = 0.015f;

    [Header("Core Count Scaling")]
    [SerializeField] private int coreCountBonusEveryNLevels = 25;

    [Header("Boss")]
    [SerializeField] private float bossFallSpeed = 0.3f;
    [SerializeField] private float bossSize = 1.2f;

    [NonSerialized] private int currentLevel = 1;
    [NonSerialized] private int coreKillsThisBlock;

    public int CurrentLevel => currentLevel;
    public int CurrentBlock => (currentLevel - 1) / 10;
    public int LevelInBlock => (currentLevel - 1) % 10 + 1;
    public bool IsBossLevel => LevelInBlock == 10;

    public int Threshold => IsBossLevel ? 0 : Mathf.RoundToInt(baseCoreKills * Mathf.Pow(currentLevel, coreKillGrowthExponent));

    public int CoreKillsThisBlock => coreKillsThisBlock;
    public float CoreKillProgress => Threshold > 0 ? Mathf.Clamp01((float)coreKillsThisBlock / Threshold) : 0f;

    // 0 at level 1, 1 at level 150
    private float LevelT => Mathf.Clamp01((currentLevel - 1f) / 149f);

    public float SpawnInitialInterval => Mathf.Lerp(level1InitialInterval, level150InitialInterval, LevelT);
    public float SpawnMinInterval => Mathf.Lerp(level1MinInterval, level150MinInterval, LevelT);

    public (float min, float max) MeteorSizeRange =>
        (Mathf.Lerp(level1SizeMin, level150SizeMin, LevelT),
         Mathf.Lerp(level1SizeMax, level150SizeMax, LevelT));

    public float HpMultiplier => 1f + hpScalePerLevel * (currentLevel - 1);
    public float CoreValueMultiplier => 1f + valueScalePerLevel * (currentLevel - 1);
    public int CoreCountBonus => coreCountBonusEveryNLevels > 0
        ? (currentLevel - 1) / coreCountBonusEveryNLevels
        : 0;

    public float BossFallSpeed => bossFallSpeed;
    public float BossSize => bossSize;

    public event Action OnLevelChanged;
    public event Action OnBossSpawned;
    public event Action OnBossFailed;
    public event Action OnCoreKillRecorded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Called by Meteor when a core voxel's HP hits 0. Increments the per-block
    /// kill counter and auto-advances the level when the threshold is met.
    /// No-op on boss levels (boss advancement goes through BossDefeated).
    /// </summary>
    public void RecordCoreKill()
    {
        if (IsBossLevel) return;
        coreKillsThisBlock++;
        OnCoreKillRecorded?.Invoke();
        if (coreKillsThisBlock >= Threshold)
        {
            coreKillsThisBlock = 0;
            currentLevel++;
            OnLevelChanged?.Invoke();
        }
    }

    public void NotifyBossSpawned()
    {
        OnBossSpawned?.Invoke();
    }

    public void BossDefeated()
    {
        coreKillsThisBlock = 0;
        currentLevel++;
        OnLevelChanged?.Invoke();
    }

    public void BossFailed()
    {
        currentLevel = Mathf.Max(1, currentLevel - 2);
        coreKillsThisBlock = 0;
        OnBossFailed?.Invoke();
        OnLevelChanged?.Invoke();
    }
}
