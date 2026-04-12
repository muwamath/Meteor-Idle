using System.Collections.Generic;
using UnityEngine;

public class BayManager : MonoBehaviour
{
    [SerializeField] private DroneBay bayPrefab;
    [SerializeField] private CollectorDrone dronePrefab;
    [SerializeField] private Collector collector;
    [SerializeField] private int bayCount = 2;
    [SerializeField] private float bayY = -8.26f;
    [SerializeField] private float[] bayXPositions = { -7f, 7f };

    [SerializeField] private DroneUpgradePanel upgradePanel;
    [SerializeField] private MeteorSpawner meteorSpawner;

    [SerializeField] private DroneStats droneStats;
    [SerializeField] private BayStats bayStats;

    private readonly List<DroneBay> bays = new List<DroneBay>();

    private void Start()
    {
        if (bayPrefab == null) { Debug.LogError("[BayManager] bayPrefab not assigned", this); return; }
        for (int i = 0; i < bayCount && i < bayXPositions.Length; i++)
        {
            var pos = new Vector3(bayXPositions[i], bayY, 0f);
            var bay = Instantiate(bayPrefab, pos, Quaternion.identity, transform);
            bays.Add(bay);
            bay.Clicked += HandleBayClicked;
            if (collector != null) bay.SetCollectorPosition(collector.Position);
            if (bayStats != null) bay.SetReloadSpeed(bayStats.reloadSpeed.CurrentValue);
            SpawnDroneFor(bay);
        }

        if (bayStats != null) bayStats.OnChanged += OnBayStatsChanged;
        if (droneStats != null) droneStats.OnChanged += OnDroneStatsChanged;
    }

    private void OnDestroy()
    {
        if (bayStats != null) bayStats.OnChanged -= OnBayStatsChanged;
        if (droneStats != null) droneStats.OnChanged -= OnDroneStatsChanged;
    }

    private void OnBayStatsChanged()
    {
        float speed = bayStats != null ? bayStats.reloadSpeed.CurrentValue : 1f;
        int targetDrones = bayStats != null ? Mathf.RoundToInt(bayStats.dronesPerBay.CurrentValue) : 1;
        foreach (var bay in bays)
        {
            bay.SetReloadSpeed(speed);
            if (!bay.gameObject.activeInHierarchy) continue;
            int current = bay.GetComponentsInChildren<CollectorDrone>(false).Length;
            for (int i = current; i < targetDrones; i++)
                SpawnDroneFor(bay);
        }
    }

    private void OnDroneStatsChanged()
    {
        float thrust = droneStats != null ? droneStats.thrust.CurrentValue : 4f;
        float battery = droneStats != null ? droneStats.batteryCapacity.CurrentValue : 60f;
        int cargo = droneStats != null ? Mathf.RoundToInt(droneStats.cargoCapacity.CurrentValue) : 1;
        foreach (var bay in bays)
        {
            foreach (var drone in bay.GetComponentsInChildren<CollectorDrone>(true))
            {
                drone.Initialize(
                    env: bay,
                    thrust: thrust, damping: 1f,
                    batteryCapacity: battery, cargoCapacity: cargo,
                    reserveThresholdFraction: 0.3f,
                    pickupRadius: 0.35f, dockRadius: 0.45f);
                drone.SetMeteorSpawner(meteorSpawner);
                if (collector != null) drone.SetCollector(collector);
            }
        }
    }

    private void SpawnDroneFor(DroneBay bay)
    {
        if (dronePrefab == null) return;
        var drone = Instantiate(dronePrefab, bay.transform.position, Quaternion.identity, bay.transform);
        float thrust = droneStats != null ? droneStats.thrust.CurrentValue : 4f;
        float battery = droneStats != null ? droneStats.batteryCapacity.CurrentValue : 60f;
        int cargo = droneStats != null ? Mathf.RoundToInt(droneStats.cargoCapacity.CurrentValue) : 1;
        drone.Initialize(
            env: bay,
            thrust: thrust,
            damping: 1f,
            batteryCapacity: battery,
            cargoCapacity: cargo,
            reserveThresholdFraction: 0.3f,
            pickupRadius: 0.35f,
            dockRadius: 0.45f);
        drone.SetMeteorSpawner(meteorSpawner);
        if (collector != null) drone.SetCollector(collector);
    }

    private void HandleBayClicked(DroneBay bay)
    {
        if (upgradePanel != null) upgradePanel.Toggle();
    }

    public DroneStats DroneStats => droneStats;
    public BayStats BayStats => bayStats;
}
