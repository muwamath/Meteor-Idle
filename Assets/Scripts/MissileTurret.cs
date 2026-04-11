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

        float speed = statsInstance.missileSpeed.CurrentValue;
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
