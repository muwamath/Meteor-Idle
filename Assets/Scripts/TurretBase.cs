using UnityEngine;

// Abstract base class for all turret weapons. Owns shared logic: targeting,
// barrel rotation, reload timer. Subclasses implement Fire() and expose the
// per-weapon stat properties FireRate and RotationSpeed.
public abstract class TurretBase : MonoBehaviour
{
    [SerializeField] protected Transform barrel;
    [SerializeField] protected Transform muzzle;
    [SerializeField] protected ParticleSystem muzzleFlash;
    [SerializeField] protected MeteorSpawner meteorSpawner;

    [SerializeField] protected float aimAlignmentDeg = 10f;

    protected float reloadTimer;

    public void SetRuntimeRefs(MeteorSpawner spawner)
    {
        meteorSpawner = spawner;
    }

    // Called by BaseSlot.Build before the turret GameObject is activated, so
    // each slot gets its own fresh runtime copy of its stats asset. Subclasses
    // override to clone their typed stats template.
    public virtual void InitializeForBuild() { }

    protected virtual void Awake()
    {
        if (meteorSpawner == null) meteorSpawner = FindAnyObjectByType<MeteorSpawner>();
    }

    // Subclass contracts — per-weapon stats come through here.
    protected abstract float FireRate { get; }
    protected abstract float RotationSpeed { get; }
    protected abstract float ProjectileSpeed { get; }
    protected abstract void Fire(Meteor target);

    protected virtual void Update()
    {
        if (reloadTimer > 0f) reloadTimer -= Time.deltaTime;

        var target = FindTarget();
        if (target == null) return;

        Vector2 aimPoint = ComputeAimPoint(target);
        Vector2 toTarget = aimPoint - (Vector2)barrel.position;
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float rotSpeed = RotationSpeed;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, rotSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0, 0, newAngle);

        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (reloadTimer <= 0f && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            reloadTimer = 1f / Mathf.Max(0.05f, FireRate);
        }
    }

    protected Meteor FindTarget()
    {
        if (meteorSpawner == null) return null;
        Meteor closest = null;
        float bestSqr = float.PositiveInfinity;
        foreach (var m in meteorSpawner.ActiveMeteors)
        {
            // HasLiveCore is the Iter 1 targeting filter: dirt-only remnants
            // are never engaged. A meteor with zero cores is not a valid
            // target, even if it's alive and still has dirt voxels. Turrets
            // with no valid target hold their last aim (no rotation, no
            // reload reset) — Update early-returns on null.
            if (m == null || !m.IsAlive || !m.HasLiveCore) continue;
            float d = ((Vector2)(m.transform.position - barrel.position)).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                closest = m;
            }
        }
        return closest;
    }

    // Lead-aim helper: returns the world-space point the barrel should
    // rotate toward, given a target meteor. Plugs the meteor's current
    // position and velocity into AimSolver for a constant-velocity
    // intercept prediction using the subclass's ProjectileSpeed.
    protected Vector2 ComputeAimPoint(Meteor target)
    {
        return AimSolver.PredictInterceptPoint(
            (Vector2)barrel.position,
            (Vector2)target.transform.position,
            target.Velocity,
            ProjectileSpeed);
    }
}
