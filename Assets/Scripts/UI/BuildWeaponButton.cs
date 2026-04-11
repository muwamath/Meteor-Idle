using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuildWeaponButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    private WeaponType weapon;
    private int cost;
    private Action<WeaponType> onClick;

    public void Bind(WeaponType weapon, int cost, Action<WeaponType> onClick)
    {
        this.weapon = weapon;
        this.cost = cost;
        this.onClick = onClick;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => this.onClick?.Invoke(this.weapon));
        }
    }

    public void Refresh(int money)
    {
        if (label != null) label.text = $"{weapon}\n${cost}";
        if (button != null) button.interactable = money >= cost;
    }
}
