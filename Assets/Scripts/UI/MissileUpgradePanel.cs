using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MissileUpgradePanel : MonoBehaviour
{
    [SerializeField] private UpgradeButton buttonPrefab;
    [SerializeField] private Transform launcherColumnParent;
    [SerializeField] private Transform missileColumnParent;
    [SerializeField] private Button sellButton;

    private readonly List<UpgradeButton> buttons = new List<UpgradeButton>();
    private Action<int> moneyListener;
    private Action OnStatsChanged;

    private BaseSlot currentSlot;
    private TurretStats currentStats;
    private Action<BaseSlot> sellHandler;

    public void SetSellHandler(Action<BaseSlot> handler) => sellHandler = handler;

    private void Start()
    {
        if (buttonPrefab == null)       { Debug.LogError("[MissileUpgradePanel] buttonPrefab is not assigned", this); return; }
        if (launcherColumnParent == null) { Debug.LogError("[MissileUpgradePanel] launcherColumnParent is not assigned", this); return; }
        if (missileColumnParent == null)  { Debug.LogError("[MissileUpgradePanel] missileColumnParent is not assigned", this); return; }

        // Build the 6 upgrade buttons once, using the stat ids from the enum.
        // Bindings are (re)pointed at the live slot's stats instance on Show.
        foreach (StatId id in Enum.GetValues(typeof(StatId)))
        {
            Transform parent = IsLauncherStat(id) ? launcherColumnParent : missileColumnParent;
            var btn = Instantiate(buttonPrefab, parent);
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

    private static bool IsLauncherStat(StatId id)
    {
        return id == StatId.FireRate || id == StatId.RotationSpeed;
    }

    // Open-or-close for a given slot. If already open for a different slot,
    // rebind to the new one; if open for the same slot, close.
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
        var turret = slot.ActiveTurret as MissileTurret;
        if (turret == null) return;

        DetachStats();

        currentSlot = slot;
        currentStats = turret.Stats;

        int i = 0;
        foreach (StatId id in Enum.GetValues(typeof(StatId)))
        {
            if (i >= buttons.Count) break;
            buttons[i].Bind(currentStats, id, OnClicked);
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

    private void OnClicked(StatId id)
    {
        if (currentStats == null) return;
        var stat = currentStats.Get(id);
        if (stat == null) return;
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
