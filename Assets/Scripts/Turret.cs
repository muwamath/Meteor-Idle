using UnityEngine;

public class Turret : MonoBehaviour
{
    [SerializeField] private TurretStats stats;
    [SerializeField] private Transform barrel;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Missile missilePrefab;
    [SerializeField] private Transform missilePoolParent;
    [SerializeField] private MeteorSpawner meteorSpawner;
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private float range = 14f;
    [SerializeField] private float aimAlignmentDeg = 10f;

    private SimplePool<Missile> missilePool;
    private float reloadTimer;

    public TurretStats Stats => stats;

    private void Awake()
    {
        missilePool = new SimplePool<Missile>(missilePrefab, missilePoolParent != null ? missilePoolParent : transform, 8);
    }

    private void Update()
    {
        if (reloadTimer > 0f) reloadTimer -= Time.deltaTime;

        var target = FindTarget();
        if (target == null) return;

        Vector2 toTarget = (Vector2)(target.transform.position - barrel.position);
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float rotSpeed = stats.rotationSpeed.CurrentValue;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, rotSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0, 0, newAngle);

        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (reloadTimer <= 0f && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            reloadTimer = 1f / Mathf.Max(0.05f, stats.fireRate.CurrentValue);
        }
    }

    private Meteor FindTarget()
    {
        if (meteorSpawner == null) return null;
        Meteor closest = null;
        float bestSqr = range * range;
        foreach (var m in meteorSpawner.ActiveMeteors)
        {
            if (m == null || !m.IsAlive) continue;
            float d = ((Vector2)(m.transform.position - barrel.position)).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                closest = m;
            }
        }
        return closest;
    }

    private void Fire(Meteor target)
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
