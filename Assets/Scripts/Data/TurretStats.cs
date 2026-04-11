using System;
using System.Collections.Generic;
using UnityEngine;

public enum StatId
{
    FireRate = 0,
    RotationSpeed = 1,
    MissileSpeed = 2,
    Damage = 3,
    BlastRadius = 4,
    Homing = 5,
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

    // Launcher stats
    public Stat fireRate      = new Stat { id = StatId.FireRate,      displayName = "Fire Rate",      baseValue = 0.5f, perLevelAdd = 0.15f, baseCost = 10 };
    public Stat rotationSpeed = new Stat { id = StatId.RotationSpeed, displayName = "Rotation Speed", baseValue = 30f,  perLevelAdd = 15f,   baseCost = 12 };

    // Missile stats
    public Stat missileSpeed = new Stat { id = StatId.MissileSpeed, displayName = "Missile Speed", baseValue = 4f,    perLevelAdd = 0.6f,  baseCost = 15 };
    public Stat damage       = new Stat { id = StatId.Damage,       displayName = "Damage",        baseValue = 1f,    perLevelAdd = 1f,    baseCost = 20 };
    public Stat blastRadius  = new Stat { id = StatId.BlastRadius,  displayName = "Blast Radius",  baseValue = 0.10f, perLevelAdd = 0.25f, baseCost = 40 };
    public Stat homing       = new Stat { id = StatId.Homing,       displayName = "Homing",        baseValue = 0f,    perLevelAdd = 30f,   baseCost = 35 };

    public event Action OnChanged;

    public Stat Get(StatId id)
    {
        switch (id)
        {
            case StatId.FireRate: return fireRate;
            case StatId.RotationSpeed: return rotationSpeed;
            case StatId.MissileSpeed: return missileSpeed;
            case StatId.Damage: return damage;
            case StatId.BlastRadius: return blastRadius;
            case StatId.Homing: return homing;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return fireRate;
        yield return rotationSpeed;
        yield return missileSpeed;
        yield return damage;
        yield return blastRadius;
        yield return homing;
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

    // Sum of every upgrade already purchased on this instance. Mirrors the
    // NextCost formula so a sell refund equals the exact money the player
    // paid into this turret's levels.
    public int TotalSpentOnUpgrades()
    {
        int total = 0;
        foreach (var s in All())
        {
            for (int lv = 0; lv < s.level; lv++)
                total += Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, lv));
        }
        return total;
    }
}
