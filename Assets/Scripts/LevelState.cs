using System;
using UnityEngine;

public class LevelState : MonoBehaviour
{
    public static LevelState Instance { get; private set; }

    [Header("Threshold Curve")]
    [SerializeField] private float baseCost = 10f;
    [SerializeField] private float growthRate = 1.08f;

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

    public int CurrentLevel => currentLevel;
    public int CurrentBlock => (currentLevel - 1) / 10;
    public int LevelInBlock => (currentLevel - 1) % 10 + 1;
    public bool IsBossLevel => LevelInBlock == 10;

    public int Threshold => IsBossLevel ? 0 : Mathf.RoundToInt(baseCost * Mathf.Pow(growthRate, currentLevel - 1));

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Called by GameManager after adding money. Returns true if the level
    /// advances (money was at or above threshold and it's not a boss level).
    /// </summary>
    public bool TryAdvance(int currentMoney)
    {
        if (IsBossLevel) return false;
        int threshold = Threshold;
        if (currentMoney < threshold) return false;

        currentLevel++;
        OnLevelChanged?.Invoke();
        return true;
    }

    public void NotifyBossSpawned()
    {
        OnBossSpawned?.Invoke();
    }

    public void BossDefeated()
    {
        currentLevel++;
        OnLevelChanged?.Invoke();
    }

    public void BossFailed()
    {
        int blockStart = CurrentBlock * 10 + 1;
        currentLevel = blockStart;
        OnBossFailed?.Invoke();
        OnLevelChanged?.Invoke();
    }
}
