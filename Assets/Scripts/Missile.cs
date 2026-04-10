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

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        col.isTrigger = true;
    }

    public void Launch(Turret turret, Vector3 position, Vector2 velocity, float damageStat, float blastStat)
    {
        owner = turret;
        // Starting 0.18 guarantees ≥ sqrt(2)/2 grid units at every sprite scale (0.7–1.6),
        // so the strict radius check always catches at least one voxel on a solid hit.
        impactRadius = 0.14f + 0.04f * Mathf.Max(0f, damageStat);
        blastRadius = Mathf.Max(0f, blastStat);
        transform.position = position;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        rb.linearVelocity = velocity;
        despawnAt = Time.time + lifetime;
    }

    private void Update()
    {
        if (Time.time >= despawnAt) Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null || !meteor.IsAlive) return;

        float totalRadius = impactRadius + blastRadius;
        int destroyed = meteor.ApplyBlast(transform.position, totalRadius);

        if (destroyed > 0)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.AddMoney(destroyed);

            if (floatingTextPrefab != null)
            {
                var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
                ft.Show($"+${destroyed}");
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
        owner?.ReleaseMissile(this);
    }
}
