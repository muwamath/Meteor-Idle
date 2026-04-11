using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradePanel : MonoBehaviour
{
    [SerializeField] private TurretStats stats;
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform launcherColumnParent;
    [SerializeField] private Transform missileColumnParent;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;

    private void Start()
    {
        if (stats == null)              { Debug.LogError("[UpgradePanel] stats is not assigned", this); return; }
        if (buttonPrefab == null)       { Debug.LogError("[UpgradePanel] buttonPrefab is not assigned", this); return; }
        if (launcherColumnParent == null) { Debug.LogError("[UpgradePanel] launcherColumnParent is not assigned", this); return; }
        if (missileColumnParent == null)  { Debug.LogError("[UpgradePanel] missileColumnParent is not assigned", this); return; }

        foreach (var stat in stats.All())
        {
            Transform parent = IsLauncherStat(stat.id) ? launcherColumnParent : missileColumnParent;
            var btn = Instantiate(buttonPrefab, parent);
            btn.Bind(stats, stat.id, OnClicked);
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

    private static bool IsLauncherStat(StatId id)
    {
        return id == StatId.FireRate || id == StatId.RotationSpeed;
    }

    private void OnClicked(StatId id)
    {
        var stat = stats.Get(id);
        if (stat == null) return;
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
