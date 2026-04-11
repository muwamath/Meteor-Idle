using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Single button prefab serves either a TurretStats (missile) stat OR a
// RailgunStats stat. Internal branches on which stats ref is set — only
// one is non-null at a time. The MissileUpgradePanel calls Bind/Refresh;
// the RailgunUpgradePanel calls BindRailgun/RefreshRailgun.
public class UpgradeButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    // Missile stat binding
    private TurretStats missileStats;
    private StatId missileStatId;
    private Action<StatId> onMissileClick;

    // Railgun stat binding
    private RailgunStats railgunStats;
    private RailgunStatId railgunStatId;
    private Action<RailgunStatId> onRailgunClick;

    public void Bind(TurretStats stats, StatId statId, Action<StatId> onClick)
    {
        missileStats = stats;
        missileStatId = statId;
        onMissileClick = onClick;
        railgunStats = null;
        onRailgunClick = null;
        WireButtonClick();
    }

    public void BindRailgun(RailgunStats stats, RailgunStatId statId, Action<RailgunStatId> onClick)
    {
        railgunStats = stats;
        railgunStatId = statId;
        onRailgunClick = onClick;
        missileStats = null;
        onMissileClick = null;
        WireButtonClick();
    }

    private void WireButtonClick()
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => {
            if (onMissileClick != null) onMissileClick.Invoke(missileStatId);
            else if (onRailgunClick != null) onRailgunClick.Invoke(railgunStatId);
        });
    }

    public void Refresh(int money)
    {
        if (label == null) return;
        if (missileStats != null)
        {
            var stat = missileStats.Get(missileStatId);
            if (stat == null) return;
            label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
            if (button != null) button.interactable = money >= stat.NextCost;
        }
        else if (railgunStats != null)
        {
            var stat = railgunStats.Get(railgunStatId);
            if (stat == null) return;
            if (stat.IsMaxed)
            {
                label.text = $"{stat.displayName}\nLvl {stat.level} — MAX";
                if (button != null) button.interactable = false;
            }
            else
            {
                label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
                if (button != null) button.interactable = money >= stat.NextCost;
            }
        }
    }
}
