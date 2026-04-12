using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ThrusterTrail : MonoBehaviour
{
    [SerializeField] private CollectorDrone owner;
    [SerializeField] private float emitIntervalAtFullThrust = 0.02f;
    [SerializeField] private float particleLifetime = 0.5f;
    [SerializeField] private Sprite particleSprite;

    private float emitTimer;

    private void Update()
    {
        if (owner == null || owner.Body == null) return;
        float velocityMagnitude = owner.Body.Velocity.magnitude;
        if (velocityMagnitude < 0.1f) return;
        float interval = emitIntervalAtFullThrust
            * Mathf.Clamp(owner.Body.ThrustCap / Mathf.Max(0.01f, velocityMagnitude), 0.5f, 2f);
        emitTimer += Time.deltaTime;
        if (emitTimer < interval) return;
        emitTimer = 0f;
        Emit();
    }

    private void Emit()
    {
        var go = new GameObject("TrailParticle");
        go.transform.position = transform.position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = particleSprite;
        sr.color = Color.white;
        sr.sortingOrder = 3;
        var p = go.AddComponent<TrailParticle>();
        p.lifetime = particleLifetime;
    }
}

public class TrailParticle : MonoBehaviour
{
    public float lifetime = 0.3f;
    private float t;
    private SpriteRenderer sr;
    private void Awake() { sr = GetComponent<SpriteRenderer>(); }
    private void Update()
    {
        t += Time.deltaTime;
        if (sr != null)
        {
            var c = sr.color;
            c.a = Mathf.Clamp01(1f - t / lifetime);
            sr.color = c;
        }
        if (t >= lifetime) Destroy(gameObject);
    }
}
