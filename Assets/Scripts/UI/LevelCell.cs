using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelCell : MonoBehaviour
{
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text progressLabel;
    [SerializeField] private Image progressOverlay;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private GameObject bossIcon;
    [SerializeField] private RectTransform targetIndicator;

    [Header("Colors")]
    [SerializeField] private Color beatenColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [SerializeField] private Color currentColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    [SerializeField] private Color upcomingColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color bossColor = new Color(0.7f, 0.2f, 0.2f, 1f);

    private float targetRotationSpeed = 30f;
    private CellState state;

    public enum CellState { Beaten, Current, Upcoming, BossUpcoming, BossCurrent }

    public void Configure(int level, CellState cellState)
    {
        state = cellState;
        bool isBossLevel = level % 10 == 0;

        levelLabel.text = $"Lv {level}";

        if (bossIcon != null)
            bossIcon.SetActive(isBossLevel && cellState != CellState.Beaten);
        if (targetIndicator != null)
            targetIndicator.gameObject.SetActive(cellState == CellState.Current || cellState == CellState.BossCurrent);
        if (progressOverlay != null)
            progressOverlay.gameObject.SetActive(cellState == CellState.Current);
        if (progressLabel != null)
            progressLabel.gameObject.SetActive(cellState == CellState.Current || cellState == CellState.BossCurrent);

        switch (cellState)
        {
            case CellState.Beaten:
                backgroundImage.color = beatenColor;
                break;
            case CellState.Current:
                backgroundImage.color = currentColor;
                break;
            case CellState.BossCurrent:
                backgroundImage.color = bossColor;
                if (progressLabel != null)
                    progressLabel.text = "DEFEAT THE BOSS";
                if (progressOverlay != null)
                    progressOverlay.gameObject.SetActive(false);
                break;
            case CellState.BossUpcoming:
                backgroundImage.color = bossColor;
                break;
            case CellState.Upcoming:
                backgroundImage.color = upcomingColor;
                break;
        }
    }

    public void UpdateProgress(float fillAmount, int current, int threshold)
    {
        if (progressOverlay != null && progressOverlay.gameObject.activeSelf)
            progressOverlay.fillAmount = fillAmount;
        if (progressLabel != null && progressLabel.gameObject.activeSelf && state == CellState.Current)
            progressLabel.text = $"{current} / {threshold}";
    }

    private void Update()
    {
        if (targetIndicator != null && targetIndicator.gameObject.activeSelf)
            targetIndicator.Rotate(0f, 0f, -targetRotationSpeed * Time.deltaTime);
    }
}
