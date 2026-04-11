using System;
using System.Collections.Generic;
using UnityEngine;

// Parallel to MissileUpgradePanel but reads from RailgunStats and uses a
// single column for all 5 stats (no Launcher/Missile category split — the
// railgun's stats don't have natural sub-categories).
public class RailgunUpgradePanel : MonoBehaviour
{
    [SerializeField] private RailgunStats stats;
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform buttonParent;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;

    private void Start()
    {
        if (stats == null)        { Debug.LogError("[RailgunUpgradePanel] stats is not assigned", this); return; }
        if (buttonPrefab == null) { Debug.LogError("[RailgunUpgradePanel] buttonPrefab is not assigned", this); return; }
        if (buttonParent == null) { Debug.LogError("[RailgunUpgradePanel] buttonParent is not assigned", this); return; }

        foreach (var stat in stats.All())
        {
            var btn = Instantiate(buttonPrefab, buttonParent);
            btn.BindRailgun(stats, stat.id, OnClicked);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged += moneyListener;

        stats.ResetRuntime();
        RefreshAll();

        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
    }

    private void OnClicked(RailgunStatId id)
    {
        var stat = stats.Get(id);
        if (stat == null) return;
        if (stat.IsMaxed) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            stats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.Refresh(money);
    }
}
