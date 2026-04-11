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
    [SerializeField] private WeaponType[] weapons = { WeaponType.Missile };

    private readonly List<BuildWeaponButton> buttons = new List<BuildWeaponButton>();
    private BaseSlot targetSlot;
    private int currentCost;
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

    public void Open(BaseSlot slot, int cost, Action<BaseSlot, WeaponType> onConfirm)
    {
        targetSlot = slot;
        currentCost = cost;
        this.onConfirm = onConfirm;
        if (titleLabel != null) titleLabel.text = $"BUILD BASE — ${cost}";
        for (int i = 0; i < buttons.Count; i++) buttons[i].Bind(weapons[i], cost, OnWeaponClicked);
        SetVisible(true);
        RefreshAll();
    }

    public void Close()
    {
        targetSlot = null;
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
