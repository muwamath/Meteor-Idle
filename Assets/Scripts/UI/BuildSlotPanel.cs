using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class BuildSlotPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private BuildWeaponButton buttonPrefab;
    [SerializeField] private Transform buttonParent;
    [SerializeField] private WeaponType[] weapons = { WeaponType.Missile, WeaponType.Railgun };

    private readonly List<BuildWeaponButton> buttons = new List<BuildWeaponButton>();
    private BaseSlot targetSlot;
    private Func<WeaponType, int> costLookup;
    private Action<BaseSlot, WeaponType> onConfirm;
    private Action<int> moneyListener;

    private void Start()
    {
        if (buttonPrefab == null || buttonParent == null)
        {
            Debug.LogError("[BuildSlotPanel] buttonPrefab or buttonParent not assigned", this);
            return;
        }

        foreach (var weapon in weapons)
        {
            var btn = Instantiate(buttonPrefab, buttonParent);
            btn.Bind(weapon, 0, OnWeaponClicked);
            buttons.Add(btn);
        }

        moneyListener = _ => RefreshAll();
        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged += moneyListener;

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
    }

    // costLookup returns the per-weapon build cost. The panel binds each
    // button with its own weapon-specific cost so missile and railgun
    // can show different prices simultaneously.
    public void Open(BaseSlot slot, Func<WeaponType, int> costLookup, Action<BaseSlot, WeaponType> onConfirm)
    {
        targetSlot = slot;
        this.costLookup = costLookup;
        this.onConfirm = onConfirm;
        if (titleLabel != null) titleLabel.text = "BUILD BASE";
        for (int i = 0; i < buttons.Count; i++)
        {
            int cost = costLookup != null ? costLookup(weapons[i]) : 0;
            buttons[i].Bind(weapons[i], cost, OnWeaponClicked);
        }
        SetVisible(true);
        RefreshAll();
    }

    public void Close()
    {
        targetSlot = null;
        costLookup = null;
        onConfirm = null;
        SetVisible(false);
    }

    public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.5f;

    private void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    private void OnWeaponClicked(WeaponType weapon)
    {
        if (targetSlot == null || onConfirm == null) return;
        onConfirm(targetSlot, weapon);
    }

    private void RefreshAll()
    {
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        foreach (var b in buttons) b.Refresh(money);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
