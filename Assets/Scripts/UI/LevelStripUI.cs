using UnityEngine;

public class LevelStripUI : MonoBehaviour
{
    [SerializeField] private LevelCell[] cells; // exactly 5, assigned in scene

    private void Start()
    {
        if (LevelState.Instance != null)
        {
            LevelState.Instance.OnLevelChanged += RefreshStrip;
            LevelState.Instance.OnBossFailed += RefreshStrip;
            LevelState.Instance.OnCoreKillRecorded += UpdateProgressOnCurrentCell;
        }
        RefreshStrip();
    }

    private void OnDestroy()
    {
        if (LevelState.Instance != null)
        {
            LevelState.Instance.OnLevelChanged -= RefreshStrip;
            LevelState.Instance.OnBossFailed -= RefreshStrip;
            LevelState.Instance.OnCoreKillRecorded -= UpdateProgressOnCurrentCell;
        }
    }

    public void RefreshStrip()
    {
        if (LevelState.Instance == null) return;

        int current = LevelState.Instance.CurrentLevel;

        for (int i = 0; i < cells.Length; i++)
        {
            int level = current + (i - 2); // -2, -1, 0, +1, +2

            if (level < 1)
            {
                cells[i].gameObject.SetActive(false);
                continue;
            }

            cells[i].gameObject.SetActive(true);
            bool isBoss = level % 10 == 0;

            LevelCell.CellState state;
            if (level < current)
                state = LevelCell.CellState.Beaten;
            else if (level == current)
                state = isBoss ? LevelCell.CellState.BossCurrent : LevelCell.CellState.Current;
            else
                state = isBoss ? LevelCell.CellState.BossUpcoming : LevelCell.CellState.Upcoming;

            cells[i].Configure(level, state);
        }

        UpdateProgressOnCurrentCell();
    }

    private void UpdateProgressOnCurrentCell()
    {
        if (LevelState.Instance == null) return;
        if (LevelState.Instance.IsBossLevel) return;

        int kills = LevelState.Instance.CoreKillsThisBlock;
        int threshold = LevelState.Instance.Threshold;
        float fill = LevelState.Instance.CoreKillProgress;

        if (cells.Length > 2)
            cells[2].UpdateProgress(fill, kills, threshold);
    }
}
