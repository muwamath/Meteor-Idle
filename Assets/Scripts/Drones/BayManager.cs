using System.Collections.Generic;
using UnityEngine;

public class BayManager : MonoBehaviour
{
    [SerializeField] private DroneBay bayPrefab;
    [SerializeField] private CollectorDrone dronePrefab;
    [SerializeField] private int bayCount = 3;
    [SerializeField] private float bayY = -8.26f;
    [SerializeField] private float bayStartX = 6f;
    [SerializeField] private float baySpacing = 2.5f;
    [SerializeField] private int prebuiltIndex = 0;
    [SerializeField] private int[] bayBuildCosts = { 200, 600 };

    [SerializeField] private DroneUpgradePanel upgradePanel;
    [SerializeField] private BuildBayPanel buildPanel;
    [SerializeField] private MeteorSpawner meteorSpawner;

    [SerializeField] private DroneStats droneStats;
    [SerializeField] private BayStats bayStats;

    private int purchasedCount;
    private readonly List<DroneBay> bays = new List<DroneBay>();

    private void Start()
    {
        if (bayPrefab == null) { Debug.LogError("[BayManager] bayPrefab not assigned", this); return; }
        for (int i = 0; i < bayCount; i++)
        {
            var pos = new Vector3(bayStartX + i * baySpacing, bayY, 0f);
            var bay = Instantiate(bayPrefab, pos, Quaternion.identity, transform);
            bays.Add(bay);
            bay.Clicked += HandleBayClicked;
            if (i == prebuiltIndex)
            {
                SpawnDroneFor(bay);
            }
            else
            {
                bay.gameObject.SetActive(false);
            }
        }

        if (bayStats != null) bayStats.OnChanged += OnBayStatsChanged;
    }

    private void OnDestroy()
    {
        if (bayStats != null) bayStats.OnChanged -= OnBayStatsChanged;
    }

    private void OnBayStatsChanged()
    {
        int targetDrones = bayStats != null ? Mathf.RoundToInt(bayStats.dronesPerBay.CurrentValue) : 1;
        foreach (var bay in bays)
        {
            if (!bay.gameObject.activeInHierarchy) continue;
            int current = bay.GetComponentsInChildren<CollectorDrone>(false).Length;
            for (int i = current; i < targetDrones; i++)
                SpawnDroneFor(bay);
        }
    }

    private void SpawnDroneFor(DroneBay bay)
    {
        if (dronePrefab == null) return;
        var drone = Instantiate(dronePrefab, bay.transform.position, Quaternion.identity, bay.transform);
        float thrust = droneStats != null ? droneStats.thrust.CurrentValue : 4f;
        float battery = droneStats != null ? droneStats.batteryCapacity.CurrentValue : 10f;
        int cargo = droneStats != null ? Mathf.RoundToInt(droneStats.cargoCapacity.CurrentValue) : 1;
        drone.Initialize(
            env: bay,
            thrust: thrust,
            damping: 1f,
            batteryCapacity: battery,
            cargoCapacity: cargo,
            reserveThresholdFraction: 0.4f,
            pickupRadius: 0.35f,
            dockRadius: 0.45f);
        drone.SetMeteorSpawner(meteorSpawner);
    }

    public int NextBuildCost()
    {
        if (bayBuildCosts == null || bayBuildCosts.Length == 0) return 0;
        if (purchasedCount < bayBuildCosts.Length) return bayBuildCosts[purchasedCount];
        int overflow = purchasedCount - bayBuildCosts.Length + 2;
        return bayBuildCosts[bayBuildCosts.Length - 1] * overflow;
    }

    private void HandleBayClicked(DroneBay bay)
    {
        bool hasDrone = bay.GetComponentInChildren<CollectorDrone>(true) != null;
        if (hasDrone)
        {
            if (upgradePanel != null) upgradePanel.Toggle();
        }
        else
        {
            if (buildPanel != null) buildPanel.Open(bay, NextBuildCost, OnConfirmBuild);
        }
    }

    private void OnConfirmBuild(DroneBay bay)
    {
        int cost = NextBuildCost();
        if (GameManager.Instance == null || !GameManager.Instance.TrySpend(cost)) return;
        bay.gameObject.SetActive(true);
        SpawnDroneFor(bay);
        purchasedCount++;
        if (buildPanel != null) buildPanel.Close();
    }

    public DroneStats DroneStats => droneStats;
    public BayStats BayStats => bayStats;
}
