using System;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private int startingMoney = 0;
    [SerializeField] private CoreDrop coreDropPrefab;
    [SerializeField] private int coreDropPoolPrewarm = 10;
    public int Money { get; private set; }

    private SimplePool<CoreDrop> coreDropPool;

    public event Action<int> OnMoneyChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Money = startingMoney;
    }

    private void Start()
    {
        if (coreDropPrefab != null)
            coreDropPool = new SimplePool<CoreDrop>(coreDropPrefab, transform, coreDropPoolPrewarm);
        OnMoneyChanged?.Invoke(Money);
    }

    public CoreDrop GetPooledCoreDrop()
    {
        return coreDropPool?.Get();
    }

    public void ReleaseCoreDrop(CoreDrop drop)
    {
        if (drop == null) return;
        activeDrops.Remove(drop);
        coreDropPool?.Release(drop);
    }

    public void AddMoney(int amount)
    {
        if (amount <= 0) return;
        Money += amount;
        OnMoneyChanged?.Invoke(Money);

        // Iter 4: auto-advance level when money reaches threshold
        if (LevelState.Instance != null)
        {
            int threshold = LevelState.Instance.Threshold;
            if (threshold > 0 && LevelState.Instance.TryAdvance(Money))
            {
                Money -= threshold;
                OnMoneyChanged?.Invoke(Money);
            }
        }
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        OnMoneyChanged?.Invoke(Money);
        return true;
    }

    public void SetMoney(int value)
    {
        Money = Mathf.Max(0, value);
        OnMoneyChanged?.Invoke(Money);
    }

    // Iter 3: global registry of active CoreDrops. Drones iterate this list
    // to find targets. Drops register themselves when spawned by Meteor and
    // unregister when consumed or despawned.
    private readonly System.Collections.Generic.List<CoreDrop> activeDrops
        = new System.Collections.Generic.List<CoreDrop>();

    public System.Collections.Generic.IReadOnlyList<CoreDrop> ActiveDrops => activeDrops;

    public void RegisterDrop(CoreDrop drop)
    {
        if (drop == null) return;
        if (!activeDrops.Contains(drop)) activeDrops.Add(drop);
    }

    public void UnregisterDrop(CoreDrop drop)
    {
        if (drop == null) return;
        activeDrops.Remove(drop);
    }

    private void OnEnable()
    {
        if (LevelState.Instance != null)
            LevelState.Instance.OnBossFailed += ClearActiveDrops;
    }

    private void OnDisable()
    {
        if (LevelState.Instance != null)
            LevelState.Instance.OnBossFailed -= ClearActiveDrops;
    }

    private void ClearActiveDrops()
    {
        for (int i = activeDrops.Count - 1; i >= 0; i--)
        {
            var drop = activeDrops[i];
            if (drop != null) coreDropPool?.Release(drop);
        }
        activeDrops.Clear();
    }

    private void LateUpdate()
    {
        for (int i = activeDrops.Count - 1; i >= 0; i--)
        {
            var d = activeDrops[i];
            if (d == null || !d.IsAlive)
            {
                activeDrops.RemoveAt(i);
                if (d != null) coreDropPool?.Release(d);
            }
        }
    }
}
