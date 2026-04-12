using System;
using System.Collections.Generic;
using UnityEngine;

public enum DroneStatId
{
    Thrust = 0,
    BatteryCapacity = 1,
    CargoCapacity = 2,
}

[CreateAssetMenu(fileName = "DroneStats", menuName = "Meteor Idle/Drone Stats")]
public class DroneStats : ScriptableObject
{
    [Serializable]
    public class Stat
    {
        public DroneStatId id;
        public string displayName;
        public float baseValue;
        public float perLevelAdd;
        public int baseCost;
        public float costGrowth = 1.6f;
        [NonSerialized] public int level;

        public float CurrentValue => baseValue + perLevelAdd * level;
        public int NextCost => Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, level));
    }

    public Stat thrust          = new Stat { id = DroneStatId.Thrust,          displayName = "Thrust",           baseValue = 4f,  perLevelAdd = 1.0f, baseCost = 1, costGrowth = 1f };
    public Stat batteryCapacity = new Stat { id = DroneStatId.BatteryCapacity, displayName = "Battery Capacity", baseValue = 10f, perLevelAdd = 3f,   baseCost = 1, costGrowth = 1f };
    public Stat cargoCapacity   = new Stat { id = DroneStatId.CargoCapacity,   displayName = "Cargo Capacity",   baseValue = 1f,  perLevelAdd = 1f,   baseCost = 1, costGrowth = 1f };

    public event Action OnChanged;

    public Stat Get(DroneStatId id)
    {
        switch (id)
        {
            case DroneStatId.Thrust: return thrust;
            case DroneStatId.BatteryCapacity: return batteryCapacity;
            case DroneStatId.CargoCapacity: return cargoCapacity;
        }
        return null;
    }

    public IEnumerable<Stat> All()
    {
        yield return thrust;
        yield return batteryCapacity;
        yield return cargoCapacity;
    }

    public void ApplyUpgrade(DroneStatId id)
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

    public int TotalSpentOnUpgrades()
    {
        int total = 0;
        foreach (var s in All())
            for (int lv = 0; lv < s.level; lv++)
                total += Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, lv));
        return total;
    }
}
