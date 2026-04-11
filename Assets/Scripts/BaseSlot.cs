using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

[RequireComponent(typeof(Collider2D))]
public class BaseSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private SpriteRenderer turretBaseSr;
    [SerializeField] private SpriteRenderer plusIconSr;

    // The two weapon child GameObjects on the BaseSlot prefab. Build(weapon)
    // activates the matching one and deactivates the other; SetEmpty
    // deactivates both.
    [SerializeField] private GameObject missileWeapon;
    [SerializeField] private GameObject railgunWeapon;

    // Two upgrade panels — clicking a built turret routes to the panel that
    // matches its weapon type. Phase 9 wires upgradePanelRailgun in the scene;
    // until then it's null and clicking a railgun does nothing.
    [FormerlySerializedAs("upgradePanel")]
    [SerializeField] private CanvasGroup upgradePanelMissile;
    [SerializeField] private CanvasGroup upgradePanelRailgun;

    public bool IsBuilt { get; private set; }
    public WeaponType BuiltWeapon { get; private set; }
    public event Action<BaseSlot> EmptyClicked;

    public void SetMissileUpgradePanel(CanvasGroup panel) => upgradePanelMissile = panel;
    public void SetRailgunUpgradePanel(CanvasGroup panel) => upgradePanelRailgun = panel;

    public void SetEmpty()
    {
        IsBuilt = false;
        if (turretBaseSr != null) turretBaseSr.enabled = false;
        if (missileWeapon != null) missileWeapon.SetActive(false);
        if (railgunWeapon != null) railgunWeapon.SetActive(false);
        if (plusIconSr != null) plusIconSr.enabled = true;
    }

    public void Build(WeaponType weapon)
    {
        IsBuilt = true;
        BuiltWeapon = weapon;
        if (turretBaseSr != null) turretBaseSr.enabled = true;
        if (plusIconSr != null) plusIconSr.enabled = false;

        // Activate the matching weapon child, deactivate the other.
        if (weapon == WeaponType.Railgun)
        {
            if (railgunWeapon != null) railgunWeapon.SetActive(true);
            if (missileWeapon != null) missileWeapon.SetActive(false);
        }
        else
        {
            if (missileWeapon != null) missileWeapon.SetActive(true);
            if (railgunWeapon != null) railgunWeapon.SetActive(false);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsBuilt) ToggleUpgradePanel();
        else EmptyClicked?.Invoke(this);
    }

    private void ToggleUpgradePanel()
    {
        // Pick the panel that matches the built weapon. If null (e.g. railgun
        // panel before Phase 9 wiring), do nothing.
        var panel = BuiltWeapon == WeaponType.Railgun
            ? upgradePanelRailgun
            : upgradePanelMissile;
        if (panel == null) return;
        bool visible = panel.alpha < 0.5f;
        panel.alpha = visible ? 1f : 0f;
        panel.interactable = visible;
        panel.blocksRaycasts = visible;
    }
}
