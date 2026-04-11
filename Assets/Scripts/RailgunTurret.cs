using UnityEngine;

// TurretBase subclass for the railgun weapon. Reads a RailgunStats asset,
// fires RailgunRound projectiles in a straight line from the muzzle, and
// runs the 4-step quantized barrel charge color animation. Overrides Update
// because the charge-timer behavior differs from the base class's
// reloadTimer — railgun shows a visible "filling up" between shots.
public class RailgunTurret : TurretBase
{
    [SerializeField] private RailgunStats stats;
    [SerializeField] private RailgunRound roundPrefab;
    [SerializeField] private SpriteRenderer barrelSprite;

    // 4-step quantized charge color animation per the voxel aesthetic rules.
    // No smooth Color.Lerp — each step is a visible chunky transition.
    //   t = 0.00: dead white
    //   t = 0.25: first blue tint  #CEE8FE
    //   t = 0.50: mid blue          #A8D6FE
    //   t = 0.75: full charge       #93DAFE (held until fire)
    private static readonly Color[] ChargeStops = new Color[]
    {
        new Color(1f,    1f,    1f,    1f),    // dead white
        new Color(0.808f,0.910f,0.996f,1f),    // CEE8FE
        new Color(0.659f,0.839f,0.996f,1f),    // A8D6FE
        new Color(0.576f,0.855f,0.996f,1f),    // 93DAFE — full charge
    };

    private float chargeTimer;

    public RailgunStats Stats => stats;

    protected override float FireRate => stats != null ? stats.fireRate.CurrentValue : 0.2f;
    protected override float RotationSpeed => stats != null ? stats.rotationSpeed.CurrentValue : 20f;

    protected override void Awake()
    {
        base.Awake();
        // Start the barrel at dead white. Charge will fill in over time.
        if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
    }

    protected override void Update()
    {
        // Custom Update — replaces the base class's reloadTimer-driven loop.
        // Charge timer advances each frame, capped at chargeDuration. Barrel
        // color is set from the quantized stops based on charge progress.
        // Fire only when charge is full AND a target is aligned.
        float chargeDuration = 1f / Mathf.Max(0.05f, FireRate);
        chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, chargeDuration);

        float t = Mathf.Clamp01(chargeTimer / chargeDuration);
        int stopIdx = Mathf.Min(Mathf.FloorToInt(t * ChargeStops.Length), ChargeStops.Length - 1);
        if (barrelSprite != null) barrelSprite.color = ChargeStops[stopIdx];

        // Find a target. Reuse the base-class FindTarget directly.
        var target = FindTarget();
        if (target == null) return;

        // Rotate barrel toward the target — same logic as TurretBase.Update,
        // but inlined here because we override the whole Update method.
        Vector2 toTarget = (Vector2)(target.transform.position - barrel.position);
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, RotationSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0f, 0f, newAngle);

        // Fire only when charge is full AND barrel is aligned within tolerance.
        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (chargeTimer >= chargeDuration && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            chargeTimer = 0f;
            // Snap color back to dead white instantly — no fade.
            if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
        }
    }

    protected override void Fire(Meteor target)
    {
        if (roundPrefab == null)
        {
            Debug.LogError("[RailgunTurret] roundPrefab not assigned", this);
            return;
        }
        if (stats == null)
        {
            Debug.LogError("[RailgunTurret] stats not assigned", this);
            return;
        }

        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;
        Vector3 dir = barrel.up;

        var round = Instantiate(roundPrefab);
        round.Configure(
            spawnPos: spawnPos,
            dir: dir,
            speed: stats.speed.CurrentValue,
            weight: Mathf.RoundToInt(stats.weight.CurrentValue),
            caliber: Mathf.RoundToInt(stats.caliber.CurrentValue));

        if (muzzleFlash != null) muzzleFlash.Play();
    }
}
