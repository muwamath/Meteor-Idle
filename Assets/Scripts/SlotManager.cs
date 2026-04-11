using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class SlotManager : MonoBehaviour
{
    [SerializeField] private BaseSlot slotPrefab;
    [SerializeField] private int slotCount = 3;
    [SerializeField] private float slotY = -8.26f;
    [SerializeField] private float slotSpacing = 8f;
    [SerializeField] private int prebuiltIndex = 1;
    [SerializeField] private WeaponType prebuiltWeapon = WeaponType.Missile;
    [FormerlySerializedAs("buildCosts")]
    [SerializeField] private int[] missileBuildCosts = { 100, 300 };
    [SerializeField] private int[] railgunBuildCosts = { 200, 600 };

    [FormerlySerializedAs("upgradePanel")]
    [SerializeField] private MissileUpgradePanel upgradePanelMissile;
    [SerializeField] private RailgunUpgradePanel upgradePanelRailgun;
    [SerializeField] private BuildSlotPanel buildSlotPanel;
    [SerializeField] private MeteorSpawner meteorSpawner;

    private int builtPurchasedCount;
    private readonly List<BaseSlot> slots = new List<BaseSlot>();

    private void Start()
    {
        if (slotPrefab == null) { Debug.LogError("[SlotManager] slotPrefab not assigned", this); return; }

        // Panels need a sell callback pointing back at us so the Sell button
        // on the overlay can refund a specific slot.
        if (upgradePanelMissile != null) upgradePanelMissile.SetSellHandler(SellSlot);
        if (upgradePanelRailgun != null) upgradePanelRailgun.SetSellHandler(SellSlot);

        float startX = -slotSpacing * (slotCount - 1) * 0.5f;
        for (int i = 0; i < slotCount; i++)
        {
            var pos = new Vector3(startX + i * slotSpacing, slotY, 0f);
            var slot = Instantiate(slotPrefab, pos, Quaternion.identity, transform);

            // Both weapon children (MissileWeapon, RailgunWeapon) live as
            // inactive children of the slot. GetComponentsInChildren with
            // includeInactive=true picks both turrets so we can wire the
            // spawner ref into both regardless of which one ends up active.
            var turrets = slot.GetComponentsInChildren<TurretBase>(true);
            foreach (var t in turrets) t.SetRuntimeRefs(meteorSpawner);

            slot.SetMissileUpgradePanel(upgradePanelMissile);
            slot.SetRailgunUpgradePanel(upgradePanelRailgun);
            slot.EmptyClicked += OpenBuildPanel;

            // Pre-built slot is free — the player didn't pay for it, so its
            // BuildCostPaid is 0 and selling refunds only any upgrades applied.
            if (i == prebuiltIndex) slot.Build(prebuiltWeapon, 0);
            else slot.SetEmpty();

            slots.Add(slot);
        }
    }

    private void OpenBuildPanel(BaseSlot slot)
    {
        if (buildSlotPanel == null) return;
        buildSlotPanel.Open(slot, NextBuildCost, OnConfirmBuild);
    }

    private void OnConfirmBuild(BaseSlot slot, WeaponType weapon)
    {
        int cost = NextBuildCost(weapon);
        if (GameManager.Instance == null || !GameManager.Instance.TrySpend(cost)) return;
        slot.Build(weapon, cost);
        builtPurchasedCount++;
        if (buildSlotPanel != null) buildSlotPanel.Close();
    }

    // Sell a built slot: refund its build cost plus every upgrade spent on
    // this specific turret instance, reset the slot to empty, and decrement
    // the purchased-slot counter so the next rebuild costs the same as before.
    public void SellSlot(BaseSlot slot)
    {
        if (slot == null || !slot.IsBuilt) return;

        int upgradeRefund = 0;
        var turret = slot.ActiveTurret;
        if (turret is MissileTurret mt && mt.Stats != null)
            upgradeRefund = mt.Stats.TotalSpentOnUpgrades();
        else if (turret is RailgunTurret rt && rt.Stats != null)
            upgradeRefund = rt.Stats.TotalSpentOnUpgrades();

        int total = slot.BuildCostPaid + upgradeRefund;
        bool wasPurchased = slot.BuildCostPaid > 0;

        if (total > 0 && GameManager.Instance != null)
            GameManager.Instance.AddMoney(total);

        if (wasPurchased && builtPurchasedCount > 0) builtPurchasedCount--;

        slot.SetEmpty();
    }

    // Per-weapon cost lookup. The slot tier (builtPurchasedCount) is shared
    // across weapons — whichever weapon you buy for your Nth slot, it counts
    // as your Nth purchase for the escalation table.
    public int NextBuildCost(WeaponType weapon)
    {
        int[] costs = weapon == WeaponType.Railgun ? railgunBuildCosts : missileBuildCosts;
        if (costs == null || costs.Length == 0) return 0;
        if (builtPurchasedCount < costs.Length) return costs[builtPurchasedCount];
        // After the table runs out, keep scaling off the last entry.
        int overflow = builtPurchasedCount - costs.Length + 2;
        return costs[costs.Length - 1] * overflow;
    }
}
