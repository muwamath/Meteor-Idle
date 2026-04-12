using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Parallel to MissileUpgradePanel but reads from RailgunStats and uses a
// single column for all 5 stats (no Launcher/Missile category split — the
// railgun's stats don't have natural sub-categories).
public class RailgunUpgradePanel : MonoBehaviour
{
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform buttonParent;
    [SerializeField] private Button sellButton;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;
    private Action OnStatsChanged;

    private BaseSlot currentSlot;
    private RailgunStats currentStats;
    private Action<BaseSlot> sellHandler;

    public void SetSellHandler(Action<BaseSlot> handler) => sellHandler = handler;

    private void Start()
    {
        if (buttonPrefab == null) { Debug.LogError("[RailgunUpgradePanel] buttonPrefab is not assigned", this); return; }
        if (buttonParent == null) { Debug.LogError("[RailgunUpgradePanel] buttonParent is not assigned", this); return; }

        foreach (RailgunStatId id in Enum.GetValues(typeof(RailgunStatId)))
        {
            var btn = Instantiate(buttonPrefab, buttonParent);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged += moneyListener;

        OnStatsChanged = RefreshAll;

        if (sellButton != null)
            sellButton.onClick.AddListener(OnSellClicked);

        var cg = GetComponent<CanvasGroup>();
        if (cg != null) PanelManager.Register(cg);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
        DetachStats();
    }

    public void ToggleForSlot(BaseSlot slot)
    {
        var cg = GetComponent<CanvasGroup>();
        bool visible = cg != null && cg.alpha > 0.5f;
        if (visible && currentSlot == slot) Hide();
        else Show(slot);
    }

    public void Show(BaseSlot slot)
    {
        if (slot == null) return;
        var turret = slot.ActiveTurret as RailgunTurret;
        if (turret == null) return;

        DetachStats();

        currentSlot = slot;
        currentStats = turret.Stats;

        int i = 0;
        foreach (RailgunStatId id in Enum.GetValues(typeof(RailgunStatId)))
        {
            if (i >= buttons.Count) break;
            buttons[i].BindRailgun(currentStats, id, OnClicked);
            i++;
        }

        if (currentStats != null) currentStats.OnChanged += OnStatsChanged;

        PanelManager.ShowExclusive(GetComponent<CanvasGroup>());
        SetVisible(true);
        RefreshAll();
    }

    public void Hide()
    {
        DetachStats();
        currentSlot = null;
        currentStats = null;
        SetVisible(false);
    }

    private void DetachStats()
    {
        if (currentStats != null && OnStatsChanged != null)
            currentStats.OnChanged -= OnStatsChanged;
    }

    private void SetVisible(bool visible)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    private void OnClicked(RailgunStatId id)
    {
        if (currentStats == null) return;
        var stat = currentStats.Get(id);
        if (stat == null) return;
        if (stat.IsMaxed) return;
        if (GameManager.Instance != null && GameManager.Instance.TrySpend(stat.NextCost))
        {
            currentStats.ApplyUpgrade(id);
            RefreshAll();
        }
    }

    private void OnSellClicked()
    {
        var slot = currentSlot;
        if (slot == null) return;
        Hide();
        sellHandler?.Invoke(slot);
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.Refresh(money);
    }
}
