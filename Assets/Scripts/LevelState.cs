using System;
using UnityEngine;

public class LevelState : MonoBehaviour
{
    public static LevelState Instance { get; private set; }

    [Header("Threshold Curve")]
    [SerializeField] private float baseCost = 10f;
    [SerializeField] private float growthRate = 1.08f;

    [NonSerialized] private int currentLevel = 1;

    public int CurrentLevel => currentLevel;
    public int CurrentBlock => (currentLevel - 1) / 10;
    public int LevelInBlock => (currentLevel - 1) % 10 + 1;
    public bool IsBossLevel => LevelInBlock == 10;

    public int Threshold => IsBossLevel ? 0 : Mathf.RoundToInt(baseCost * Mathf.Pow(growthRate, currentLevel - 1));

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
