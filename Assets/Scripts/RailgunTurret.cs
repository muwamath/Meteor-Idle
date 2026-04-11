using UnityEngine;
using UnityEngine.Serialization;

// TurretBase subclass for the railgun weapon. Reads a RailgunStats asset,
// fires RailgunRound projectiles in a straight line from the muzzle, and
// runs the 4-step quantized barrel charge color animation. Overrides Update
// because the charge-timer behavior differs from the base class's
// reloadTimer — railgun shows a visible "filling up" between shots.
public class RailgunTurret : TurretBase
{
    [FormerlySerializedAs("stats")]
    [SerializeField] private RailgunStats statsTemplate;
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
    private RailgunStats statsInstance;

    // Cached aim voxel — the specific live voxel on the current target the
    // barrel is rotating toward and will shoot at. Picked via
    // Meteor.PickRandomPresentVoxel once per shot cycle. Re-picked when: no
    // voxel cached, target changed, the cached voxel died, or we just fired.
    //
    // This exists because the railgun's straight-line shot was previously
    // aimed at Meteor.transform.position (the meteor's center). After a shot
    // carves a tunnel through the center, subsequent shots aimed at that same
    // center would fly through the (now-dead) tunnel without hitting any live
    // voxels — the meteor would sit there partially destroyed forever.
    // Aiming at a random LIVE voxel each shot guarantees the round's ray
    // crosses still-present voxels, so the walker in Meteor.ApplyTunnel
    // actually consumes cells and damage continues.
    private Meteor aimVoxelTarget;
    private int aimVoxelGx;
    private int aimVoxelGy;
    private bool hasAimVoxel;

    public RailgunStats Stats => statsInstance;

    protected override float FireRate => statsInstance != null ? statsInstance.fireRate.CurrentValue : 0.2f;
    protected override float RotationSpeed => statsInstance != null ? statsInstance.rotationSpeed.CurrentValue : 20f;
    protected override float ProjectileSpeed => statsInstance != null ? statsInstance.speed.CurrentValue : 6f;

    protected override void Awake()
    {
        base.Awake();
        if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
        if (statsInstance == null && statsTemplate != null)
            statsInstance = Instantiate(statsTemplate);
    }

    public override void InitializeForBuild()
    {
        if (statsInstance != null) Destroy(statsInstance);
        if (statsTemplate != null) statsInstance = Instantiate(statsTemplate);
        chargeTimer = 0f;
        if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
        aimVoxelTarget = null;
        hasAimVoxel = false;
    }

    private void OnDestroy()
    {
        if (statsInstance != null) Destroy(statsInstance);
    }

    protected override void Update()
    {
        float chargeDuration = 1f / Mathf.Max(0.05f, FireRate);
        chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, chargeDuration);

        float t = Mathf.Clamp01(chargeTimer / chargeDuration);
        int stopIdx = Mathf.Min(Mathf.FloorToInt(t * ChargeStops.Length), ChargeStops.Length - 1);
        if (barrelSprite != null) barrelSprite.color = ChargeStops[stopIdx];

        var target = FindTarget();
        if (target == null)
        {
            aimVoxelTarget = null;
            hasAimVoxel = false;
            return;
        }

        RefreshAimVoxel(target);

        Vector2 aimWorld = hasAimVoxel
            ? (Vector2)target.GetVoxelWorldPosition(aimVoxelGx, aimVoxelGy)
            : (Vector2)target.transform.position;
        Vector2 aimPoint = AimSolver.PredictInterceptPoint(
            (Vector2)barrel.position,
            aimWorld,
            target.Velocity,
            ProjectileSpeed);
        Vector2 toTarget = aimPoint - (Vector2)barrel.position;
        float desiredAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg - 90f;
        float currentAngle = barrel.eulerAngles.z;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, RotationSpeed * Time.deltaTime);
        barrel.rotation = Quaternion.Euler(0f, 0f, newAngle);

        float alignmentErr = Mathf.Abs(Mathf.DeltaAngle(newAngle, desiredAngle));
        if (chargeTimer >= chargeDuration && alignmentErr <= aimAlignmentDeg)
        {
            Fire(target);
            chargeTimer = 0f;
            if (barrelSprite != null) barrelSprite.color = ChargeStops[0];
            // Invalidate the cached aim voxel so the next Update picks a
            // fresh one. Without this, subsequent shots would re-target the
            // same voxel (or, if it's now dead, the meteor center — which is
            // exactly the tunnel-through bug we're avoiding).
            aimVoxelTarget = null;
            hasAimVoxel = false;
        }
    }

    // Pick a random live voxel on target to aim at. Re-pick only when we
    // need to (no voxel, different target, or the voxel died between ticks).
    // Called once per Update tick so the barrel rotates smoothly toward a
    // stable point rather than jittering between randomly-picked voxels
    // every frame.
    private void RefreshAimVoxel(Meteor target)
    {
        bool needPick =
            !hasAimVoxel ||
            target != aimVoxelTarget ||
            !target.IsVoxelPresent(aimVoxelGx, aimVoxelGy);

        if (!needPick) return;

        aimVoxelTarget = target;
        hasAimVoxel = target.PickRandomPresentVoxel(out aimVoxelGx, out aimVoxelGy);
    }

    protected override void Fire(Meteor target)
    {
        if (roundPrefab == null)
        {
            Debug.LogError("[RailgunTurret] roundPrefab not assigned", this);
            return;
        }
        if (statsInstance == null)
        {
            Debug.LogError("[RailgunTurret] stats not assigned", this);
            return;
        }

        Vector3 spawnPos = muzzle != null ? muzzle.position : barrel.position;

        // Single source of truth for projectile speed: the ProjectileSpeed
        // getter. Both the lead-aim solver call and the round's configured
        // flight speed must come from the same value or the round will aim
        // at a point it can't actually reach at the assumed speed — over-
        // or under-lead. Do not read statsInstance directly here.
        float projectileSpeed = ProjectileSpeed;

        // Aim at the same live voxel the Update step was rotating toward.
        // Falls back to meteor center only if no live voxels exist or the
        // cached voxel just died (rare race — the Update step picks one
        // right before calling Fire, so we expect hasAimVoxel to be true
        // almost always). Shooting at the meteor center is the original
        // tunnel-through bug path — only take it when there's literally no
        // other choice.
        Vector2 aimWorld;
        if (hasAimVoxel && target.IsVoxelPresent(aimVoxelGx, aimVoxelGy))
        {
            aimWorld = (Vector2)target.GetVoxelWorldPosition(aimVoxelGx, aimVoxelGy);
        }
        else
        {
            aimWorld = (Vector2)target.transform.position;
        }

        // Recompute the lead point at fire time — the barrel may have just
        // finished rotating this frame, and the target may have moved since
        // the Update-step aim. Using the fresh value keeps fire direction and
        // final aim in sync.
        Vector2 leadPoint = AimSolver.PredictInterceptPoint(
            (Vector2)spawnPos,
            aimWorld,
            target.Velocity,
            projectileSpeed);
        Vector2 dir2 = (leadPoint - (Vector2)spawnPos).normalized;
        if (dir2.sqrMagnitude < 0.0001f) dir2 = (Vector2)barrel.up;
        Vector3 dir = new Vector3(dir2.x, dir2.y, 0f);

        var round = Instantiate(roundPrefab);
        round.Configure(
            spawnPos: spawnPos,
            dir: dir,
            speed: projectileSpeed,
            weight: Mathf.RoundToInt(statsInstance.weight.CurrentValue),
            caliber: Mathf.RoundToInt(statsInstance.caliber.CurrentValue));

        if (muzzleFlash != null) muzzleFlash.Play();
    }
}
