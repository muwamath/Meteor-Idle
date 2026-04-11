using UnityEngine;

public class MissileTurret : TurretBase
{
    [SerializeField] private TurretStats stats;
    [SerializeField] private Missile missilePrefab;
    [SerializeField] private Transform missilePoolParent;

    private SimplePool<Missile> missilePool;

    public TurretStats Stats => stats;

    protected override float FireRate => stats.fireRate.CurrentValue;
    protected override float RotationSpeed => stats.rotationSpeed.CurrentValue;

    protected override void Awake()
    {
        base.Awake();
        if (missilePoolParent == null) missilePoolParent = transform;
        missilePool = new SimplePool<Missile>(missilePrefab, missilePoolParent, 8);
    }

    protected override void Fire(Meteor target)
    {
        var missile = missilePool.Get();
        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;

        // Pick a specific voxel on the target meteor to aim at.
        int gx = 0, gy = 0;
        bool hasVoxel = target.PickRandomPresentVoxel(out gx, out gy);

        Vector2 dir;
        if (hasVoxel)
        {
            Vector3 voxelWorld = target.GetVoxelWorldPosition(gx, gy);
            dir = ((Vector2)(voxelWorld - spawnPos)).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = barrel.up;
        }
        else
        {
            dir = barrel.up;
        }

        float speed = stats.missileSpeed.CurrentValue;
        // Only pass the target meteor for homing if we successfully picked a voxel to aim at.
        // When hasVoxel is false (meteor has no live cells between FindTarget and Fire),
        // the missile flies as a dumb projectile toward barrel.up.
        Meteor homingTarget = hasVoxel ? target : null;
        missile.Launch(
            this,
            spawnPos,
            dir * speed,
            stats.damage.CurrentValue,
            stats.blastRadius.CurrentValue,
            homingTarget,
            gx,
            gy,
            stats.homing.CurrentValue);

        if (muzzleFlash != null) muzzleFlash.Play();
    }

    public void ReleaseMissile(Missile m) => missilePool.Release(m);
}
