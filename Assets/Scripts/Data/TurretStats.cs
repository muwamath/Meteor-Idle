using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatId
{
    FireRate = 0,
    MissileSpeed = 1,
    Damage = 2,
    Accuracy = 3,
    BlastRadius = 4,
}

[CreateAssetMenu(fileName = "TurretStats", menuName = "Meteor Idle/Turret Stats")]
public class TurretStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public StatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1.6f;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
    }

    public Stat fireRate   = new Stat { id = StatId.FireRate,    displayName = "Fire Rate",    baseValue = 0.5f, perLevelAdd = 0.15f, baseCost = 10 };
    public Stat missileSpeed = new Stat { id = StatId.MissileSpeed, displayName = "Missile Speed", baseValue = 4f,   perLevelAdd = 0.6f,  baseCost = 15 };
    public Stat damage     = new Stat { id = StatId.Damage,      displayName = "Damage",       baseValue = 1f,   perLevelAdd = 1f,    baseCost = 20 };
    public Stat accuracy   = new Stat { id = StatId.Accuracy,    displayName = "Accuracy",     baseValue = 0.5f, perLevelAdd = 0.04f, baseCost = 25 };
    public Stat blastRadius = new Stat { id = StatId.BlastRadius, displayName = "Blast Radius", baseValue = 0.10f, perLevelAdd = 0.25f, baseCost = 40 };

    public event Action OnChanged;

    public Stat Get(StatId id)
    {
        switch (id)
        {
            case StatId.FireRate: return fireRate;
            case StatId.MissileSpeed: return missileSpeed;
            case StatId.Damage: return damage;
            case StatId.Accuracy: return accuracy;
            case StatId.BlastRadius: return blastRadius;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return fireRate;
        yield return missileSpeed;
        yield return damage;
        yield return accuracy;
        yield return blastRadius;
    }

    public void ApplyUpgrade(StatId id)
    {
        var stat = Get(id);
        if (stat == null) return;
        stat.level++;
        OnChanged?.Invoke();
    }

    public void ResetRuntime()
    {
        foreach (var s in All()) s.level = 0;
        OnChanged?.Invoke();
    }
}
