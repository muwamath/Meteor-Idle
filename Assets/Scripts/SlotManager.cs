using System.Collections.Generic;
using UnityEngine;

public class SlotManager : MonoBehaviour
{
    [SerializeField] private BaseSlot slotPrefab;
    [SerializeField] private int slotCount = 3;
    [SerializeField] private float slotY = -8.26f;
    [SerializeField] private float slotSpacing = 8f;
    [SerializeField] private int prebuiltIndex = 1;
    [SerializeField] private WeaponType prebuiltWeapon = WeaponType.Missile;
    [SerializeField] private int[] buildCosts = { 100, 300 };

    [SerializeField] private CanvasGroup upgradePanel;
    [SerializeField] private BuildSlotPanel buildSlotPanel;
    [SerializeField] private MeteorSpawner meteorSpawner;

    private int builtPurchasedCount;
    private readonly List<BaseSlot> slots = new List<BaseSlot>();

    private void Start()
    {
        if (slotPrefab == null) { Debug.LogError("[SlotManager] slotPrefab not assigned", this); return; }

        float startX = -slotSpacing * (slotCount - 1) * 0.5f;
        for (int i = 0; i < slotCount; i++)
        {
            var pos = new Vector3(startX + i * slotSpacing, slotY, 0f);
            var slot = Instantiate(slotPrefab, pos, Quaternion.identity, transform);

            var t = slot.GetComponent<Turret>();
            if (t != null) t.SetRuntimeRefs(meteorSpawner);
            slot.SetUpgradePanel(upgradePanel);
            slot.EmptyClicked += OpenBuildPanel;

            if (i == prebuiltIndex) slot.Build(prebuiltWeapon);
            else slot.SetEmpty();

            slots.Add(slot);
        }
    }

    private void OpenBuildPanel(BaseSlot slot)
    {
        if (buildSlotPanel == null) return;
        int cost = NextBuildCost();
        buildSlotPanel.Open(slot, cost, OnConfirmBuild);
    }

    private void OnConfirmBuild(BaseSlot slot, WeaponType weapon)
    {
        int cost = NextBuildCost();
        if (GameManager.Instance == null || !GameManager.Instance.TrySpend(cost)) return;
        slot.Build(weapon);
        builtPurchasedCount++;
        if (buildSlotPanel != null) buildSlotPanel.Close();
    }

    public int NextBuildCost()
    {
        if (buildCosts == null || buildCosts.Length == 0) return 0;
        if (builtPurchasedCount < buildCosts.Length) return buildCosts[builtPurchasedCount];
        // After the table runs out, keep scaling off the last entry.
        int overflow = builtPurchasedCount - buildCosts.Length + 2;
        return buildCosts[buildCosts.Length - 1] * overflow;
    }
}
