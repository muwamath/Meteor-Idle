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
    [SerializeField] private TMP_InputField levelInput;
    [SerializeField] private Button levelApplyButton;

    private bool isOpen;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyMoney);
        if (moneyInput != null) moneyInput.onSubmit.AddListener(_ => ApplyMoney());
        if (resetButton != null) resetButton.onClick.AddListener(ResetGame);
        if (levelApplyButton != null) levelApplyButton.onClick.AddListener(ApplyLevel);
        if (levelInput != null) levelInput.onSubmit.AddListener(_ => ApplyLevel());
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

        if (isOpen)
        {
            if (moneyInput != null && GameManager.Instance != null)
            {
                moneyInput.text = GameManager.Instance.Money.ToString();
                moneyInput.Select();
                moneyInput.ActivateInputField();
            }
            if (levelInput != null && LevelState.Instance != null)
                levelInput.text = LevelState.Instance.CurrentLevel.ToString();
        }
    }

    private void ApplyMoney()
    {
        if (moneyInput == null || GameManager.Instance == null) return;
        if (int.TryParse(moneyInput.text, out int value))
            GameManager.Instance.SetMoney(Mathf.Max(0, value));
    }

    private void ApplyLevel()
    {
        if (levelInput == null || LevelState.Instance == null) return;
        if (int.TryParse(levelInput.text, out int level))
            LevelState.Instance.SetLevel(Mathf.Max(1, level));
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
