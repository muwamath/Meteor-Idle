using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradeButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private TurretStats stats;
    private StatId statId;
    private Action<StatId> onClick;

    public void Bind(TurretStats stats, StatId statId, Action<StatId> onClick)
    {
        this.stats = stats;
        this.statId = statId;
        this.onClick = onClick;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => this.onClick?.Invoke(this.statId));
        }
    }

    public void Refresh(int money)
    {
        if (stats == null || label == null) return;
        var stat = stats.Get(statId);
        if (stat == null) return;
        label.text = $"{stat.displayName}\nLvl {stat.level} — ${stat.NextCost}";
        if (button != null) button.interactable = money >= stat.NextCost;
    }
}
