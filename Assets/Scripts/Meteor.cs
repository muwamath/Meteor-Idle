using UnityEngine;

// Returned by Meteor.ApplyBlast and Meteor.ApplyTunnel so callers can pay
// money based on what was destroyed while still tracking weight-budget
// consumption per HP point (not per cell kill).
//
// Iter 2: per-material counts replace the dirt/core split. Legacy
// dirtDestroyed/coreDestroyed fields stay populated as a shim so any test
// code still asserting them keeps working. New callers should read
// totalPayout (sum of payoutPerCell across all destroyed cells) directly.
public struct DestroyResult
{
    // Legacy shim fields — populated alongside the new per-material counts
    // so Iter 1 test assertions keep working.
    public int dirtDestroyed;
    public int coreDestroyed;
    public int damageDealt; // total HP points subtracted, regardless of cell kills

    // Iter 2 per-material counts. Indexed by MaterialRegistry index.
    // Allocated lazily on first increment to keep the zero-result path
    // allocation-free. Caller-friendly accessors below.
    public int[] countByMaterialIndex;
    public int totalPayout;

    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
    public int TotalPayout => totalPayout;

    public int GetCount(VoxelMaterial m, MaterialRegistry registry)
    {
        if (countByMaterialIndex == null || registry == null) return 0;
        int idx = registry.IndexOf(m);
        if (idx < 0 || idx >= countByMaterialIndex.Length) return 0;
        return countByMaterialIndex[idx];
    }
}

[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public class Meteor : MonoBehaviour
{
    // Money paid out per core voxel destroyed. Dirt voxels pay nothing.
    // Iter 2 will lift this into a per-type asteroid field; until then it
    // lives here as a single named constant so future wiring is mechanical.
    public const int CoreBaseValue = 5;

    [SerializeField] private float fallSpeedMin = 0.4f;
    [SerializeField] private float fallSpeedMax = 0.67f;
    [SerializeField] private float driftMax = 0.4f;
    [SerializeField] private float groundY = -8.7f;
    // Below this Y the meteor becomes untargetable (turrets ignore it) and its
    // sprite fades from 1 → 0 over fadeDuration before despawning. This prevents
    // turrets from swinging sideways to track meteors drifting past the base.
    [SerializeField] private float fadeStartY = -7.88f;
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private ParticleSystem voxelChunkPrefab;

    // Iter 2: per-cell material data drives palette, HP, payout, targeting
    // tier, and behavior dispatch. When this is null (Iter 1 backward-compat),
    // the legacy dirt+core path runs and material[,] stays null.
    [SerializeField] private MaterialRegistry materialRegistry;

    // Iter 3: prefab spawned at a core cell's world position when that cell
    // is destroyed. Drones collect and deposit it; the meteor itself no
    // longer credits money directly for cores.
    [SerializeField] private CoreDrop coreDropPrefab;

    private SpriteRenderer sr;
    private CircleCollider2D col;
    private Vector2 velocity;
    private MeteorSpawner owner;

    // Parallel voxel state: kind[,] for what the cell is (Empty/Dirt/Core),
    // hp[,] for how many more hits before it clears. Phase 1 keeps instant-
    // clear behavior (dirt HP 1, cores also clear on first hit). Phase 2
    // will start decrementing hp instead of setting Empty outright, so
    // cores with HP > 1 survive multiple hits. All internal checks go
    // through kind[x,y] != VoxelKind.Empty for "alive" — hp is only read
    // when applying damage.
    private VoxelKind[,] kind;
    private int[,] hp;
    private VoxelMaterial[,] material;
    private Texture2D texture;

    // Iter 2: explosive cells whose HP just hit 0 in the current frame, queued
    // for next-frame detonation. Drained at the start of Update so chain
    // reactions span multiple frames and the cascade is visible to the player
    // (one frame per chain link). The 1-frame delay is intrinsic — drain
    // happens before any new ApplyBlast/ApplyTunnel calls land that frame, so
    // newly-queued explosives wait until the NEXT frame's drain.
    private readonly System.Collections.Generic.Queue<(int gx, int gy)> pendingDetonations
        = new System.Collections.Generic.Queue<(int, int)>();
    private Sprite sprite;
    private int aliveCount;
    private bool dead;
    private bool fading;
    private float fadeTimer;

    public int AliveVoxelCount => aliveCount;
    public Vector2 Velocity => velocity;
    public bool IsAlive => !dead && !fading && aliveCount > 0 && gameObject.activeInHierarchy;

    // True when at least one Core cell is still alive. Turrets only lock
    // onto meteors where this is true — dirt-only remnants are ignored and
    // the turret holds its last aim until a cored meteor comes into play.
    public bool HasLiveCore
    {
        get
        {
            if (kind == null) return false;
            for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    if (kind[x, y] == VoxelKind.Core) return true;
            return false;
        }
    }

    public int CoreVoxelCount
    {
        get
        {
            if (kind == null) return 0;
            int n = 0;
            for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    if (kind[x, y] == VoxelKind.Core) n++;
            return n;
        }
    }

    // Iter 2: generalization of HasLiveCore. True if any cell on this meteor
    // is targetable (its material has targetingTier > 0). Used by TurretBase
    // to decide if this meteor is worth aiming at.
    //
    // Backward compat: when material[,] is null (Iter 1 path), falls back to
    // the legacy core-only check. This keeps Iter 1 PlayMode tests passing
    // until they're migrated to inject a registry.
    public bool HasAnyTargetable
    {
        get
        {
            if (kind == null) return false;
            if (material == null) return HasLiveCore;
            for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
                for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                    if (kind[x, y] != VoxelKind.Empty
                        && material[x, y] != null
                        && material[x, y].targetingTier > 0)
                        return true;
            return false;
        }
    }

    // Iter 2: generalization of PickRandomCoreVoxel. Picks a random live cell
    // from the highest-priority tier present on this meteor (gold first, then
    // explosive, then core). Returns false only when no targetable cell exists.
    //
    // Backward compat: when material[,] is null, falls through to
    // PickRandomCoreVoxel so Iter 1 test code still works.
    public bool PickPriorityVoxel(out int gx, out int gy)
    {
        gx = 0; gy = 0;
        if (kind == null) return false;
        if (material == null) return PickRandomCoreVoxel(out gx, out gy);

        // Find the lowest tier > 0 that has any live cell on this meteor.
        int bestTier = int.MaxValue;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                var mat = material[x, y];
                if (mat == null || mat.targetingTier <= 0) continue;
                if (mat.targetingTier < bestTier) bestTier = mat.targetingTier;
            }
        if (bestTier == int.MaxValue) return false;

        // Pick uniformly across all live cells at that tier.
        int tierCount = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                var mat = material[x, y];
                if (mat == null || mat.targetingTier != bestTier) continue;
                tierCount++;
            }
        int targetIndex = Random.Range(0, tierCount);
        int seen = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                var mat = material[x, y];
                if (mat == null || mat.targetingTier != bestTier) continue;
                if (seen == targetIndex) { gx = x; gy = y; return true; }
                seen++;
            }
        }
        return false;
    }

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
    }

    public void Spawn(MeteorSpawner spawner, Vector3 position, int seed, float sizeScale)
    {
        owner = spawner;
        dead = false;
        fading = false;
        fadeTimer = 0f;
        transform.position = position;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one * sizeScale;

        ReleaseTexture();
        pendingDetonations.Clear();
        if (materialRegistry != null)
        {
            VoxelMeteorGenerator.Generate(
                seed, sizeScale, materialRegistry,
                out kind, out hp, out material, out texture, out aliveCount);
        }
        else
        {
            // Iter 1 backward-compat path: legacy two-kind output, material[,] left null.
            VoxelMeteorGenerator.Generate(seed, sizeScale, out kind, out hp, out texture, out aliveCount);
            material = null;
        }
        sprite = Sprite.Create(
            texture,
            new Rect(0, 0, VoxelMeteorGenerator.TextureSize, VoxelMeteorGenerator.TextureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        sr.sprite = sprite;
        sr.color = Color.white;

        float drift = Random.Range(-driftMax, driftMax);
        float fall  = Random.Range(fallSpeedMin, fallSpeedMax);
        velocity = new Vector2(drift, -fall);

        col.radius = 0.75f;
        col.offset = Vector2.zero;
    }

    private void Update()
    {
        if (dead) return;
        // Iter 2: drain pending explosive detonations queued in the previous
        // frame BEFORE moving the meteor. Each drain pass applies 1 damage to
        // all 8 neighbors of each pending cell; chain links to other
        // explosives queue for the NEXT frame so the cascade is visible.
        if (pendingDetonations.Count > 0) DrainPendingDetonations();
        if (dead) return; // detonation chain may have killed the meteor
        transform.position += (Vector3)(velocity * Time.deltaTime);

        if (!fading && transform.position.y < fadeStartY)
        {
            fading = true;
            fadeTimer = 0f;
        }

        if (fading)
        {
            fadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(fadeTimer / fadeDuration);
            if (sr != null)
            {
                var c = sr.color;
                c.a = 1f - t;
                sr.color = c;
            }
            if (t >= 1f)
            {
                ReturnSilently();
                return;
            }
        }

        if (transform.position.y < groundY)
            ReturnSilently();
    }

    public DestroyResult ApplyBlast(Vector3 worldImpactPoint, float worldRadius)
    {
        var result = new DestroyResult();
        if (dead || aliveCount == 0) return result;

        Vector3 local = transform.InverseTransformPoint(worldImpactPoint);
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);
        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        // Clamp to the valid cell-center range so rim-edge impacts snap onto the
        // nearest column/row instead of landing outside the grid.
        gx = Mathf.Clamp(gx, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);
        gy = Mathf.Clamp(gy, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);

        // The meteor's CircleCollider2D radius never shrinks as voxels are destroyed, so
        // a missile can trigger on an eroded rim that has no live voxels within the blast
        // circle. Walk the impact coordinates inward until we find a live voxel.
        WalkInwardToAliveCell(ref gx, ref gy);

        // Safety net: if the inward walk ended on a dead cell, fall back to the
        // nearest alive voxel anywhere in the grid.
        int snapX = Mathf.Clamp(Mathf.FloorToInt(gx), 0, VoxelMeteorGenerator.GridSize - 1);
        int snapY = Mathf.Clamp(Mathf.FloorToInt(gy), 0, VoxelMeteorGenerator.GridSize - 1);
        if (kind[snapX, snapY] == VoxelKind.Empty) SnapToNearestAliveCell(ref gx, ref gy);

        float gridRadius = worldRadius * localToGrid;

        int minX = Mathf.Max(0, Mathf.FloorToInt(gx - gridRadius));
        int maxX = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gx + gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(gy - gridRadius));
        int maxY = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gy + gridRadius));

        float r2 = gridRadius * gridRadius;
        bool anyPainted = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                float cx = x + 0.5f;
                float cy = y + 0.5f;
                float d2 = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
                if (d2 > r2) continue;

                // 1 HP of damage per blast coverage. Blast radius is the
                // damage-scaling mechanism — wider blasts hit more cells,
                // not harder. A core with HP > 1 survives a single blast.
                hp[x, y]--;
                result.damageDealt++;
                if (hp[x, y] > 0) continue;

                bool wasCore = kind[x, y] == VoxelKind.Core;
                var matHere = material != null ? material[x, y] : null;
                kind[x, y] = VoxelKind.Empty;
                VoxelMeteorGenerator.ClearVoxel(texture, x, y);
                anyPainted = true;
                aliveCount--;

                // Legacy shim fields — keeps Iter 1 test assertions valid.
                if (wasCore) result.coreDestroyed++;
                else         result.dirtDestroyed++;

                // Iter 2 per-material counts and payout sum.
                AccumulateDestroyed(ref result, matHere);

                // Iter 3: non-paying materials (cores) spawn a CoreDrop
                // instead of contributing to TotalPayout.
                if (matHere != null && !matHere.paysOnBreak)
                    SpawnCoreDrop(x, y, matHere);

                // Iter 2: explosive cells queue a chain detonation for next frame.
                if (matHere != null && matHere.behavior == MaterialBehavior.Explosive)
                    pendingDetonations.Enqueue((x, y));

                if (voxelChunkPrefab != null)
                {
                    Vector3 worldVoxel = VoxelCenterToWorld(x, y);
                    var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                    burst.Play();
                    Destroy(burst.gameObject, 1.5f);
                }
            }
        }

        if (anyPainted) texture.Apply();

        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }
        return result;
    }

    // Iter 2: bump per-material count and payout in DestroyResult.
    // Allocates the count array lazily on first hit so the zero-result
    // path stays allocation-free. Safe with null material (Iter 1 path).
    private void AccumulateDestroyed(ref DestroyResult result, VoxelMaterial mat)
    {
        if (mat == null || materialRegistry == null) return;
        if (result.countByMaterialIndex == null)
            result.countByMaterialIndex = new int[materialRegistry.materials.Length];
        int idx = materialRegistry.IndexOf(mat);
        if (idx < 0) return;
        result.countByMaterialIndex[idx]++;
        // Iter 3: only materials flagged paysOnBreak=true contribute to the
        // direct-payout sum. Cores (paysOnBreak=false) go through SpawnCoreDrop
        // and deposit via the collector drone loop.
        if (mat.paysOnBreak) result.totalPayout += mat.payoutPerCell;
    }

    // Iter 3: instantiate a CoreDrop at a just-destroyed core cell's world
    // position, carrying that material's payoutPerCell as the drop value.
    // Registers with GameManager so drones can find it. Graceful no-op if
    // the prefab is unwired (keeps Iter 1 test harnesses compiling).
    private void SpawnCoreDrop(int gx, int gy, VoxelMaterial mat)
    {
        if (coreDropPrefab == null) return;
        if (mat == null) return;
        Vector3 pos = VoxelCenterToWorld(gx, gy);
        var drop = Instantiate(coreDropPrefab, pos, Quaternion.identity);
        drop.Spawn(pos, mat.payoutPerCell);
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterDrop(drop);
    }

    // Iter 2: drain the pending-detonation queue. Each cell in the queue is
    // an explosive that died in a previous frame; this pass applies 1 damage
    // to all 8 neighbors. Newly-killed explosives go BACK on the queue (via
    // the enqueue branch below) so the chain ripples one frame at a time.
    //
    // Pays out for everything destroyed in the chain via GameManager.AddMoney
    // directly — this path runs from Update, not from a weapon's OnTrigger,
    // so there's no projectile to bubble payout up through.
    private void DrainPendingDetonations()
    {
        int snapshot = pendingDetonations.Count;
        bool anyPainted = false;
        var totalResult = new DestroyResult();

        for (int i = 0; i < snapshot; i++)
        {
            var cell = pendingDetonations.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cell.gx + dx;
                    int ny = cell.gy + dy;
                    if (nx < 0 || ny < 0
                        || nx >= VoxelMeteorGenerator.GridSize
                        || ny >= VoxelMeteorGenerator.GridSize) continue;
                    if (kind[nx, ny] == VoxelKind.Empty) continue;

                    hp[nx, ny]--;
                    totalResult.damageDealt++;
                    if (hp[nx, ny] > 0) continue;

                    bool wasCore = kind[nx, ny] == VoxelKind.Core;
                    var matHere = material != null ? material[nx, ny] : null;
                    kind[nx, ny] = VoxelKind.Empty;
                    VoxelMeteorGenerator.ClearVoxel(texture, nx, ny);
                    anyPainted = true;
                    aliveCount--;

                    if (wasCore) totalResult.coreDestroyed++;
                    else         totalResult.dirtDestroyed++;
                    AccumulateDestroyed(ref totalResult, matHere);

                    // Iter 3: non-paying materials (cores) spawn CoreDrops.
                    if (matHere != null && !matHere.paysOnBreak)
                        SpawnCoreDrop(nx, ny, matHere);

                    // Chain: a destroyed Explosive enqueues for the next frame.
                    if (matHere != null && matHere.behavior == MaterialBehavior.Explosive)
                        pendingDetonations.Enqueue((nx, ny));

                    if (voxelChunkPrefab != null)
                    {
                        Vector3 worldVoxel = VoxelCenterToWorld(nx, ny);
                        var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                        burst.Play();
                        Destroy(burst.gameObject, 1.5f);
                    }
                }
            }
        }

        if (anyPainted) texture.Apply();

        if (totalResult.totalPayout > 0 && GameManager.Instance != null)
            GameManager.Instance.AddMoney(totalResult.totalPayout);

        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }
    }

    // Line-walking voxel destruction for the railgun. Walks the grid along
    // worldDirection from entryWorld, destroying live voxels within a
    // perpendicular band of width caliberWidth (1 = 1 cell, 2 = 3 cells,
    // 3 = 5 cells). Budget is consumed per POINT OF DAMAGE DEALT (each hp
    // decrement), not per voxel cleared — so a core with HP 5 costs 5
    // budget to kill entirely, while a dirt voxel (HP 1) costs 1. Empty
    // cells are free and don't consume budget, preserving the "glide
    // through prior tunnel" feel.
    public DestroyResult ApplyTunnel(
        Vector3 entryWorld,
        Vector3 worldDirection,
        int budget,
        int caliberWidth,
        out Vector3 exitWorld)
    {
        var result = new DestroyResult();
        exitWorld = entryWorld;
        if (dead || aliveCount == 0 || budget <= 0) return result;

        Vector3 local = transform.InverseTransformPoint(entryWorld);
        Vector3 localDir = transform.InverseTransformDirection(worldDirection).normalized;
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);

        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        float dx = localDir.x;
        float dy = localDir.y;

        float perpX = -dy;
        float perpY = dx;
        int halfBand = Mathf.Max(0, caliberWidth - 1);

        bool anyPainted = false;
        int maxSteps = VoxelMeteorGenerator.GridSize * 4;
        bool hasEnteredGrid = false;

        for (int step = 0; step < maxSteps; step++)
        {
            if (budget <= 0) break;

            bool inGridNow =
                gx >= 0f && gx < VoxelMeteorGenerator.GridSize &&
                gy >= 0f && gy < VoxelMeteorGenerator.GridSize;
            if (inGridNow) hasEnteredGrid = true;

            for (int offset = -halfBand; offset <= halfBand; offset++)
            {
                float cellX = gx + perpX * offset;
                float cellY = gy + perpY * offset;
                int ix = Mathf.FloorToInt(cellX);
                int iy = Mathf.FloorToInt(cellY);
                if (ix < 0 || ix >= VoxelMeteorGenerator.GridSize) continue;
                if (iy < 0 || iy >= VoxelMeteorGenerator.GridSize) continue;
                if (kind[ix, iy] == VoxelKind.Empty) continue; // empty — free

                // 1 HP of damage = 1 unit of budget. Multi-HP cores cost
                // their full HP to kill.
                hp[ix, iy]--;
                budget--;
                result.damageDealt++;

                if (hp[ix, iy] > 0)
                {
                    if (budget <= 0) break;
                    continue;
                }

                bool wasCore = kind[ix, iy] == VoxelKind.Core;
                var matHere = material != null ? material[ix, iy] : null;
                kind[ix, iy] = VoxelKind.Empty;
                VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
                anyPainted = true;
                aliveCount--;

                if (wasCore) result.coreDestroyed++;
                else         result.dirtDestroyed++;
                AccumulateDestroyed(ref result, matHere);

                // Iter 3: non-paying materials (cores) spawn CoreDrops.
                if (matHere != null && !matHere.paysOnBreak)
                    SpawnCoreDrop(ix, iy, matHere);

                // Iter 2: explosive cells queue a chain detonation for next frame.
                if (matHere != null && matHere.behavior == MaterialBehavior.Explosive)
                    pendingDetonations.Enqueue((ix, iy));

                if (voxelChunkPrefab != null)
                {
                    Vector3 worldVoxel = VoxelCenterToWorld(ix, iy);
                    var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                    burst.Play();
                    Destroy(burst.gameObject, 1.5f);
                }

                if (budget <= 0) break;
            }

            gx += dx * 0.5f;
            gy += dy * 0.5f;

            if (hasEnteredGrid)
            {
                if (gx < -0.5f || gx >= VoxelMeteorGenerator.GridSize + 0.5f) break;
                if (gy < -0.5f || gy >= VoxelMeteorGenerator.GridSize + 0.5f) break;
            }
        }

        if (anyPainted) texture.Apply();

        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }

        Vector3 localExit = new Vector3(
            gx / localToGrid - halfExtent,
            gy / localToGrid - halfExtent,
            0f);
        exitWorld = transform.TransformPoint(localExit);

        return result;
    }

    private void SnapToNearestAliveCell(ref float gx, ref float gy)
    {
        int bestX = -1, bestY = -1;
        float bestD2 = float.MaxValue;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                float cx = x + 0.5f;
                float cy = y + 0.5f;
                float d2 = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestX = x;
                    bestY = y;
                }
            }
        }
        if (bestX >= 0)
        {
            gx = bestX + 0.5f;
            gy = bestY + 0.5f;
        }
    }

    private void WalkInwardToAliveCell(ref float gx, ref float gy)
    {
        const float center = VoxelMeteorGenerator.GridSize * 0.5f;
        float dx = center - gx;
        float dy = center - gy;
        float distToCenter = Mathf.Sqrt(dx * dx + dy * dy);
        if (distToCenter < 0.001f) return;

        float stepX = dx / distToCenter;
        float stepY = dy / distToCenter;
        int maxSteps = Mathf.CeilToInt(distToCenter * 2f);
        float cx = gx;
        float cy = gy;
        for (int i = 0; i <= maxSteps; i++)
        {
            int ix = Mathf.Clamp(Mathf.FloorToInt(cx), 0, VoxelMeteorGenerator.GridSize - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt(cy), 0, VoxelMeteorGenerator.GridSize - 1);
            if (kind[ix, iy] != VoxelKind.Empty)
            {
                gx = ix + 0.5f;
                gy = iy + 0.5f;
                return;
            }
            cx += stepX * 0.5f;
            cy += stepY * 0.5f;
        }
    }

    private Vector3 VoxelCenterToWorld(int gx, int gy)
    {
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);
        float lx = (gx + 0.5f) / localToGrid - halfExtent;
        float ly = (gy + 0.5f) / localToGrid - halfExtent;
        return transform.TransformPoint(new Vector3(lx, ly, 0f));
    }

    public Vector3 GetVoxelWorldPosition(int gx, int gy) => VoxelCenterToWorld(gx, gy);

    public bool IsVoxelPresent(int gx, int gy)
    {
        if (kind == null) return false;
        if (gx < 0 || gy < 0 || gx >= VoxelMeteorGenerator.GridSize || gy >= VoxelMeteorGenerator.GridSize) return false;
        return kind[gx, gy] != VoxelKind.Empty;
    }

    // Targeting primitive: pick a random live CORE cell. Returns false when
    // no live cores exist — the turret then holds fire (or looks for a
    // different target). Mirrors PickRandomPresentVoxel's counting-then-
    // indexing pattern so a uniform distribution across live cores is cheap
    // on the 10x10 grid.
    public bool PickRandomCoreVoxel(out int gx, out int gy)
    {
        gx = 0; gy = 0;
        if (kind == null) return false;

        int coreCount = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                if (kind[x, y] == VoxelKind.Core) coreCount++;

        if (coreCount == 0) return false;

        int targetIndex = Random.Range(0, coreCount);
        int seen = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] != VoxelKind.Core) continue;
                if (seen == targetIndex)
                {
                    gx = x; gy = y;
                    return true;
                }
                seen++;
            }
        }
        return false;
    }

    public bool PickRandomPresentVoxel(out int gx, out int gy)
    {
        gx = 0; gy = 0;
        if (kind == null) return false;

        // Count present cells directly instead of trusting aliveCount. Cheap on a 10x10 grid
        // and defensive against any future drift between aliveCount and the voxel grid.
        int liveCount = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                if (kind[x, y] != VoxelKind.Empty) liveCount++;

        if (liveCount == 0) return false;

        int targetIndex = Random.Range(0, liveCount);
        int seen = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                if (seen == targetIndex)
                {
                    gx = x; gy = y;
                    return true;
                }
                seen++;
            }
        }
        return false;
    }

    private void ReturnSilently()
    {
        dead = true;
        owner?.Release(this);
    }

    private void OnDisable()
    {
        ReleaseTexture();
    }

    private void ReleaseTexture()
    {
        if (sr != null) sr.sprite = null;
        if (sprite != null) { Destroy(sprite); sprite = null; }
        if (texture != null) { Destroy(texture); texture = null; }
    }
}
