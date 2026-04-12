using UnityEngine;
using UnityEngine.EventSystems;

public class DroneBay : MonoBehaviour, ICollectorDroneEnvironment, IPointerClickHandler
{
    public enum DoorState
    {
        Closed  = 0,
        Opening = 1,
        Open    = 2,
        Closing = 3,
    }

    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField] private float openingDuration = 0.4f;
    [SerializeField] private float closingDuration = 0.4f;

    public DoorState Doors { get; private set; } = DoorState.Closed;
    public bool IsOpen => Doors == DoorState.Open;
    public Vector3 BayPosition => transform.position;
    public bool BayDoorsOpen => IsOpen;

    public float LeftDoorLocalRotationZ => leftDoor != null
        ? leftDoor.localRotation.eulerAngles.z : 0f;

    private float doorTimer;

    private static readonly float[] LeftOpenKeyframes    = { 0f, 45f, 90f };
    private static readonly float[] LeftClosingKeyframes = { 90f, 45f, 0f };

    private void Awake()
    {
        ApplyDoorRotation(0f);
    }

    public void Tick(float dt)
    {
        if (Doors == DoorState.Opening || Doors == DoorState.Closing)
        {
            doorTimer += dt;
            StepAnimation();
        }
    }

    private void Update() { Tick(Time.deltaTime); }

    public void RequestOpenDoors()
    {
        if (Doors == DoorState.Open || Doors == DoorState.Opening) return;
        Doors = DoorState.Opening;
        doorTimer = 0f;
        ApplyDoorRotation(LeftOpenKeyframes[0]);
    }

    public void RequestCloseDoors()
    {
        if (Doors == DoorState.Closed || Doors == DoorState.Closing) return;
        Doors = DoorState.Closing;
        doorTimer = 0f;
        ApplyDoorRotation(LeftClosingKeyframes[0]);
    }

    private void StepAnimation()
    {
        if (Doors == DoorState.Opening)
        {
            float third = openingDuration / 3f;
            int idx = Mathf.Clamp(Mathf.FloorToInt(doorTimer / third), 0, LeftOpenKeyframes.Length - 1);
            ApplyDoorRotation(LeftOpenKeyframes[idx]);
            if (doorTimer >= openingDuration)
            {
                ApplyDoorRotation(LeftOpenKeyframes[LeftOpenKeyframes.Length - 1]);
                Doors = DoorState.Open;
            }
        }
        else if (Doors == DoorState.Closing)
        {
            float third = closingDuration / 3f;
            int idx = Mathf.Clamp(Mathf.FloorToInt(doorTimer / third), 0, LeftClosingKeyframes.Length - 1);
            ApplyDoorRotation(LeftClosingKeyframes[idx]);
            if (doorTimer >= closingDuration)
            {
                ApplyDoorRotation(LeftClosingKeyframes[LeftClosingKeyframes.Length - 1]);
                Doors = DoorState.Closed;
            }
        }
    }

    private void ApplyDoorRotation(float magnitude)
    {
        if (leftDoor != null)  leftDoor.localRotation  = Quaternion.Euler(0f, 0f, magnitude);
        if (rightDoor != null) rightDoor.localRotation = Quaternion.Euler(0f, 0f, -magnitude);
    }

    public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
    {
        if (GameManager.Instance == null) return null;
        CoreDrop best = null;
        float bestD = float.MaxValue;
        foreach (var d in GameManager.Instance.ActiveDrops)
        {
            if (d == null || d.IsClaimed || !d.IsAlive) continue;
            float dist = Vector3.Distance(from, d.Position);
            if (dist > maxDistance) continue;
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    public void Deposit(int value)
    {
        if (GameManager.Instance != null) GameManager.Instance.AddMoney(value);
    }

    public event System.Action<DroneBay> Clicked;

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(this);
    }
}
