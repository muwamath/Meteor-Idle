using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class BuildBayPanel : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text costLabel;
    [SerializeField] private Button confirmButton;

    private DroneBay targetBay;
    private Func<int> costLookup;
    private Action<DroneBay> onConfirm;
    private Action<int> moneyListener;

    private void Start()
    {
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
        moneyListener = _ => Refresh();
        if (GameManager.Instance != null) GameManager.Instance.OnMoneyChanged += moneyListener;
        if (canvasGroup != null) PanelManager.Register(canvasGroup);
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (moneyListener != null && GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= moneyListener;
    }

    public void Open(DroneBay bay, Func<int> costLookup, Action<DroneBay> onConfirm)
    {
        targetBay = bay;
        this.costLookup = costLookup;
        this.onConfirm = onConfirm;
        if (titleLabel != null) titleLabel.text = "BUILD BAY";
        SetVisible(true);
        Refresh();
    }

    public void Close()
    {
        targetBay = null;
        costLookup = null;
        onConfirm = null;
        SetVisible(false);
    }

    public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.5f;

    private void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    private void OnConfirmClicked()
    {
        if (targetBay == null || onConfirm == null) return;
        onConfirm(targetBay);
    }

    private void Refresh()
    {
        int cost = costLookup != null ? costLookup() : 0;
        int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
        if (costLabel != null) costLabel.text = $"${cost}";
        if (confirmButton != null) confirmButton.interactable = money >= cost;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
