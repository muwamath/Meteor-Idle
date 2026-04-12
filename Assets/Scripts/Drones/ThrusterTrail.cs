using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ThrusterTrail : MonoBehaviour
{
    [SerializeField] private CollectorDrone owner;
    [SerializeField] private float emitInterval = 0.012f;
    [SerializeField] private float particleLifetime = 0.6f;
    [SerializeField] private Sprite particleSprite;
    [SerializeField] private Color trailColor = Color.white;

    private float emitTimer;

    private static readonly Queue<TrailParticle> pool = new Queue<TrailParticle>();

    private void Update()
    {
        if (owner == null || owner.Body == null) return;
        float velocityMagnitude = owner.Body.Velocity.magnitude;
        if (velocityMagnitude < 0.05f) return;
        emitTimer += Time.deltaTime;
        if (emitTimer < emitInterval) return;
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
        p.Reset(particleLifetime, trailColor);
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
    private float lifetime = 0.6f;
    private float t;
    private SpriteRenderer sr;
    private Color baseColor;

    private void Awake() { sr = GetComponent<SpriteRenderer>(); }

    public void Reset(float life, Color color)
    {
        lifetime = life;
        t = 0f;
        baseColor = color;
        if (sr != null) { sr.color = color; }
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (sr != null)
        {
            var c = baseColor;
            c.a = Mathf.Clamp01(1f - t / lifetime);
            sr.color = c;
        }
        if (t >= lifetime) ThrusterTrail.ReturnToPool(this);
    }
}
