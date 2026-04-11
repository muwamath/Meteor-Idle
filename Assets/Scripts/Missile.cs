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
    private Turret owner;

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
        Turret turret,
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
        int destroyed = meteor.ApplyBlast(transform.position, totalRadius);

        // Phantom hit: the missile entered the meteor's circle collider, but the
        // contact point (and the walk-inward search) found no live voxels — the
        // rim has been carved away in this region. Don't explode, don't despawn;
        // let the missile keep flying through the empty crater. It may still hit
        // a different meteor along its remaining path, or time out via lifetime.
        if (destroyed <= 0) return;

        if (GameManager.Instance != null)
            GameManager.Instance.AddMoney(destroyed);

        if (floatingTextPrefab != null)
        {
            var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
            ft.Show($"+${destroyed}");
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
