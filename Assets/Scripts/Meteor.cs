using UnityEngine;

// Returned by Meteor.ApplyBlast and Meteor.ApplyTunnel so callers can pay
// money based on core destruction while still reporting total for visuals
// and tracking weight-budget consumption on every HP point (not just cell
// kills). A struct instead of a tuple so call sites read naturally at
// every use and the field names survive refactors.
public struct DestroyResult
{
    public int dirtDestroyed;
    public int coreDestroyed;
    public int damageDealt; // total HP points subtracted, regardless of cell kills
    public int TotalDestroyed => dirtDestroyed + coreDestroyed;
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
    private Texture2D texture;
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
        VoxelMeteorGenerator.Generate(seed, sizeScale, out kind, out hp, out texture, out aliveCount);
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
                kind[x, y] = VoxelKind.Empty;
                VoxelMeteorGenerator.ClearVoxel(texture, x, y);
                anyPainted = true;
                aliveCount--;

                if (wasCore) result.coreDestroyed++;
                else         result.dirtDestroyed++;

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
                kind[ix, iy] = VoxelKind.Empty;
                VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
                anyPainted = true;
                aliveCount--;

                if (wasCore) result.coreDestroyed++;
                else         result.dirtDestroyed++;

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
