using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradePanel : MonoBehaviour
{
    [SerializeField] private TurretStats stats;
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform buttonParent;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();

    private void Start()
    {
        if (stats == null || buttonPrefab == null || buttonParent == null) return;

        foreach (var stat in stats.All())
        {
            var btn = Instantiate(buttonPrefab, buttonParent);
            btn.Bind(stats, stat.id, OnClicked);
            buttons.Add(btn);
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged += _ => RefreshAll();

        stats.ResetRuntime();
        RefreshAll();
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
