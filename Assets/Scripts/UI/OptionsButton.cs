using UnityEngine;
using UnityEngine.UI;

// Tiny gear button anchored bottom-right. The only thing it does is
// toggle OptionsPanel. Lives on its own GameObject so the click target
// stays small and out of the way of gameplay.
[RequireComponent(typeof(Button))]
public class OptionsButton : MonoBehaviour
{
    [SerializeField] private OptionsPanel panel;

    private void Awake()
    {
        var btn = GetComponent<Button>();
        btn.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        if (panel != null) panel.Toggle();
    }
}
