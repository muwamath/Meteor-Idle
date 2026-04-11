using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Full-screen transparent image that closes a target CanvasGroup modal when
// clicked. Place as a sibling rendered BEFORE the modal in hierarchy order so
// the modal's own graphics capture clicks first — only clicks that miss the
// modal fall through to the catcher.
[RequireComponent(typeof(Image))]
public class ModalClickCatcher : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private CanvasGroup target;

    private Image image;

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void LateUpdate()
    {
        if (target == null || image == null) return;
        // Only catch clicks while the modal is actually visible. LateUpdate runs
        // after the click that opened the modal was already routed, so we don't
        // accidentally close the modal on the same frame it opened.
        bool modalOpen = target.alpha > 0.5f;
        if (image.raycastTarget != modalOpen) image.raycastTarget = modalOpen;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (target == null) return;
        target.alpha = 0f;
        target.interactable = false;
        target.blocksRaycasts = false;
    }
}
