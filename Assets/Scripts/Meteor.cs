using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public class Meteor : MonoBehaviour
{
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

    private bool[,] voxels;
    private Texture2D texture;
    private Sprite sprite;
    private int aliveCount;
    private bool dead;
    private bool fading;
    private float fadeTimer;

    public int AliveVoxelCount => aliveCount;
    public Vector2 Velocity => velocity;
    public bool IsAlive => !dead && !fading && aliveCount > 0 && gameObject.activeInHierarchy;

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
        VoxelMeteorGenerator.Generate(seed, out voxels, out texture, out aliveCount);
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

    public int ApplyBlast(Vector3 worldImpactPoint, float worldRadius)
    {
        if (dead || aliveCount == 0) return 0;

        Vector3 local = transform.InverseTransformPoint(worldImpactPoint);
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);
        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        // Clamp to the valid cell-center range so rim-edge impacts snap onto the
        // nearest column/row instead of landing outside the grid. Legitimate
        // pass-through-hole misses still return 0 because the cell check still
        // requires voxels[x,y] == true.
        gx = Mathf.Clamp(gx, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);
        gy = Mathf.Clamp(gy, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);

        // The meteor's CircleCollider2D radius never shrinks as voxels are destroyed, so
        // a missile can trigger on an eroded rim that has no live voxels within the blast
        // circle — landing a "hit in empty space". Walk the impact coordinates inward
        // (toward the meteor center) until we find a live voxel, then blast there. This
        // produces an outside-rim crater on the first live chunk along the missile's
        // entry path.
        WalkInwardToAliveCell(ref gx, ref gy);

        // Safety net: if the inward walk ended on a dead cell (the impact landed in
        // a fully-bored tunnel where no live voxel sits along the ray to center), fall
        // back to the nearest alive voxel anywhere in the grid. A missile must always
        // damage the meteor it collides with — the user's requirement is "never pass
        // through". aliveCount > 0 is guaranteed by the early return above.
        int snapX = Mathf.Clamp(Mathf.FloorToInt(gx), 0, VoxelMeteorGenerator.GridSize - 1);
        int snapY = Mathf.Clamp(Mathf.FloorToInt(gy), 0, VoxelMeteorGenerator.GridSize - 1);
        if (!voxels[snapX, snapY]) SnapToNearestAliveCell(ref gx, ref gy);
        // Scale-invariant: the blast covers the same number of grid cells on any
        // size meteor. Without this, big meteors get proportionally smaller blasts
        // (in grid units) and miss outer columns that small meteors would hit.
        float gridRadius = worldRadius * localToGrid;

        int minX = Mathf.Max(0, Mathf.FloorToInt(gx - gridRadius));
        int maxX = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gx + gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(gy - gridRadius));
        int maxY = Mathf.Min(VoxelMeteorGenerator.GridSize - 1, Mathf.CeilToInt(gy + gridRadius));

        float r2 = gridRadius * gridRadius;
        int destroyed = 0;
        bool anyPainted = false;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!voxels[x, y]) continue;
                float cx = x + 0.5f;
                float cy = y + 0.5f;
                float d2 = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
                if (d2 > r2) continue;

                voxels[x, y] = false;
                VoxelMeteorGenerator.ClearVoxel(texture, x, y);
                anyPainted = true;
                destroyed++;

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

        aliveCount -= destroyed;
        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }
        return destroyed;
    }

    // Line-walking voxel destruction for the railgun. Walks the grid along
    // worldDirection from entryWorld, destroying live voxels within a
    // perpendicular band of width caliberWidth (1 = 1 cell, 2 = 3 cells,
    // 3 = 5 cells). Each live cell destroyed consumes 1 from the budget.
    // Empty cells are free — the round glides past without losing budget.
    // Stops when budget hits 0 or the ray exits the grid. Returns the number
    // of voxels actually destroyed and, via out param, the world position
    // where the walk terminated (used by RailgunRound to continue to the
    // next meteor with remaining budget).
    public int ApplyTunnel(
        Vector3 entryWorld,
        Vector3 worldDirection,
        int budget,
        int caliberWidth,
        out Vector3 exitWorld)
    {
        exitWorld = entryWorld;
        if (dead || aliveCount == 0 || budget <= 0) return 0;

        Vector3 local = transform.InverseTransformPoint(entryWorld);
        Vector3 localDir = transform.InverseTransformDirection(worldDirection).normalized;
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);

        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        float dx = localDir.x;
        float dy = localDir.y;

        // Perpendicular direction (2D rotation by 90°) for caliber width.
        float perpX = -dy;
        float perpY = dx;
        int halfBand = Mathf.Max(0, caliberWidth - 1); // 0, 1, 2 for caliber 1, 2, 3

        int consumed = 0;
        bool anyPainted = false;
        // GridSize*4 = 40 half-cell steps = 20 cells of walk distance, enough
        // to traverse the full 10-cell grid even when entry is several cells
        // outside the boundary (common: missiles hit the circle collider at
        // world distance ~0.75 from center, which lands outside the square
        // voxel grid by ~5 cells along the approach direction).
        int maxSteps = VoxelMeteorGenerator.GridSize * 4;

        // Track whether the walker has entered the grid yet. Entry points can
        // start outside the grid (that's the common case — see note above);
        // we only want to terminate when we've been INSIDE and then left the
        // other side. Without this flag, entries below the grid would break
        // out on the first step without ever destroying anything.
        bool hasEnteredGrid = false;

        for (int step = 0; step < maxSteps; step++)
        {
            if (budget <= 0) break;

            bool inGridNow =
                gx >= 0f && gx < VoxelMeteorGenerator.GridSize &&
                gy >= 0f && gy < VoxelMeteorGenerator.GridSize;
            if (inGridNow) hasEnteredGrid = true;

            // Destroy all live cells within the perpendicular band at this step.
            for (int offset = -halfBand; offset <= halfBand; offset++)
            {
                float cellX = gx + perpX * offset;
                float cellY = gy + perpY * offset;
                int ix = Mathf.FloorToInt(cellX);
                int iy = Mathf.FloorToInt(cellY);
                if (ix < 0 || ix >= VoxelMeteorGenerator.GridSize) continue;
                if (iy < 0 || iy >= VoxelMeteorGenerator.GridSize) continue;
                if (!voxels[ix, iy]) continue; // empty — free, doesn't consume budget

                voxels[ix, iy] = false;
                VoxelMeteorGenerator.ClearVoxel(texture, ix, iy);
                anyPainted = true;
                consumed++;
                budget--;

                if (voxelChunkPrefab != null)
                {
                    Vector3 worldVoxel = VoxelCenterToWorld(ix, iy);
                    var burst = Instantiate(voxelChunkPrefab, worldVoxel, Quaternion.identity);
                    burst.Play();
                    Destroy(burst.gameObject, 1.5f);
                }

                if (budget <= 0) break;
            }

            // Advance half a cell along the ray (sub-cell steps so adjacent
            // cells along the direction both get checked).
            gx += dx * 0.5f;
            gy += dy * 0.5f;

            // Only terminate once we've been inside the grid AND have now
            // walked back out the other side. Entries from outside keep
            // walking until they enter.
            if (hasEnteredGrid)
            {
                if (gx < -0.5f || gx >= VoxelMeteorGenerator.GridSize + 0.5f) break;
                if (gy < -0.5f || gy >= VoxelMeteorGenerator.GridSize + 0.5f) break;
            }
        }

        if (anyPainted) texture.Apply();

        aliveCount -= consumed;
        if (aliveCount <= 0)
        {
            dead = true;
            owner?.Release(this);
        }

        // Report where the walk terminated in world space — used by the
        // railgun round to compute where to continue when piercing to the
        // next meteor.
        Vector3 localExit = new Vector3(
            gx / localToGrid - halfExtent,
            gy / localToGrid - halfExtent,
            0f);
        exitWorld = transform.TransformPoint(localExit);

        return consumed;
    }

    // Full-grid scan for the alive voxel closest to (gx, gy). Only called as a
    // last-resort fallback when the walk-inward search ended on a dead cell —
    // guarantees that a missile collision always lands on some live voxel so we
    // never return 0 destroyed on a valid hit.
    private void SnapToNearestAliveCell(ref float gx, ref float gy)
    {
        int bestX = -1, bestY = -1;
        float bestD2 = float.MaxValue;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (!voxels[x, y]) continue;
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

    // Walk (gx, gy) from the contact point toward the meteor center, stopping at the
    // first alive voxel encountered. This keeps the blast on the outside rim at the
    // missile's entry side — if the near rim is intact we stay there; if the near rim
    // has been eroded, the blast shifts inward by however much has been carved away,
    // hitting the next surviving layer from the same side. The walk stops at the center,
    // so a missile that grazes a fully-hollowed-out meteor can't reach through to the
    // far side.
    private void WalkInwardToAliveCell(ref float gx, ref float gy)
    {
        const float center = VoxelMeteorGenerator.GridSize * 0.5f;
        float dx = center - gx;
        float dy = center - gy;
        float distToCenter = Mathf.Sqrt(dx * dx + dy * dy);
        if (distToCenter < 0.001f) return;

        float stepX = dx / distToCenter;
        float stepY = dy / distToCenter;
        // Step in 0.5-cell increments so adjacent cells along the ray are both checked.
        int maxSteps = Mathf.CeilToInt(distToCenter * 2f);
        float cx = gx;
        float cy = gy;
        for (int i = 0; i <= maxSteps; i++)
        {
            int ix = Mathf.Clamp(Mathf.FloorToInt(cx), 0, VoxelMeteorGenerator.GridSize - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt(cy), 0, VoxelMeteorGenerator.GridSize - 1);
            if (voxels[ix, iy])
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
        if (voxels == null) return false;
        if (gx < 0 || gy < 0 || gx >= VoxelMeteorGenerator.GridSize || gy >= VoxelMeteorGenerator.GridSize) return false;
        return voxels[gx, gy];
    }

    public bool PickRandomPresentVoxel(out int gx, out int gy)
    {
        gx = 0; gy = 0;
        if (voxels == null) return false;

        // Count present cells directly instead of trusting aliveCount. Cheap on a 10x10 grid
        // and defensive against any future drift between aliveCount and the voxel grid.
        int liveCount = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
                if (voxels[x, y]) liveCount++;

        if (liveCount == 0) return false;

        int targetIndex = Random.Range(0, liveCount);
        int seen = 0;
        for (int y = 0; y < VoxelMeteorGenerator.GridSize; y++)
        {
            for (int x = 0; x < VoxelMeteorGenerator.GridSize; x++)
            {
                if (!voxels[x, y]) continue;
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
