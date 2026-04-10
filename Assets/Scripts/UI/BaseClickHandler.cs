using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider2D))]
public class BaseClickHandler : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private CanvasGroup upgradePanel;
    [SerializeField] private bool startHidden = true;

    private void Start()
    {
        if (startHidden) SetVisible(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        SetVisible(upgradePanel == null || upgradePanel.alpha < 0.5f);
    }

    private void SetVisible(bool visible)
    {
        if (upgradePanel == null) return;
        upgradePanel.alpha = visible ? 1f : 0f;
        upgradePanel.interactable = visible;
        upgradePanel.blocksRaycasts = visible;
    }
}
