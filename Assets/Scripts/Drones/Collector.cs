using UnityEngine;
using UnityEngine.EventSystems;

public class Collector : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Transform leftTooth;
    [SerializeField] private Transform rightTooth;
    [SerializeField] private float toothStepInterval = 0.4f;

    private float toothTimer;
    private int toothStep;

    // 4 quantized rotation stops — voxel aesthetic, no lerp.
    private static readonly float[] ToothSteps = { 0f, 90f, 180f, 270f };

    public Vector3 Position => transform.position;

    public void Deposit(int value)
    {
        if (GameManager.Instance != null) GameManager.Instance.AddMoney(value);
    }

    private void Update()
    {
        toothTimer += Time.deltaTime;
        if (toothTimer >= toothStepInterval)
        {
            toothTimer = 0f;
            toothStep = (toothStep + 1) % ToothSteps.Length;
            ApplyToothRotation();
        }
    }

    private void ApplyToothRotation()
    {
        float angle = ToothSteps[toothStep];
        if (leftTooth != null) leftTooth.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (rightTooth != null) rightTooth.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    // Stub for future upgrade panel
    public void OnPointerClick(PointerEventData eventData)
    {
        // Phase: future upgrade panel toggle
    }
}
