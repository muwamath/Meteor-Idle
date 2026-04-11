using System;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class BaseSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private SpriteRenderer turretBaseSr;
    [SerializeField] private Transform barrel;
    [SerializeField] private SpriteRenderer plusIconSr;
    [SerializeField] private TurretBase turret;
    [SerializeField] private CanvasGroup upgradePanel;

    public bool IsBuilt { get; private set; }
    public event Action<BaseSlot> EmptyClicked;

    public void SetUpgradePanel(CanvasGroup panel) => upgradePanel = panel;

    public void SetEmpty()
    {
        IsBuilt = false;
        if (turretBaseSr != null) turretBaseSr.enabled = false;
        if (barrel != null) barrel.gameObject.SetActive(false);
        if (plusIconSr != null) plusIconSr.enabled = true;
        if (turret != null) turret.enabled = false;
    }

    public void Build(WeaponType weapon)
    {
        IsBuilt = true;
        if (turretBaseSr != null) turretBaseSr.enabled = true;
        if (barrel != null) barrel.gameObject.SetActive(true);
        if (plusIconSr != null) plusIconSr.enabled = false;
        if (turret != null) turret.enabled = true;
        // Only Missile weapon today; future weapons branch here.
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsBuilt) ToggleUpgradePanel();
        else EmptyClicked?.Invoke(this);
    }

    private void ToggleUpgradePanel()
    {
        if (upgradePanel == null) return;
        bool visible = upgradePanel.alpha < 0.5f;
        upgradePanel.alpha = visible ? 1f : 0f;
        upgradePanel.interactable = visible;
        upgradePanel.blocksRaycasts = visible;
    }
}
