using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Missile : MonoBehaviour
{
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private ParticleSystem explosionPrefab;
    [SerializeField] private FloatingText floatingTextPrefab;

    private Rigidbody2D rb;
    private CircleCollider2D col;
    private float impactRadius;
    private float blastRadius;
    private float despawnAt;
    private MissileTurret owner;

    // Homing state
    private Meteor homingTarget;
    private int targetGx;
    private int targetGy;
    private float homingDegPerSec;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        col.isTrigger = true;
    }

    public void Launch(
        MissileTurret turret,
        Vector3 position,
        Vector2 velocity,
        float damageStat,
        float blastStat,
        Meteor target,
        int targetGridX,
        int targetGridY,
        float homingDegPerSec)
    {
        owner = turret;
        impactRadius = 0.14f + 0.04f * Mathf.Max(0f, damageStat);
        blastRadius = Mathf.Max(0f, blastStat);

        homingTarget = target;
        targetGx = targetGridX;
        targetGy = targetGridY;
        this.homingDegPerSec = Mathf.Max(0f, homingDegPerSec);

        transform.position = position;
        ApplyVelocityRotation(velocity);
        rb.linearVelocity = velocity;
        despawnAt = Time.time + lifetime;
    }

    private void Update()
    {
        if (Time.time >= despawnAt) { Despawn(); return; }

        if (homingDegPerSec > 0.01f && homingTarget != null && homingTarget.IsAlive && homingTarget.IsVoxelPresent(targetGx, targetGy))
        {
            Vector3 voxelWorld = homingTarget.GetVoxelWorldPosition(targetGx, targetGy);
            Vector2 desired = ((Vector2)(voxelWorld - transform.position)).normalized;
            if (desired.sqrMagnitude > 0.0001f)
            {
                Vector2 current = rb.linearVelocity;
                float speed = current.magnitude;
                if (speed > 0.0001f)
                {
                    Vector2 currentDir = current / speed;
                    float maxStep = homingDegPerSec * Time.deltaTime;
                    Vector2 newDir = Vector3.RotateTowards(currentDir, desired, maxStep * Mathf.Deg2Rad, 0f);
                    Vector2 newVel = newDir * speed;
                    rb.linearVelocity = newVel;
                    ApplyVelocityRotation(newVel);
                }
            }
        }
    }

    private void ApplyVelocityRotation(Vector2 velocity)
    {
        if (velocity.sqrMagnitude < 0.0001f) return;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null || !meteor.IsAlive) return;

        float totalRadius = impactRadius + blastRadius;
        var result = meteor.ApplyBlast(transform.position, totalRadius);

        // Iter 2: per-material economy. result.totalPayout sums payoutPerCell
        // across every destroyed cell, so dirt+stone hits stay silent and any
        // gold/explosive/core blown up in the blast adds its own value. The
        // floating text + AddMoney guard on payout > 0 keeps dirt-only hits
        // visually quiet.
        int payout = result.TotalPayout;
        if (payout > 0)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.AddMoney(payout);

            if (floatingTextPrefab != null)
            {
                var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
                ft.Show($"+${payout}");
            }
        }

        if (explosionPrefab != null)
        {
            var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, 1.5f);
        }

        Despawn();
    }

    private void Despawn()
    {
        rb.linearVelocity = Vector2.zero;
        homingTarget = null;
        owner?.ReleaseMissile(this);
    }
}
