using TMPro;
using UnityEngine;

public class MoneyDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnMoneyChanged += Refresh;
            Refresh(GameManager.Instance.Money);
        }
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            // Rebind in case Awake order placed us before GameManager.
            GameManager.Instance.OnMoneyChanged -= Refresh;
            GameManager.Instance.OnMoneyChanged += Refresh;
            Refresh(GameManager.Instance.Money);
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnMoneyChanged -= Refresh;
    }

    private void Refresh(int money)
    {
        if (label != null) label.text = $"${money}";
    }
}
