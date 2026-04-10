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
    [SerializeField] private float rotationSpeedDegPerSec = 45f;
    [SerializeField] private float aimAlignmentDeg = 10f;
    [SerializeField] private float maxWobbleDeg = 30f;

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
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, rotationSpeedDegPerSec * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0, 0, newAngle);

        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (reloadTimer <= 0f && alignmentErr <= aimAlignmentDeg)
        {
            Fire();
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

    private void Fire()
    {
        var missile = missilePool.Get();

        // Barrel "up" is local +Y; world direction comes from barrel.up
        Vector2 dir = barrel.up;
        float accuracy = Mathf.Clamp01(stats.accuracy.CurrentValue);
        float wobble = (1f - accuracy) * maxWobbleDeg;
        float offset = Random.Range(-wobble, wobble);
        dir = (Vector2)(Quaternion.Euler(0, 0, offset) * dir);

        float speed = stats.missileSpeed.CurrentValue;
        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;
        missile.Launch(this, spawnPos, dir * speed, stats.damage.CurrentValue, stats.blastRadius.CurrentValue);

        if (muzzleFlash != null) muzzleFlash.Play();
    }

    public void ReleaseMissile(Missile m) => missilePool.Release(m);
}
