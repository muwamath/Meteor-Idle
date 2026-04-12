using System;
using System.Collections.Generic;
using UnityEngine;

public class DroneUpgradePanel : MonoBehaviour
{
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform bayColumnParent;
    [SerializeField] private Transform droneColumnParent;
    [SerializeField] private BayManager bayManager;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;
    private Action OnStatsChanged;

    private void Start()
    {
        if (buttonPrefab == null) { Debug.LogError("[DroneUpgradePanel] buttonPrefab missing", this); return; }
        if (bayManager == null)   { Debug.LogError("[DroneUpgradePanel] bayManager missing", this); return; }

        foreach (BayStatId id in Enum.GetValues(typeof(BayStatId)))
        {
            var btn = Instantiate(buttonPrefab, bayColumnParent);
            btn.BindBay(bayManager.BayStats, id, OnBayClicked);
            buttons.Add(btn);
        }
        foreach (DroneStatId id in Enum.GetValues(typeof(DroneStatId)))
        {
            var btn = Instantiate(buttonPrefab, droneColumnParent);
            btn.BindDrone(bayManager.DroneStats, id, OnDroneClicked);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null) GameManager.Instance.OnMoneyChanged += moneyListener;

        OnStatsChanged = RefreshAll;
        if (bayManager.BayStats != null)   bayManager.BayStats.OnChanged   += OnStatsChanged;
        if (bayManager.DroneStats != null) bayManager.DroneStats.OnChanged += OnStatsChanged;

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
        if (OnStatsChanged != null && bayManager != null)
        {
            if (bayManager.BayStats != null)   bayManager.BayStats.OnChanged   -= OnStatsChanged;
            if (bayManager.DroneStats != null) bayManager.DroneStats.OnChanged -= OnStatsChanged;
        }
    }

    public void Toggle()
    {
        var cg = GetComponent<CanvasGroup>();
        bool visible = cg != null && cg.alpha > 0.5f;
        SetVisible(!visible);
        if (!visible) RefreshAll();
    }

    private void SetVisible(bool visible)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    private void OnBayClicked(BayStatId id)
    {
        var stat = bayManager.BayStats.Get(id);
        if (stat == null) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            bayManager.BayStats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void OnDroneClicked(DroneStatId id)
    {
        var stat = bayManager.DroneStats.Get(id);
        if (stat == null) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            bayManager.DroneStats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.Refresh(money);
    }
}
