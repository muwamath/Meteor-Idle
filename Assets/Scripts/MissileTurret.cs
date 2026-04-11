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
        var missile = missilePool.Get();
        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;

        int gx = 0, gy = 0;
        bool hasVoxel = target.PickRandomPresentVoxel(out gx, out gy);

        float speed = statsInstance.missileSpeed.CurrentValue;
        Vector2 dir;
        if (hasVoxel)
        {
            // Lead-aim at the specific voxel the missile will home to. Using
            // the meteor's velocity as the target velocity — voxels move with
            // the meteor rigidly, so the voxel's future position is the voxel's
            // current world position plus meteor velocity * intercept time.
            // Note: Missile.Update homing steers toward the voxel's *current*
            // position each frame, so for Homing > 0 the initial lead is
            // partially rewritten mid-flight. The lead is still the dominant
            // benefit for Homing = 0 (base level) missiles, where the shot
            // stays straight all the way to the predicted intercept.
            Vector3 voxelWorld = target.GetVoxelWorldPosition(gx, gy);
            Vector2 leadPoint = AimSolver.PredictInterceptPoint(
                (Vector2)spawnPos,
                (Vector2)voxelWorld,
                target.Velocity,
                speed);
            dir = (leadPoint - (Vector2)spawnPos).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = barrel.up;
        }
        else
        {
            dir = barrel.up;
        }

        Meteor homingTarget = hasVoxel ? target : null;
        missile.Launch(
            this,
            spawnPos,
            dir * speed,
            statsInstance.damage.CurrentValue,
            statsInstance.blastRadius.CurrentValue,
            homingTarget,
            gx,
            gy,
            statsInstance.homing.CurrentValue);

        if (muzzleFlash != null) muzzleFlash.Play();
    }

    public void ReleaseMissile(Missile m) => missilePool.Release(m);
}
