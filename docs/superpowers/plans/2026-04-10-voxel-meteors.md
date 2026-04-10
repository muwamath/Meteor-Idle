# Voxel Meteors + Voxel Art Pass — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace smooth-sprite meteors with a voxel-grid model that can be partially destroyed by missile hits, each destroyed voxel paying $1 directly. Swap the smooth missile trail for pixel sparks, voxelify the turret/missile/background art, and cut the spawn rate ~3×.

**Architecture:** Each meteor owns a `bool[10,10]` grid and its own `Texture2D`. On missile impact, `Meteor.ApplyBlast(worldPos, radius)` computes which cells fall inside the blast circle, marks them destroyed, paints their texture blocks transparent, and returns the destroyed count for the missile to pay out. All other changes (art, particles, missile prefab, spawn timing) are adjustments around this core model.

**Tech Stack:** Unity 6000.4.1f1, 2D URP, C# (no assembly definitions), Unity MCP for editor automation via `execute_code`, PNG art generated procedurally by editor scripts.

**Spec reference:** `docs/superpowers/specs/2026-04-10-voxel-meteors-design.md`

**Testing strategy:** Verification is manual in-editor (spec §11 is the checklist), not unit tests. After each code task, the verification step is: `refresh_unity → read_console (expect no errors) → enter play mode → screenshot → read_console again → stop play mode`. "Passing" means console is clean and the screenshot matches the expected visual state.

---

## File Structure

### New files
- `Assets/Scripts/VoxelMeteorGenerator.cs` — static generator: `Generate(seed, out bool[,] grid, out Texture2D texture)`. One purpose: turn a seed into a voxel grid and its initial texture.

### Rewritten
- `Assets/Scripts/Meteor.cs` — now holds voxel state and texture ownership. Removes HP/reward. New public: `ApplyBlast(Vector3, float) → int`, `AliveVoxelCount`.

### Modified
- `Assets/Scripts/Missile.cs` — `OnTriggerEnter2D` now calls `ApplyBlast`, removes the `OverlapCircleAll` splash loop, pays money from the return count.
- `Assets/Scripts/MeteorSpawner.cs` — default spawn timing tuned.

### Deleted
- `Assets/Scripts/MeteorShapeGenerator.cs` — superseded by `VoxelMeteorGenerator`.

### Regenerated art (editor script writes PNGs to disk, reimports as Sprites)
- `Assets/Art/turret_base.png`
- `Assets/Art/turret_barrel.png`
- `Assets/Art/missile.png`
- `Assets/Art/background.png`

### Updated assets (editor script)
- `Assets/Art/ParticleMat.mat` — material's main texture swapped to `square.png`.
- `Assets/Prefabs/Missile.prefab` — remove TrailRenderer child, add sparks ParticleSystem child.
- `Assets/Prefabs/DebrisBurst.prefab`, `ExplosionBurst.prefab`, `MuzzleFlash.prefab` — retuned particle sizes/colors to fit square sprite.

### Untouched
- `Assets/Scripts/GameManager.cs`, `SimplePool.cs`, `Turret.cs`, `FloatingText.cs`, UI scripts, `TurretStats.cs`, `TurretStats.asset`, scene hierarchy for Game.unity (no new GameObjects needed).

---

## Task 1: New VoxelMeteorGenerator

**Files:**
- Create: `Assets/Scripts/VoxelMeteorGenerator.cs`

Additive — does not break any existing code.

- [ ] **Step 1: Create the generator file**

Write `Assets/Scripts/VoxelMeteorGenerator.cs` with exact content:

```csharp
using UnityEngine;

public static class VoxelMeteorGenerator
{
    public const int GridSize = 10;
    public const int VoxelPixelSize = 15;
    public const int TextureSize = GridSize * VoxelPixelSize;

    private static readonly Color TopColor    = new Color(0.545f, 0.451f, 0.333f, 1f); // #8B7355
    private static readonly Color BottomColor = new Color(0.290f, 0.227f, 0.165f, 1f); // #4A3A2A

    public static void Generate(int seed, out bool[,] grid, out Texture2D texture, out int aliveCount)
    {
        grid = new bool[GridSize, GridSize];
        var rng = new System.Random(seed);

        // Noise-perturbed disk shape
        float phase1 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase2 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase3 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float amp1 = 0.55f + (float)rng.NextDouble() * 0.25f;
        float amp2 = 0.25f + (float)rng.NextDouble() * 0.20f;
        float amp3 = 0.10f + (float)rng.NextDouble() * 0.10f;

        const float baseRadius = 4.5f;
        const float lumpAmp = 0.25f;
        Vector2 center = new Vector2(4.5f, 4.5f);

        aliveCount = 0;
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float theta = Mathf.Atan2(dy, dx);
                float lump =
                    amp1 * Mathf.Sin(theta * 3f + phase1) +
                    amp2 * Mathf.Sin(theta * 5f + phase2) +
                    amp3 * Mathf.Sin(theta * 9f + phase3);
                float radius = baseRadius * (1f + lumpAmp * lump);
                if (dist <= radius)
                {
                    grid[x, y] = true;
                    aliveCount++;
                }
            }
        }

        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"VoxelMeteor_{seed}"
        };

        // Initialize fully transparent
        var clear = new Color[TextureSize * TextureSize];
        texture.SetPixels(clear);

        // Paint each present voxel
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (grid[x, y])
                    PaintVoxel(texture, x, y);

        texture.Apply();
    }

    public static void PaintVoxel(Texture2D tex, int gx, int gy)
    {
        int px0 = gx * VoxelPixelSize;
        int py0 = gy * VoxelPixelSize;

        // Row gradient: top row brighter, bottom row darker
        float t = (float)gy / (GridSize - 1);
        Color baseCol = Color.Lerp(BottomColor, TopColor, t);
        Color hi = Color.Lerp(baseCol, Color.white, 0.18f);
        Color lo = Color.Lerp(baseCol, Color.black, 0.35f);

        for (int y = 0; y < VoxelPixelSize; y++)
        {
            for (int x = 0; x < VoxelPixelSize; x++)
            {
                Color c = baseCol;
                // Top-left 1px highlight
                if (x == 0 || y == VoxelPixelSize - 1) c = hi;
                // Bottom-right 1px shadow
                if (x == VoxelPixelSize - 1 || y == 0) c = lo;
                tex.SetPixel(px0 + x, py0 + y, c);
            }
        }
    }

    public static void ClearVoxel(Texture2D tex, int gx, int gy)
    {
        int px0 = gx * VoxelPixelSize;
        int py0 = gy * VoxelPixelSize;
        for (int y = 0; y < VoxelPixelSize; y++)
            for (int x = 0; x < VoxelPixelSize; x++)
                tex.SetPixel(px0 + x, py0 + y, new Color(0, 0, 0, 0));
    }
}
```

- [ ] **Step 2: Refresh Unity and verify compile**

Run via Unity MCP:
```
mcp__UnityMCP__refresh_unity(scope="scripts", compile="request", wait_for_ready=true)
mcp__UnityMCP__read_console(types=["error"], count=20)
```
Expected: zero errors (new class, no existing references yet).

- [ ] **Step 3: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/VoxelMeteorGenerator.cs Assets/Scripts/VoxelMeteorGenerator.cs.meta
git commit -m "Add VoxelMeteorGenerator"
```

---

## Task 2: Rewrite Meteor.cs for voxel model

**Files:**
- Modify: `Assets/Scripts/Meteor.cs` (full rewrite)

This will break `Missile.cs` momentarily (it calls `TakeDamage` which no longer exists). Task 3 fixes that. Do not compile-check between tasks 2 and 3; do it once after task 3.

- [ ] **Step 1: Overwrite Meteor.cs**

Write `Assets/Scripts/Meteor.cs` with exact content:

```csharp
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
        transform.rotation = Quaternion.identity; // voxel meteors don't spin — keeps grid axis aligned
        transform.localScale = Vector3.one * sizeScale;

        ReleaseTexture(); // in case of reused pool instance
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

        // Collider sized to the bounding disk at local scale 1. sizeScale is applied via transform.
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

    // Returns the number of voxels destroyed.
    public int ApplyBlast(Vector3 worldImpactPoint, float worldRadius)
    {
        if (dead || aliveCount == 0) return 0;

        // Convert world → local (meters) → voxel grid coordinates
        Vector3 local = transform.InverseTransformPoint(worldImpactPoint);
        // Meteor sprite is 1.5 world units across at scale=1 (150px / 100ppu).
        // Local range [-0.75, +0.75] maps to grid [0, 10].
        const float halfExtent = 0.75f;
        float localToGrid = VoxelMeteorGenerator.GridSize / (halfExtent * 2f);
        float gx = (local.x + halfExtent) * localToGrid;
        float gy = (local.y + halfExtent) * localToGrid;
        float gridRadius = worldRadius * localToGrid / transform.localScale.x;

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
```

- [ ] **Step 2: Do NOT refresh yet** (Missile.cs will fail to compile until Task 3 lands).

---

## Task 3: Update Missile.cs for ApplyBlast contract

**Files:**
- Modify: `Assets/Scripts/Missile.cs`

- [ ] **Step 1: Overwrite Missile.cs**

Write `Assets/Scripts/Missile.cs` with exact content:

```csharp
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class Missile : MonoBehaviour
{
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private ParticleSystem explosionPrefab;
    [SerializeField] private FloatingText floatingTextPrefab;

    private Rigidbody2D rb;
    private CircleCollider2D col;
    private float impactRadius;
    private float blastRadius;
    private float despawnAt;
    private Turret owner;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        col.isTrigger = true;
    }

    public void Launch(Turret turret, Vector3 position, Vector2 velocity, float damageStat, float blastStat)
    {
        owner = turret;
        // Damage stat → impact radius: 0.05 + 0.02 * damage
        impactRadius = 0.05f + 0.02f * Mathf.Max(0f, damageStat);
        blastRadius = Mathf.Max(0f, blastStat);
        transform.position = position;
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        rb.linearVelocity = velocity;
        despawnAt = Time.time + lifetime;
    }

    private void Update()
    {
        if (Time.time >= despawnAt) Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var meteor = other.GetComponentInParent<Meteor>();
        if (meteor == null || !meteor.IsAlive) return;

        float totalRadius = impactRadius + blastRadius;
        int destroyed = meteor.ApplyBlast(transform.position, totalRadius);

        if (destroyed > 0)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.AddMoney(destroyed);

            if (floatingTextPrefab != null)
            {
                var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
                ft.Show($"+${destroyed}");
            }
        }

        if (explosionPrefab != null)
        {
            var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            fx.Play();
            Destroy(fx.gameObject, 1.5f);
        }

        Despawn();
    }

    private void Despawn()
    {
        rb.linearVelocity = Vector2.zero;
        owner?.ReleaseMissile(this);
    }
}
```

**Contract changes from previous version:**
- `Launch()` now takes `damageStat` and `blastStat` (raw stat values), not pre-computed damage + radius.
- TrailRenderer reference removed — trail is now handled by a sparks ParticleSystem on the prefab (Task 7) that auto-plays with the missile.
- `floatingTextPrefab` is a new serialized field on the missile — previously the floating text was spawned by the meteor. Moving it to the missile lets the text show the destroyed count which only the missile knows.

- [ ] **Step 2: Update Turret.cs Fire() to pass raw stat values**

`Assets/Scripts/Turret.cs` — the existing `Fire()` method calls `missile.Launch(this, spawnPos, dir * speed, stats.damage.CurrentValue, stats.blastRadius.CurrentValue)`. That signature already matches the new `Launch`, so no change is needed. Double-check by reading the current line:

```
grep -n "missile.Launch" "Assets/Scripts/Turret.cs"
```
Expected output:
```
<line_no>:        missile.Launch(this, spawnPos, dir * speed, stats.damage.CurrentValue, stats.blastRadius.CurrentValue);
```
If it matches, no edit needed. If it doesn't match, edit `Turret.cs` to make it match.

- [ ] **Step 3: Delete the old MeteorShapeGenerator.cs**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
rm Assets/Scripts/MeteorShapeGenerator.cs Assets/Scripts/MeteorShapeGenerator.cs.meta
```

- [ ] **Step 4: Refresh Unity and verify compile**

```
mcp__UnityMCP__refresh_unity(scope="all", mode="force", compile="request", wait_for_ready=true)
mcp__UnityMCP__read_console(types=["error"], count=30)
```
Expected: zero compile errors. If errors appear, fix them inline and re-refresh.

Also sanity-check types loaded:
```
mcp__UnityMCP__execute_code(action="execute", code="""
var names = new[] { "Meteor", "Missile", "VoxelMeteorGenerator", "Turret", "MeteorSpawner" };
var results = new System.Collections.Generic.List<string>();
foreach (var n in names) {
    var t = System.AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
        .FirstOrDefault(x => x.Name == n);
    results.Add(n + ": " + (t != null ? "OK" : "MISSING"));
}
return string.Join("\n", results);
""")
```
Expected: all OK.

- [ ] **Step 5: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/Meteor.cs Assets/Scripts/Missile.cs
git rm Assets/Scripts/MeteorShapeGenerator.cs Assets/Scripts/MeteorShapeGenerator.cs.meta
git commit -m "Voxel meteor model: ApplyBlast, pay-per-voxel, drop HP"
```

---

## Task 4: Tune spawner defaults

**Files:**
- Modify: `Assets/Scripts/MeteorSpawner.cs`

- [ ] **Step 1: Update three serialized default values**

In `Assets/Scripts/MeteorSpawner.cs`, change lines:
```csharp
    [SerializeField] private float initialInterval = 2.5f;
    [SerializeField] private float minInterval = 0.5f;
    [SerializeField] private float rampDurationSeconds = 120f;
```
to:
```csharp
    [SerializeField] private float initialInterval = 4.0f;
    [SerializeField] private float minInterval = 1.5f;
    [SerializeField] private float rampDurationSeconds = 180f;
```

**Important:** Changing the `[SerializeField]` default in source does NOT update the value on the existing scene instance (Unity stores the serialized value in the scene file). Step 2 applies the new values to the scene's MeteorSpawner.

- [ ] **Step 2: Apply new values to the scene's MeteorSpawner via execute_code**

```
mcp__UnityMCP__execute_code(action="execute", code="""
var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (active.path != "Assets/Scenes/Game.unity") {
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
    active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
}
var spawnerGo = GameObject.Find("MeteorSpawner");
var spawner = spawnerGo.GetComponent<MeteorSpawner>();
var so = new UnityEditor.SerializedObject(spawner);
so.FindProperty("initialInterval").floatValue = 4.0f;
so.FindProperty("minInterval").floatValue = 1.5f;
so.FindProperty("rampDurationSeconds").floatValue = 180f;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(active);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
return "Spawner retuned and scene saved";
""")
```
Expected: `"Spawner retuned and scene saved"`.

- [ ] **Step 3: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Scripts/MeteorSpawner.cs Assets/Scenes/Game.unity
git commit -m "Tune spawn rate: calmer starting cadence and ramp"
```

---

## Task 5: Regenerate voxel art PNGs

**Files:**
- Regenerate: `Assets/Art/turret_base.png`, `turret_barrel.png`, `missile.png`, `background.png`

- [ ] **Step 1: Overwrite the four PNGs via execute_code**

```
mcp__UnityMCP__execute_code(action="execute", code="""
string artDir = System.IO.Path.Combine(Application.dataPath, "Art");
System.Action<string, Texture2D> save = (name, tex) => {
    var bytes = tex.EncodeToPNG();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(artDir, name + \".png\"), bytes);
    UnityEngine.Object.DestroyImmediate(tex);
};
System.Action<Texture2D, int, int, int, int, Color, Color, Color> drawBrick = (t, x0, y0, w, h, fill, hi, lo) => {
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
        Color c = fill;
        if (x == 0 || y == h - 1) c = hi;
        if (x == w - 1 || y == 0) c = lo;
        t.SetPixel(x0 + x, y0 + y, c);
    }
};

// turret_base.png — 64x48, 3 stacked brick rows
{
    int W = 64, H = 48;
    var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
    var clear = new Color[W * H];
    t.SetPixels(clear);
    Color bot = new Color(0.188f, 0.188f, 0.220f, 1f); // #303038
    Color mid = new Color(0.282f, 0.282f, 0.345f, 1f); // #484858
    Color top = new Color(0.353f, 0.376f, 0.439f, 1f); // #5A6070
    Color dark = new Color(0.08f, 0.08f, 0.10f, 1f);
    Color hiBot = Color.Lerp(bot, Color.white, 0.18f);
    Color hiMid = Color.Lerp(mid, Color.white, 0.18f);
    Color hiTop = Color.Lerp(top, Color.white, 0.25f);
    int bw = 16, bh = 16; // 16x16 bricks, 4 across, 3 rows
    for (int col = 0; col < 4; col++) drawBrick(t, col * bw, 0,      bw, bh, bot, hiBot, dark);
    for (int col = 0; col < 4; col++) drawBrick(t, col * bw, bh,     bw, bh, mid, hiMid, dark);
    // Top row: crenellated — skip every other block
    for (int col = 0; col < 4; col++)
        if (col % 2 == 0) drawBrick(t, col * bw, bh * 2, bw, bh, top, hiTop, dark);
    t.Apply();
    save(\"turret_base\", t);
}

// turret_barrel.png — 16x48, 2-wide by 6-tall voxel column (8x8 voxels)
{
    int W = 16, H = 48;
    var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
    var clear = new Color[W * H];
    t.SetPixels(clear);
    Color baseC = new Color(0.188f, 0.188f, 0.220f, 1f);
    Color midC  = new Color(0.282f, 0.282f, 0.345f, 1f);
    Color tipC  = new Color(0.502f, 0.502f, 0.565f, 1f);
    Color dark  = new Color(0.08f, 0.08f, 0.10f, 1f);
    int vw = 8, vh = 8;
    for (int row = 0; row < 6; row++)
    {
        Color fill;
        if (row == 0) fill = baseC;
        else if (row == 5) fill = tipC;
        else fill = midC;
        Color hi = Color.Lerp(fill, Color.white, 0.22f);
        for (int c = 0; c < 2; c++)
            drawBrick(t, c * vw, row * vh, vw, vh, fill, hi, dark);
    }
    t.Apply();
    save(\"turret_barrel\", t);
}

// missile.png — 12x20, 2-wide by 4-tall voxels (6x5 each)
{
    int W = 12, H = 20;
    var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
    var clear = new Color[W * H];
    t.SetPixels(clear);
    Color head = new Color(1f,    0.878f, 0.251f, 1f); // #FFE040
    Color body = new Color(1f,    0.502f, 0.188f, 1f); // #FF8030
    Color tail = new Color(0.502f, 0.125f, 0.039f, 1f); // #80200A
    Color dark = new Color(0.08f,  0.04f,  0.02f,  1f);
    int vw = 6, vh = 5;
    Color[] rowColors = new Color[] { tail, body, body, head };
    for (int row = 0; row < 4; row++)
    {
        Color fill = rowColors[row];
        Color hi = Color.Lerp(fill, Color.white, 0.3f);
        for (int c = 0; c < 2; c++)
            drawBrick(t, c * vw, row * vh, vw, vh, fill, hi, dark);
    }
    t.Apply();
    save(\"missile\", t);
}

// background.png — 512x288, gradient + chunky stars
{
    int W = 512, H = 288;
    var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
    var px = new Color[W * H];
    Color topC = new Color(0.04f, 0.06f, 0.14f, 1f);
    Color botC = new Color(0.01f, 0.01f, 0.03f, 1f);
    for (int y = 0; y < H; y++) {
        float tg = (float)y / (H - 1);
        Color c = Color.Lerp(botC, topC, tg);
        for (int x = 0; x < W; x++) px[y * W + x] = c;
    }
    var rng = new System.Random(12345);
    for (int i = 0; i < 200; i++) {
        int sx = rng.Next(W - 3);
        int sy = rng.Next(H - 3);
        float a = 0.5f + (float)rng.NextDouble() * 0.5f;
        int size = rng.Next(100) < 60 ? 2 : 3;
        Color sc = new Color(a, a, a, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[(sy + y) * W + (sx + x)] = sc;
    }
    t.SetPixels(px); t.Apply();
    save(\"background\", t);
}

UnityEditor.AssetDatabase.Refresh();
return \"Regenerated 4 PNGs\";
""")
```
Expected: `"Regenerated 4 PNGs"`.

- [ ] **Step 2: Reimport PNGs as Sprites with correct settings**

The existing `.png.meta` files already mark these as Sprites, so Unity should re-import them automatically with the same settings. Force-refresh to be sure:

```
mcp__UnityMCP__refresh_unity(scope="all", mode="force", wait_for_ready=true)
mcp__UnityMCP__read_console(types=["error","warning"], count=20)
```
Expected: no errors about the PNGs.

- [ ] **Step 3: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Art/turret_base.png Assets/Art/turret_barrel.png Assets/Art/missile.png Assets/Art/background.png
git commit -m "Voxelify turret, missile, and starfield art"
```

---

## Task 6: Swap ParticleMat to square sprite

**Files:**
- Modify: `Assets/Art/ParticleMat.mat`

- [ ] **Step 1: Update material's main texture**

```
mcp__UnityMCP__execute_code(action="execute", code="""
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(\"Assets/Art/ParticleMat.mat\");
if (mat == null) return \"ParticleMat not found\";
var sq = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(\"Assets/Art/square.png\");
if (sq == null) return \"square.png not found\";
mat.mainTexture = sq;
if (mat.HasProperty(\"_BaseMap\")) mat.SetTexture(\"_BaseMap\", sq);
UnityEditor.EditorUtility.SetDirty(mat);
UnityEditor.AssetDatabase.SaveAssets();
return \"ParticleMat now uses square.png\";
""")
```
Expected: `"ParticleMat now uses square.png"`.

- [ ] **Step 2: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Art/ParticleMat.mat
git commit -m "Point ParticleMat at square sprite for pixel-edge particles"
```

---

## Task 7: Rebuild Missile prefab — remove trail, add sparks, add floating text ref

**Files:**
- Modify: `Assets/Prefabs/Missile.prefab`

- [ ] **Step 1: Open prefab stage, rebuild trail child, wire refs**

```
mcp__UnityMCP__execute_code(action="execute", code="""
var path = \"Assets/Prefabs/Missile.prefab\";
var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(path);
var root = stage.prefabContentsRoot;

// Remove old Trail child if present
var oldTrail = root.transform.Find(\"Trail\");
if (oldTrail != null) UnityEngine.Object.DestroyImmediate(oldTrail.gameObject);

// Add new Sparks child
var sparksGo = new GameObject(\"Sparks\");
sparksGo.transform.SetParent(root.transform, false);
sparksGo.transform.localPosition = new Vector3(0f, -0.1f, 0f); // tail of missile (local −Y)
var ps = sparksGo.AddComponent<ParticleSystem>();
var main = ps.main;
main.duration = 5f;
main.loop = true;
main.playOnAwake = true;
main.startLifetime = 0.3f;
main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.878f, 0.251f, 1f), new Color(1f, 0.69f, 0.125f, 1f));
main.gravityModifier = 0f;
main.simulationSpace = ParticleSystemSimulationSpace.World;
var emission = ps.emission;
emission.rateOverTime = 60f;
var shape = ps.shape;
shape.shapeType = ParticleSystemShapeType.Circle;
shape.radius = 0.03f;
var vol = ps.velocityOverLifetime;
vol.enabled = true;
vol.space = ParticleSystemSimulationSpace.Local;
vol.y = new ParticleSystem.MinMaxCurve(-1.5f); // push out the tail (local −Y)
var col = ps.colorOverLifetime;
col.enabled = true;
var grad = new Gradient();
grad.SetKeys(
    new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.88f, 0.25f), 0f), new GradientColorKey(new Color(0.9f, 0.2f, 0.05f), 1f) },
    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) }
);
col.color = grad;
var renderer = sparksGo.GetComponent<ParticleSystemRenderer>();
renderer.renderMode = ParticleSystemRenderMode.Billboard;
renderer.sharedMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(\"Assets/Art/ParticleMat.mat\");
renderer.sortingOrder = 13;

// Wire the Missile.floatingTextPrefab ref
var missile = root.GetComponent<Missile>();
var floatingPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(\"Assets/Prefabs/FloatingText.prefab\");
var floatingComp = floatingPrefab != null ? floatingPrefab.GetComponent<FloatingText>() : null;
var so = new UnityEditor.SerializedObject(missile);
var ftProp = so.FindProperty(\"floatingTextPrefab\");
if (ftProp != null) ftProp.objectReferenceValue = floatingComp;
// Clear the old 'trail' field if it still exists in the serialized data
var trailProp = so.FindProperty(\"trail\");
if (trailProp != null) trailProp.objectReferenceValue = null;
so.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.PrefabStageUtility.SaveAsPrefabStage(stage);
UnityEditor.SceneManagement.PrefabStageUtility.SaveAndCloseOpenPrefabStage();
return \"Missile prefab: Trail removed, Sparks added, floatingText wired\";
""")
```

Expected: `"Missile prefab: Trail removed, Sparks added, floatingText wired"`.

Note: `PrefabStageUtility.SaveAsPrefabStage` / `SaveAndCloseOpenPrefabStage` API names may vary by Unity version. If those methods don't exist in 6000.4.1f1, fallback to `UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path)` followed by `UnityEditor.SceneManagement.StageUtility.GoToMainStage()`. If the execute_code returns a compilation or runtime error, retry with the fallback.

- [ ] **Step 2: Delete the now-orphaned TrailMat.mat**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
rm Assets/Art/TrailMat.mat Assets/Art/TrailMat.mat.meta
```
Then refresh:
```
mcp__UnityMCP__refresh_unity(scope="all", mode="force", wait_for_ready=true)
mcp__UnityMCP__read_console(types=["error"], count=20)
```
Expected: no errors (TrailMat was only referenced by the now-removed Trail child).

- [ ] **Step 3: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Prefabs/Missile.prefab
git rm Assets/Art/TrailMat.mat Assets/Art/TrailMat.mat.meta
git commit -m "Missile: replace TrailRenderer with pixel spark particle system"
```

---

## Task 8: Rebuild Meteor prefab — wire voxelChunkPrefab and remove stale FloatingText ref

**Files:**
- Modify: `Assets/Prefabs/Meteor.prefab`

The `Meteor.cs` serialized field surface changed: `debrisPrefab` → `voxelChunkPrefab`, and `floatingTextPrefab` was removed entirely (moved to Missile).

- [ ] **Step 1: Update prefab serialized refs**

```
mcp__UnityMCP__execute_code(action="execute", code="""
var path = \"Assets/Prefabs/Meteor.prefab\";
var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(path);
var root = stage.prefabContentsRoot;
var meteor = root.GetComponent<Meteor>();
var debris = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(\"Assets/Prefabs/DebrisBurst.prefab\");
var debrisPs = debris != null ? debris.GetComponent<ParticleSystem>() : null;
var so = new UnityEditor.SerializedObject(meteor);
var prop = so.FindProperty(\"voxelChunkPrefab\");
if (prop != null) prop.objectReferenceValue = debrisPs;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
UnityEditor.SceneManagement.StageUtility.GoToMainStage();
return \"Meteor prefab: voxelChunkPrefab wired\";
""")
```
Expected: `"Meteor prefab: voxelChunkPrefab wired"`.

- [ ] **Step 2: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Prefabs/Meteor.prefab
git commit -m "Meteor prefab: wire voxelChunkPrefab reference"
```

---

## Task 9: Retune debris / explosion / muzzle-flash particle prefabs

**Files:**
- Modify: `Assets/Prefabs/DebrisBurst.prefab`, `ExplosionBurst.prefab`, `MuzzleFlash.prefab`

These already use `ParticleMat` (which now points at `square.png`), so they're already rendering square particles. Small tuning pass to make sizes and shapes feel voxelly.

- [ ] **Step 1: Retune the three particle prefabs**

```
mcp__UnityMCP__execute_code(action="execute", code="""
System.Action<string, System.Action<ParticleSystem>> retune = (path, cfg) => {
    var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(path);
    var root = stage.prefabContentsRoot;
    var ps = root.GetComponent<ParticleSystem>();
    cfg(ps);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    UnityEditor.SceneManagement.StageUtility.GoToMainStage();
};

retune(\"Assets/Prefabs/DebrisBurst.prefab\", ps => {
    var main = ps.main;
    main.duration = 0.5f;
    main.startLifetime = 0.8f;
    main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
    main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.14f);
    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.40f, 0.25f, 1f), new Color(0.35f, 0.25f, 0.15f, 1f));
    main.gravityModifier = 1.5f;
    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 1) });
    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Circle;
    shape.radius = 0.02f;
});

retune(\"Assets/Prefabs/ExplosionBurst.prefab\", ps => {
    var main = ps.main;
    main.duration = 0.3f;
    main.startLifetime = 0.4f;
    main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
    main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.18f);
    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f, 1f), new Color(1f, 0.5f, 0.1f, 1f));
    main.gravityModifier = 0f;
    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 12) });
    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Circle;
    shape.radius = 0.08f;
});

retune(\"Assets/Prefabs/MuzzleFlash.prefab\", ps => {
    var main = ps.main;
    main.duration = 0.08f;
    main.startLifetime = 0.12f;
    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
    main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.14f);
    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f, 1f), new Color(1f, 0.7f, 0.3f, 1f));
    main.gravityModifier = 0f;
    var emission = ps.emission;
    emission.rateOverTime = 0f;
    emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 6) });
});

return \"Retuned Debris/Explosion/MuzzleFlash\";
""")
```
Expected: `"Retuned Debris/Explosion/MuzzleFlash"`.

**DebrisBurst emits exactly 1 particle per instantiation** — because `Meteor.ApplyBlast` instantiates a fresh DebrisBurst per destroyed voxel. One burst = one flying chunk.

- [ ] **Step 2: Commit**

```bash
cd "/Users/matt/dev/Unity/Meteor Idle"
git add Assets/Prefabs/DebrisBurst.prefab Assets/Prefabs/ExplosionBurst.prefab Assets/Prefabs/MuzzleFlash.prefab
git commit -m "Retune particle prefabs for voxel-chunk feel"
```

---

## Task 10: Play-mode verification

No file changes in this task. Purely verification.

- [ ] **Step 1: Clear console, enter play mode, wait, screenshot, check console**

```
mcp__UnityMCP__read_console(action="clear")
mcp__UnityMCP__manage_editor(action="play")
# Wait ~8 seconds naturally by doing a trivial read_console first
mcp__UnityMCP__read_console(types=["error"], count=20)
mcp__UnityMCP__manage_camera(action="screenshot", include_image=true, max_resolution=900)
mcp__UnityMCP__manage_editor(action="stop")
```

Expected outcome:
- Console has zero errors (only the harmless Rider path warning is acceptable).
- Screenshot shows: blocky voxel meteors falling, spawn density clearly lower than before (≤ 6 meteors visible at once in the first 10 seconds), the turret base and barrel visibly blocky/Lego-like, the missile visibly a 2×4 voxel stack, missile sparks visible as discrete square flakes behind any in-flight missile.

- [ ] **Step 2: Visual checklist against spec §11**

Review the screenshot against these specific items from the design doc:
- [ ] Meteors are chunky voxel grids, different shape per seed.
- [ ] Spawn density is calmer than before.
- [ ] Turret base / barrel / missile are visibly blocky.
- [ ] Starfield stars are chunks, not single pixels.
- [ ] Missile sparks are discrete square flakes.

Chew-in-action items that require a missile to land on a meteor (may not be in the screenshot frame):
- [ ] A landed missile removes a cluster of voxels from the impact point.
- [ ] Money increases by exactly the destroyed count.
- [ ] Meteor keeps falling after partial destruction.

If chew behavior isn't visible in the first screenshot, take a second screenshot ~3-4 seconds later and look for the effect. If still not visible, let the user manually drive a playtest and report back.

- [ ] **Step 3: Commit only if screenshots unexpectedly landed in Assets**

Screenshots go to `Assets/Screenshots/`, which is gitignored. No commit expected from this task — it's verification only.

---

## Self-Review

**Spec coverage:**
- §3 voxel grid + rendering → Tasks 1, 2.
- §4 ApplyBlast → Task 2.
- §5 missile sparks + particle material swap → Tasks 6, 7, 9.
- §6 art pass (turret, missile, background) → Task 5.
- §7 stat reinterpretation (Damage = impact radius) → Task 3 (in `Missile.Launch`).
- §8 spawn rate → Task 4.
- §9 files touched → covered in file structure + tasks.
- §10 deferred items → explicitly not in any task.
- §11 verification → Task 10.

All spec sections have at least one task. No gaps.

**Type consistency:**
- `VoxelMeteorGenerator.Generate` signature `(int seed, out bool[,] grid, out Texture2D texture, out int aliveCount)` — used identically in `Meteor.Spawn` in Task 2. ✓
- `Meteor.ApplyBlast(Vector3, float) → int` defined in Task 2 and called with matching signature in Task 3's `Missile.OnTriggerEnter2D`. ✓
- `Missile.Launch(Turret, Vector3, Vector2, float, float)` — defined in Task 3, matches existing `Turret.Fire` call-site verified in Task 3 Step 2. ✓
- `VoxelMeteorGenerator.PaintVoxel` / `ClearVoxel` — defined in Task 1, called from `Meteor.ApplyBlast` in Task 2. ✓
- `voxelChunkPrefab` serialized field name matches between `Meteor.cs` Task 2 and the prefab wiring in Task 8. ✓
- `floatingTextPrefab` serialized field name matches between `Missile.cs` Task 3 and the prefab wiring in Task 7. ✓

**Placeholder scan:**
- No TBD/TODO.
- No "implement later" or "similar to task N."
- No "add appropriate error handling."
- Every step that touches code contains the full code.
- Every command has a clear expected output.

All clean.

**Execution order safety:**
- Task 1 is additive (no break).
- Tasks 2+3 are coupled: Meteor.cs rewrite will break Missile.cs compile until Task 3 lands. Task 3 explicitly says not to refresh between them and verifies compile only in Task 3 Step 4.
- Task 4 depends only on compile being green (true after Task 3).
- Tasks 5, 6 are pure asset edits.
- Task 7 depends on Task 6 (ParticleMat swap) being done so the Sparks renderer gets the square texture.
- Task 8 depends on Task 2 (new `voxelChunkPrefab` field exists).
- Task 9 depends on Task 6.
- Task 10 is final verification.

Order is safe.
