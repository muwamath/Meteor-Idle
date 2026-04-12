using System;
using System.Collections.Generic;
using UnityEngine;

public enum BayStatId
{
    ReloadSpeed = 0,
    DronesPerBay = 1,
}

[CreateAssetMenu(fileName = "BayStats", menuName = "Meteor Idle/Bay Stats")]
public class BayStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public BayStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1.6f;
        public int maxLevel;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
        public bool IsMaxed => maxLevel > 0 && level >= maxLevel;
    }

    public Stat reloadSpeed  = new Stat { id = BayStatId.ReloadSpeed,  displayName = "Reload Speed",   baseValue = 1f, perLevelAdd = 0.25f, baseCost = 1, costGrowth = 1f, maxLevel = 0 };
    public Stat dronesPerBay = new Stat { id = BayStatId.DronesPerBay, displayName = "Drones Per Bay", baseValue = 1f, perLevelAdd = 1f,    baseCost = 1, costGrowth = 1f, maxLevel = 2 };

    public event Action OnChanged;

    public Stat Get(BayStatId id)
    {
        switch (id)
        {
            case BayStatId.ReloadSpeed: return reloadSpeed;
            case BayStatId.DronesPerBay: return dronesPerBay;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return reloadSpeed;
        yield return dronesPerBay;
    }

    public void ApplyUpgrade(BayStatId id)
    {
        var stat = Get(id);
        if (stat == null) return;
        if (stat.IsMaxed) return;
        stat.level++;
        OnChanged?.Invoke();
    }

    public void ResetRuntime()
    {
        foreach (var s in All()) s.level = 0;
        OnChanged?.Invoke();
    }

    public int TotalSpentOnUpgrades()
    {
        int total = 0;
        foreach (var s in All())
            for (int lv = 0; lv < s.level; lv++)
                total += Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, lv));
        return total;
    }
}
