using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public class Meteor : MonoBehaviour
{
    [SerializeField] private float fallSpeedMin = 1.2f;
    [SerializeField] private float fallSpeedMax = 2.0f;
    [SerializeField] private float driftMax = 0.4f;
    [SerializeField] private float groundY = -7f;
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

    public int AliveVoxelCount => aliveCount;
    public bool IsAlive => !dead && aliveCount > 0 && gameObject.activeInHierarchy;

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
