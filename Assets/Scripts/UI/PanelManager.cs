using System.Collections.Generic;
using UnityEngine;

// Ensures only one overlay panel is visible at a time. Any panel that wants
// exclusive visibility calls PanelManager.ShowExclusive(myCanvasGroup) before
// making itself visible.
public static class PanelManager
{
    private static readonly List<CanvasGroup> registered = new List<CanvasGroup>();

    public static void Register(CanvasGroup cg)
    {
        if (cg != null && !registered.Contains(cg)) registered.Add(cg);
    }

    public static void ShowExclusive(CanvasGroup toShow)
    {
        foreach (var cg in registered)
        {
            if (cg == null || cg == toShow) continue;
            if (cg.alpha > 0.5f)
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }
    }
}
