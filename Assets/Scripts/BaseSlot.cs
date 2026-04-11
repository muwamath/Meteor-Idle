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

    // The typed turret components live on the two weapon children. Cached in
    // Awake so BaseSlot can route "clicked this slot" to the matching panel
    // and hand the panel that slot's per-instance stats clone.
    private MissileTurret missileTurret;
    private RailgunTurret railgunTurret;

    // Contextual upgrade panels — clicking a built turret opens the panel
    // that matches its weapon and binds it to this slot's stats instance.
    [FormerlySerializedAs("upgradePanel")]
    [SerializeField] private MissileUpgradePanel upgradePanelMissile;
    [SerializeField] private RailgunUpgradePanel upgradePanelRailgun;

    public bool IsBuilt { get; private set; }
    public WeaponType BuiltWeapon { get; private set; }
    public int BuildCostPaid { get; private set; }
    public event Action<BaseSlot> EmptyClicked;

    // Returns the active turret component (MissileTurret or RailgunTurret)
    // or null if the slot is empty. Panels read this to reach the per-slot
    // stats clone when the player opens the upgrade overlay.
    public TurretBase ActiveTurret
    {
        get
        {
            if (!IsBuilt) return null;
            return BuiltWeapon == WeaponType.Railgun
                ? (TurretBase)railgunTurret
                : missileTurret;
        }
    }

    public void SetMissileUpgradePanel(MissileUpgradePanel panel) => upgradePanelMissile = panel;
    public void SetRailgunUpgradePanel(RailgunUpgradePanel panel) => upgradePanelRailgun = panel;

    private void Awake()
    {
        // Cache turret components from both (possibly inactive) weapon children.
        if (missileWeapon != null) missileTurret = missileWeapon.GetComponentInChildren<MissileTurret>(true);
        if (railgunWeapon != null) railgunTurret = railgunWeapon.GetComponentInChildren<RailgunTurret>(true);
    }

    public void SetEmpty()
    {
        IsBuilt = false;
        BuildCostPaid = 0;
        if (turretBaseSr != null) turretBaseSr.enabled = false;
        if (missileWeapon != null) missileWeapon.SetActive(false);
        if (railgunWeapon != null) railgunWeapon.SetActive(false);
        if (plusIconSr != null) plusIconSr.enabled = true;
    }

    public void Build(WeaponType weapon, int costPaid)
    {
        IsBuilt = true;
        BuiltWeapon = weapon;
        BuildCostPaid = costPaid;
        if (turretBaseSr != null) turretBaseSr.enabled = true;
        if (plusIconSr != null) plusIconSr.enabled = false;

        // Initialize the chosen turret's per-slot stats clone BEFORE activating
        // its GameObject so Update() sees a valid statsInstance on first tick.
        if (weapon == WeaponType.Railgun)
        {
            if (railgunTurret != null) railgunTurret.InitializeForBuild();
            if (railgunWeapon != null) railgunWeapon.SetActive(true);
            if (missileWeapon != null) missileWeapon.SetActive(false);
        }
        else
        {
            if (missileTurret != null) missileTurret.InitializeForBuild();
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
        if (BuiltWeapon == WeaponType.Railgun)
        {
            if (upgradePanelRailgun == null) return;
            upgradePanelRailgun.ToggleForSlot(this);
        }
        else
        {
            if (upgradePanelMissile == null) return;
            upgradePanelMissile.ToggleForSlot(this);
        }
    }
}
