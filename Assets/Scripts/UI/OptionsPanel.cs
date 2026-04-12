using TMPro;
using UnityEngine;

// Modal options panel. For now, the only thing it shows is the build
// version (read from BuildInfo). Future expansion lives here: graphics
// quality, audio sliders, fullscreen toggle, etc. Wired up the same
// way as DroneUpgradePanel — registered with PanelManager so opening
// it dismisses any other panel that's currently up.
public class OptionsPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text versionLabel;

    private void Start()
    {
        if (versionLabel != null) versionLabel.text = $"Build: {BuildInfo.DisplayLine}";

        var cg = GetComponent<CanvasGroup>();
        if (cg != null) PanelManager.Register(cg);
        SetVisible(false);
    }

    public void Toggle()
    {
        var cg = GetComponent<CanvasGroup>();
        bool visible = cg != null && cg.alpha > 0.5f;
        if (!visible) PanelManager.ShowExclusive(cg);
        SetVisible(!visible);
    }

    private void SetVisible(bool visible)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }
}
