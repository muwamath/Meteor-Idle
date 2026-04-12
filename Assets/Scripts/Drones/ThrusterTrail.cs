using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ThrusterTrail : MonoBehaviour
{
    [SerializeField] private CollectorDrone owner;
    [SerializeField] private float emitIntervalAtFullThrust = 0.02f;
    [SerializeField] private float particleLifetime = 0.5f;
    [SerializeField] private Sprite particleSprite;

    private float emitTimer;

    private static readonly Queue<TrailParticle> pool = new Queue<TrailParticle>();

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
        TrailParticle p;
        if (pool.Count > 0)
        {
            p = pool.Dequeue();
            p.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("TrailParticle");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = particleSprite;
            sr.sortingOrder = 3;
            p = go.AddComponent<TrailParticle>();
        }
        p.transform.position = transform.position;
        p.Reset(particleLifetime);
    }

    public static void ReturnToPool(TrailParticle p)
    {
        if (p == null) return;
        p.gameObject.SetActive(false);
        pool.Enqueue(p);
    }
}

public class TrailParticle : MonoBehaviour
{
    private float lifetime = 0.5f;
    private float t;
    private SpriteRenderer sr;

    private void Awake() { sr = GetComponent<SpriteRenderer>(); }

    public void Reset(float life)
    {
        lifetime = life;
        t = 0f;
        if (sr != null) { var c = sr.color; c.a = 1f; sr.color = c; }
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (sr != null)
        {
            var c = sr.color;
            c.a = Mathf.Clamp01(1f - t / lifetime);
            sr.color = c;
        }
        if (t >= lifetime) ThrusterTrail.ReturnToPool(this);
    }
}
