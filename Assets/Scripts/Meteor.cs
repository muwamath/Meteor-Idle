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
        // requires kind[x,y] != Empty.
        gx = Mathf.Clamp(gx, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);
        gy = Mathf.Clamp(gy, 0.5f, VoxelMeteorGenerator.GridSize - 0.5f);

        // The meteor's CircleCollider2D radius never shrinks as voxels are destroyed, so
        // a missile can trigger on an eroded rim that has no live voxels within the blast
        // circle — landing a "hit in empty space". Walk the impact coordinates inward
        // (toward the meteor center) until we find a live voxel, then blast there.
        WalkInwardToAliveCell(ref gx, ref gy);

        // Safety net: if the inward walk ended on a dead cell, fall back to the
        // nearest alive voxel anywhere in the grid. aliveCount > 0 is guaranteed
        // by the early return above.
        int snapX = Mathf.Clamp(Mathf.FloorToInt(gx), 0, VoxelMeteorGenerator.GridSize - 1);
        int snapY = Mathf.Clamp(Mathf.FloorToInt(gy), 0, VoxelMeteorGenerator.GridSize - 1);
        if (kind[snapX, snapY] == VoxelKind.Empty) SnapToNearestAliveCell(ref gx, ref gy);

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
                if (kind[x, y] == VoxelKind.Empty) continue;
                float cx = x + 0.5f;
                float cy = y + 0.5f;
                float d2 = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
                if (d2 > r2) continue;

                // Phase 1 keeps instant-clear semantics; Phase 2 will switch
                // to hp[x,y]-- and only clear on hp <= 0.
                kind[x, y] = VoxelKind.Empty;
                hp[x, y] = 0;
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

        float perpX = -dy;
        float perpY = dx;
        int halfBand = Mathf.Max(0, caliberWidth - 1);

        int consumed = 0;
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

                // Phase 1 instant-clear; Phase 2 will decrement hp first.
                kind[ix, iy] = VoxelKind.Empty;
                hp[ix, iy] = 0;
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

            gx += dx * 0.5f;
            gy += dy * 0.5f;

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

        Vector3 localExit = new Vector3(
            gx / localToGrid - halfExtent,
            gy / localToGrid - halfExtent,
            0f);
        exitWorld = transform.TransformPoint(localExit);

        return consumed;
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
