using System.Collections.Generic;
using UnityEngine;

// Visual-only projectile fired by the railgun. No Rigidbody2D, no Collider2D
// — damage is resolved via per-frame Physics2D.RaycastAll against the
// `Meteors` physics layer. Each frame the round advances along its direction
// by speed * deltaTime, and that same delta is the raycast distance — so we
// always cover the gap we just moved through (manual continuous collision).
// Works at any speed from base 6 world/sec to fully-upgraded ~36 world/sec
// without tunneling, and missiles never come back from the raycast because
// they're on the Default layer.
public class RailgunRound : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private RailgunStreak streakPrefab;

    private Vector3 direction;
    private float speed;
    private int remainingWeight;
    private int caliber;
    private Vector3 spawnPoint;
    private readonly HashSet<Meteor> alreadyTunneled = new HashSet<Meteor>();
    private int meteorLayerMask;

    public void Configure(Vector3 spawnPos, Vector3 dir, float speed, int weight, int caliber)
    {
        transform.position = spawnPos;
        spawnPoint = spawnPos;
        direction = dir.normalized;
        this.speed = speed;
        remainingWeight = weight;
        this.caliber = caliber;
        alreadyTunneled.Clear();

        // Orient the bullet sprite so its long axis is along travel.
        // The bullet texture is taller than wide, so the "up" of the sprite
        // (local +y) is the forward direction.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void Awake()
    {
        int layer = LayerMask.NameToLayer("Meteors");
        if (layer < 0)
        {
            // Fail loud rather than fall back to ~0: a wide mask would let
            // raycasts hit missiles on the Default layer and silently violate
            // the layer-isolation invariant the railgun depends on.
            Debug.LogError("[RailgunRound] Meteors layer not defined — disabling round", this);
            enabled = false;
            return;
        }
        meteorLayerMask = 1 << layer;
    }

    private void Update()
    {
        if (remainingWeight <= 0) { Despawn(); return; }

        float stepDistance = speed * Time.deltaTime;
        if (stepDistance <= 0f) return;

        var hits = Physics2D.RaycastAll(
            transform.position,
            (Vector2)direction,
            stepDistance,
            meteorLayerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (remainingWeight <= 0) break;
            var meteor = hit.collider.GetComponentInParent<Meteor>();
            if (meteor == null || !meteor.IsAlive) continue;
            if (alreadyTunneled.Contains(meteor)) continue;

            var result = meteor.ApplyTunnel(
                entryWorld: hit.point,
                worldDirection: direction,
                budget: remainingWeight,
                caliberWidth: caliber,
                out _);
            // Budget consumed = damage dealt (every HP point costs 1 budget).
            // Using result.damageDealt instead of TotalDestroyed is critical:
            // a multi-HP core can absorb several points of damage without
            // dying, and those still cost the round its budget.
            int damageDealt = result.damageDealt;
            remainingWeight -= damageDealt;
            alreadyTunneled.Add(meteor);

            // Iter 2: per-material economy. result.TotalPayout sums
            // payoutPerCell across every destroyed cell — dirt and stone
            // pay 0 (free pass-through), gold/core/explosive each contribute
            // their own value. A pure-dirt tunnel pays nothing.
            int payout = result.TotalPayout;
            if (payout > 0 && GameManager.Instance != null)
                GameManager.Instance.AddMoney(payout);
        }

        transform.position += direction * stepDistance;

        if (OffScreen(transform.position)) { Despawn(); return; }
    }

    private void Despawn()
    {
        if (streakPrefab != null)
        {
            var streak = Instantiate(streakPrefab);
            streak.Configure(spawnPoint, transform.position, caliber);
        }
        Destroy(gameObject);
    }

    private static bool OffScreen(Vector3 pos) =>
        pos.y > 10f || pos.y < -10f || Mathf.Abs(pos.x) > 17f;
}
