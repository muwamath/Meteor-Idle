using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Missile : MonoBehaviour
{
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private ParticleSystem explosionPrefab;
    [SerializeField] private TrailRenderer trail;

    private Rigidbody2D rb;
    private CircleCollider2D col;
    private float damage;
    private float blastRadius;
    private float despawnAt;
    private Turret owner;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        col.isTrigger = true;
    }

    public void Launch(Turret turret, Vector3 position, Vector2 velocity, float damage, float blastRadius)
    {
        owner = turret;
        this.damage = damage;
        this.blastRadius = blastRadius;
        transform.position = position;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle - 90f); // missile art points up
        rb.linearVelocity = velocity;
        despawnAt = Time.time + lifetime;
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    private void Update()
    {
        if (Time.time >= despawnAt) Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null || !meteor.IsAlive) return;

        meteor.TakeDamage(damage);

        if (blastRadius > 0.01f)
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, blastRadius);
            foreach (var h in hits)
            {
                if (h == other) continue;
                var m = h.GetComponentInParent<Meteor>();
                if (m != null && m.IsAlive) m.TakeDamage(damage * 0.75f);
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
        if (trail != null) trail.emitting = false;
        rb.linearVelocity = Vector2.zero;
        owner?.ReleaseMissile(this);
    }
}
