using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class CollectorDrone : MonoBehaviour
{
    [SerializeField] private float contactPushMagnitude = 2.5f;
    private ICollectorDroneEnvironment env;
    private DroneBody body;

    private float battery;
    private float batteryCapacity;
    private int cargoCapacity;
    private int cargoCount;
    private int cargoValue;

    private float reserveThresholdFraction;
    private float pickupRadius;
    private float dockRadius;
    private float seekMaxRange = 30f;

    [SerializeField] private float avoidanceSafetyMargin = 0.35f;
    private MeteorSpawner cachedSpawner;

    public void SetMeteorSpawner(MeteorSpawner spawner) { cachedSpawner = spawner; }

    public DroneState State { get; private set; } = DroneState.Idle;
    public CoreDrop TargetDrop { get; private set; }
    public float Battery => battery;
    public int CargoCount => cargoCount;
    public DroneBody Body => body;

    private void Awake()
    {
        body = new DroneBody(transform.position, thrustCap: 4f, dampingPerSec: 1f);
        var col = GetComponent<CircleCollider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null) return;
        Vector2 away = ((Vector2)(transform.position - meteor.transform.position)).normalized;
        if (away.sqrMagnitude < 0.001f) away = Vector2.up;
        body?.ApplyPushKick(away * contactPushMagnitude);
    }

    public void Initialize(
        ICollectorDroneEnvironment env,
        float thrust,
        float damping,
        float batteryCapacity,
        int cargoCapacity,
        float reserveThresholdFraction,
        float pickupRadius,
        float dockRadius)
    {
        this.env = env;
        if (body == null) body = new DroneBody(transform.position, thrust, damping);
        body.Position = transform.position;
        body.Velocity = Vector2.zero;
        body.LimpHomeMode = false;
        body.ThrustCap = thrust;
        body.DampingPerSec = damping;
        this.batteryCapacity = batteryCapacity;
        this.battery = batteryCapacity;
        this.cargoCapacity = Mathf.Max(1, cargoCapacity);
        this.reserveThresholdFraction = reserveThresholdFraction;
        this.pickupRadius = pickupRadius;
        this.dockRadius = dockRadius;
        this.cargoCount = 0;
        this.cargoValue = 0;
        State = DroneState.Idle;
    }

    public void Tick(float dt)
    {
        if (env == null) return;
        switch (State)
        {
            case DroneState.Idle: TickIdle(dt); break;
            case DroneState.Launching: TickLaunching(dt); break;
            case DroneState.Seeking: TickSeeking(dt); break;
            case DroneState.Pickup: TickPickup(dt); break;
            case DroneState.Returning: TickReturning(dt); break;
            case DroneState.Docking: TickDocking(dt); break;
            case DroneState.Depositing: TickDepositing(dt); break;
        }
    }

    private void TickIdle(float dt)
    {
        battery = Mathf.Min(batteryCapacity, battery + dt);
        if (battery < batteryCapacity) return;
        var drop = env.FindNearestUnclaimedDrop(env.BayPosition, seekMaxRange);
        if (drop == null) return;
        env.RequestOpenDoors();
        State = DroneState.Launching;
    }

    private void TickLaunching(float dt)
    {
        if (!env.BayDoorsOpen) return;
        var drop = env.FindNearestUnclaimedDrop(env.BayPosition, seekMaxRange);
        if (drop == null || !drop.TryClaim())
        {
            env.RequestCloseDoors();
            State = DroneState.Idle;
            return;
        }
        TargetDrop = drop;
        State = DroneState.Seeking;
    }

    private void TickSeeking(float dt)
    {
        if (TargetDrop == null || !TargetDrop.IsAlive)
        {
            var replacement = env.FindNearestUnclaimedDrop(transform.position, seekMaxRange);
            if (replacement != null && replacement.TryClaim()) { TargetDrop = replacement; return; }
            State = DroneState.Returning;
            return;
        }
        battery -= dt;
        if (battery <= batteryCapacity * reserveThresholdFraction)
        {
            State = DroneState.Returning;
            return;
        }
        float dist = Vector3.Distance(transform.position, TargetDrop.Position);
        if (dist <= pickupRadius) State = DroneState.Pickup;
    }

    private void TickPickup(float dt)
    {
        if (TargetDrop != null && TargetDrop.IsAlive)
        {
            cargoCount++;
            cargoValue += TargetDrop.Value;
            TargetDrop.Consume();
            if (GameManager.Instance != null) GameManager.Instance.UnregisterDrop(TargetDrop);
            TargetDrop = null;
        }
        bool roomInCargo = cargoCount < cargoCapacity;
        bool aboveReserve = battery > batteryCapacity * reserveThresholdFraction;
        if (roomInCargo && aboveReserve)
        {
            var next = env.FindNearestUnclaimedDrop(transform.position, seekMaxRange);
            if (next != null && next.TryClaim()) { TargetDrop = next; State = DroneState.Seeking; return; }
        }
        State = DroneState.Returning;
    }

    private void TickReturning(float dt)
    {
        battery -= dt;
        if (battery <= 0f)
        {
            battery = 0f;
            body.LimpHomeMode = true;
        }
        float dist = Vector3.Distance(transform.position, env.BayPosition);
        if (dist <= dockRadius)
        {
            env.RequestOpenDoors();
            State = DroneState.Docking;
        }
    }

    private void TickDocking(float dt)
    {
        if (!env.BayDoorsOpen) return;
        State = DroneState.Depositing;
        env.Deposit(cargoValue);
        cargoCount = 0;
        cargoValue = 0;
    }

    private void TickDepositing(float dt)
    {
        env.RequestCloseDoors();
        body.LimpHomeMode = false;
        State = DroneState.Idle;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (body == null || env == null) { Tick(dt); return; }

        body.DesiredThrust = ComputeDesiredThrust();
        ApplyMeteorAvoidance();
        Tick(dt);
        body.Integrate(dt);
        transform.position = new Vector3(body.Position.x, body.Position.y, 0f);
    }

    private void ApplyMeteorAvoidance()
    {
        if (cachedSpawner == null)
            cachedSpawner = Object.FindFirstObjectByType<MeteorSpawner>();
        if (cachedSpawner != null)
        {
            foreach (var m in cachedSpawner.ActiveMeteors)
            {
                if (m == null || !m.IsAlive) continue;
                float radius = 0.75f * m.transform.localScale.x;
                body.ApplyAvoidance((Vector2)m.transform.position, radius, avoidanceSafetyMargin);
            }
            return;
        }
        var loose = Object.FindObjectsByType<Meteor>(FindObjectsSortMode.None);
        foreach (var m in loose)
        {
            if (m == null || !m.IsAlive) continue;
            float radius = 0.75f * m.transform.localScale.x;
            body.ApplyAvoidance((Vector2)m.transform.position, radius, avoidanceSafetyMargin);
        }
    }

    private Vector2 ComputeDesiredThrust()
    {
        switch (State)
        {
            case DroneState.Seeking:
            case DroneState.Pickup:
                if (TargetDrop != null && TargetDrop.IsAlive)
                    return ((Vector2)(TargetDrop.Position - transform.position)).normalized;
                return Vector2.zero;
            case DroneState.Returning:
            case DroneState.Docking:
                return ((Vector2)(env.BayPosition - transform.position)).normalized;
            default:
                return Vector2.zero;
        }
    }
}
