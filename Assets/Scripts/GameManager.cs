using System;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private int startingMoney = 0;
    public int Money { get; private set; }

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
        OnMoneyChanged?.Invoke(Money);
    }

    public void AddMoney(int amount)
    {
        if (amount <= 0) return;
        Money += amount;
        OnMoneyChanged?.Invoke(Money);
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
}
