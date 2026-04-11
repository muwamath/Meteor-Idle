using System;
using System.Collections.Generic;
using UnityEngine;

public enum RailgunStatId
{
    FireRate = 0,
    RotationSpeed = 1,
    Speed = 2,
    Weight = 3,
    Caliber = 4,
}

[CreateAssetMenu(fileName = "RailgunStats", menuName = "Meteor Idle/Railgun Stats")]
public class RailgunStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public RailgunStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1f;
        public int maxLevel; // 0 = uncapped
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
        public bool IsMaxed => maxLevel > 0 && level >= maxLevel;
    }

    public Stat fireRate      = new Stat { id = RailgunStatId.FireRate,      displayName = "Fire Rate",      baseValue = 0.2f, perLevelAdd = 0.05f, baseCost = 1, costGrowth = 1f };
    public Stat rotationSpeed = new Stat { id = RailgunStatId.RotationSpeed, displayName = "Rotation Speed", baseValue = 20f,  perLevelAdd = 12f,   baseCost = 1, costGrowth = 1f };
    public Stat speed         = new Stat { id = RailgunStatId.Speed,         displayName = "Speed",          baseValue = 6f,   perLevelAdd = 3f,    baseCost = 1, costGrowth = 1f };
    public Stat weight        = new Stat { id = RailgunStatId.Weight,        displayName = "Weight",         baseValue = 4f,   perLevelAdd = 2f,    baseCost = 1, costGrowth = 1f };
    public Stat caliber       = new Stat { id = RailgunStatId.Caliber,       displayName = "Caliber",        baseValue = 1f,   perLevelAdd = 1f,    baseCost = 1, costGrowth = 1f, maxLevel = 2 };

    public event Action OnChanged;

    public Stat Get(RailgunStatId id)
    {
        switch (id)
        {
            case RailgunStatId.FireRate:      return fireRate;
            case RailgunStatId.RotationSpeed: return rotationSpeed;
            case RailgunStatId.Speed:         return speed;
            case RailgunStatId.Weight:        return weight;
            case RailgunStatId.Caliber:       return caliber;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return fireRate;
        yield return rotationSpeed;
        yield return speed;
        yield return weight;
        yield return caliber;
    }

    public void ApplyUpgrade(RailgunStatId id)
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
}
