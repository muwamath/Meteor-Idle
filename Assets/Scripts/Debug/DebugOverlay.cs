using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class DebugOverlay : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_InputField moneyInput;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;

    private bool isOpen;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyMoney);
        if (moneyInput != null) moneyInput.onSubmit.AddListener(_ => ApplyMoney());
        if (resetButton != null) resetButton.onClick.AddListener(ResetGame);
    }

    private void ResetGame()
    {
        Time.timeScale = 1f;
        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
            Toggle();
    }

    public void Toggle()
    {
        isOpen = !isOpen;
        if (panelRoot != null) panelRoot.SetActive(isOpen);
        Time.timeScale = isOpen ? 0f : 1f;

        if (isOpen && moneyInput != null && GameManager.Instance != null)
        {
            moneyInput.text = GameManager.Instance.Money.ToString();
            moneyInput.Select();
            moneyInput.ActivateInputField();
        }
    }

    private void ApplyMoney()
    {
        if (moneyInput == null || GameManager.Instance == null) return;
        if (int.TryParse(moneyInput.text, out int value))
            GameManager.Instance.SetMoney(Mathf.Max(0, value));
    }

    private void OnDisable()
    {
        Time.timeScale = 1f;
    }
}
#else
public class DebugOverlay : MonoBehaviour
{
    // Debug overlay is only present in Editor and in development player builds
    // (DEVELOPMENT_BUILD). This stub exists so scene references don't break in
    // production player builds pushed to gh-pages. Does nothing at runtime.
    private void Awake() { gameObject.SetActive(false); }
}
#endif
