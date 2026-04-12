using UnityEngine;
using UnityEngine.Serialization;

public class MissileTurret : TurretBase
{
    [FormerlySerializedAs("stats")]
    [SerializeField] private TurretStats statsTemplate;
    [SerializeField] private Missile missilePrefab;
    [SerializeField] private Transform missilePoolParent;

    private SimplePool<Missile> missilePool;
    private TurretStats statsInstance;

    public TurretStats Stats => statsInstance;

    protected override float FireRate => statsInstance != null ? statsInstance.fireRate.CurrentValue : 0.5f;
    protected override float RotationSpeed => statsInstance != null ? statsInstance.rotationSpeed.CurrentValue : 30f;
    protected override float ProjectileSpeed => statsInstance != null ? statsInstance.missileSpeed.CurrentValue : 4f;

    protected override void Awake()
    {
        base.Awake();
        if (missilePoolParent == null) missilePoolParent = transform;
        missilePool = new SimplePool<Missile>(missilePrefab, missilePoolParent, 8);
        if (statsInstance == null && statsTemplate != null)
            statsInstance = Instantiate(statsTemplate);
    }

    public override void InitializeForBuild()
    {
        // Fresh per-slot clone on every (re)build so a sold-and-rebuilt slot
        // starts from level 0 instead of inheriting old upgrade state. Destroy
        // any previous clone first so sell+rebuild cycles don't leak SOs.
        if (statsInstance != null) Destroy(statsInstance);
        if (statsTemplate != null) statsInstance = Instantiate(statsTemplate);
    }

    private void OnDestroy()
    {
        if (statsInstance != null) Destroy(statsInstance);
    }

    protected override void Fire(Meteor target)
    {
        // Iter 2: aim at the highest-priority targetable cell on this meteor
        // (gold > explosive > core). FindTarget already filtered for
        // HasAnyTargetable, so PickPriorityVoxel should almost always
        // succeed. The only failure mode is a race: the target's last
        // targetable cell died between FindTarget and Fire in the same
        // frame. In that case we skip the shot rather than firing at dirt.
        int gx = 0, gy = 0;
        if (!target.PickPriorityVoxel(out gx, out gy)) return;

        var missile = missilePool.Get();
        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;

        // Single source of truth: the ProjectileSpeed getter. Both the lead
        // solver call and the missile's launch velocity magnitude must read
        // from the same value, or the missile will aim at a point it can't
        // actually reach at the assumed speed. Do not read statsInstance
        // directly here.
        float speed = ProjectileSpeed;

        // Lead-aim at the specific core voxel the missile will home to. The
        // voxel moves rigidly with the meteor, so its future position is
        // current voxel world position + meteor velocity * intercept time.
        // Missile.Update homing steers toward the voxel's current position
        // each frame; for Homing 0 the initial lead carries the whole way.
        Vector3 voxelWorld = target.GetVoxelWorldPosition(gx, gy);
        Vector2 leadPoint = AimSolver.PredictInterceptPoint(
            (Vector2)spawnPos,
            (Vector2)voxelWorld,
            target.Velocity,
            speed);
        Vector2 dir = (leadPoint - (Vector2)spawnPos).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = barrel.up;

        missile.Launch(
            this,
            spawnPos,
            dir * speed,
            statsInstance.damage.CurrentValue,
            statsInstance.blastRadius.CurrentValue,
            target,
            gx,
            gy,
            statsInstance.homing.CurrentValue);

        if (muzzleFlash != null) muzzleFlash.Play();
    }

    public void ReleaseMissile(Missile m) => missilePool.Release(m);
}
