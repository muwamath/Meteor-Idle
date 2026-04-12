using System.Collections.Generic;
using UnityEngine;

public static class VoxelMeteorGenerator
{
    public const int GridSize = 10;
    public const int VoxelPixelSize = 15;
    public const int TextureSize = GridSize * VoxelPixelSize;

    // Legacy hardcoded palettes — used as fallback when registry is null so
    // existing tests that don't pass a registry still produce identical output.
    private static readonly Color DirtTopColor    = new Color(0.545f, 0.451f, 0.333f, 1f);
    private static readonly Color DirtBottomColor = new Color(0.290f, 0.227f, 0.165f, 1f);
    private static readonly Color CoreTopColor    = new Color(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Color CoreBottomColor = new Color(0.35f, 0.10f, 0.10f, 1f);

    // Iter 1 backward-compat overload — no registry, no material out param.
    // Existing tests and call sites that don't yet pass a registry use this
    // and get identical output to pre-Iter-2 behavior (dirt + core only, no
    // stone/gold/explosive).
    public static void Generate(
        int seed,
        float sizeScale,
        out VoxelKind[,] kind,
        out int[,] hp,
        out Texture2D texture,
        out int aliveCount)
    {
        Generate(seed, sizeScale, null, out kind, out hp, out _, out texture, out aliveCount);
    }

    // Iter 2 generator: accepts a MaterialRegistry and emits a parallel
    // VoxelMaterial[,] array alongside kind[,] and hp[,]. Runs three new
    // placement passes (stone clumps, gold cells, explosives) after the
    // existing dirt-and-cores logic. All passes use the same System.Random
    // already threaded through the generator, so output stays deterministic
    // per seed.
    public static void Generate(
        int seed,
        float sizeScale,
        MaterialRegistry registry,
        out VoxelKind[,] kind,
        out int[,] hp,
        out VoxelMaterial[,] material,
        out Texture2D texture,
        out int aliveCount)
    {
        kind     = new VoxelKind[GridSize, GridSize];
        hp       = new int[GridSize, GridSize];
        material = new VoxelMaterial[GridSize, GridSize];
        var rng = new System.Random(seed);

        // Resolve registry materials once. All five may be null if registry
        // is null (backward-compat path); placement passes early-out then.
        VoxelMaterial dirtMat      = registry?.GetByName("Dirt");
        VoxelMaterial stoneMat     = registry?.GetByName("Stone");
        VoxelMaterial coreMat      = registry?.GetByName("Core");
        VoxelMaterial goldMat      = registry?.GetByName("Gold");
        VoxelMaterial explosiveMat = registry?.GetByName("Explosive");

        // --- dirt shape (unchanged sin-wave lump algorithm) ---
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
                    kind[x, y] = VoxelKind.Dirt;
                    hp[x, y] = 1;
                    material[x, y] = dirtMat;
                    aliveCount++;
                }
            }
        }

        // --- core count + HP scale with sizeScale (Iter 1 formulas) ---
        float sizeT = Mathf.Clamp01((sizeScale - 0.525f) / (1.2f - 0.525f));
        int coreCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 4f, sizeT)), 1, 4);
        int coreHp    = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 5f, sizeT)), 1, 5);

        // --- core placement: innermost live cells, deterministic shuffle ---
        var liveCells = new List<(int x, int y, float d2)>(aliveCount);
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                float dx = x + 0.5f - GridSize * 0.5f;
                float dy = y + 0.5f - GridSize * 0.5f;
                liveCells.Add((x, y, dx * dx + dy * dy));
            }
        }
        liveCells.Sort((a, b) => a.d2.CompareTo(b.d2));

        int poolSize = Mathf.Min(Mathf.Max(coreCount * 2, 5), liveCells.Count);
        for (int i = poolSize - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (liveCells[i], liveCells[j]) = (liveCells[j], liveCells[i]);
        }
        int actualCoreCount = Mathf.Min(coreCount, poolSize);
        for (int i = 0; i < actualCoreCount; i++)
        {
            var c = liveCells[i];
            kind[c.x, c.y] = VoxelKind.Core;
            hp[c.x, c.y] = coreHp;
            material[c.x, c.y] = coreMat;
        }

        // --- Iter 2 Pass 1: stone clumps (vein constraint, ≤2 deep) ---
        if (stoneMat != null) PlaceStoneClumps(rng, sizeScale, kind, hp, material, stoneMat);

        // --- Iter 2 Pass 2: gold cells (prefer adjacent to stone) ---
        if (goldMat != null) PlaceGold(rng, sizeT, kind, hp, material, goldMat, stoneMat);

        // --- Iter 2 Pass 3: explosives (never adjacent to other explosives) ---
        if (explosiveMat != null) PlaceExplosives(rng, kind, hp, material, explosiveMat, dirtMat);

        // --- texture paint ---
        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"VoxelMeteor_{seed}"
        };
        var clear = new Color[TextureSize * TextureSize];
        texture.SetPixels(clear);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] == VoxelKind.Empty) continue;
                if (material[x, y] != null)
                    PaintBlockWithPalette(texture, x, y, material[x, y].topColor, material[x, y].bottomColor);
                else if (kind[x, y] == VoxelKind.Core)
                    PaintBlockWithPalette(texture, x, y, CoreTopColor, CoreBottomColor);
                else
                    PaintBlockWithPalette(texture, x, y, DirtTopColor, DirtBottomColor);
            }
        }
        texture.Apply();
    }

    // ---------- placement helpers ----------

    private static void PlaceStoneClumps(
        System.Random rng, float sizeScale,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial stoneMat)
    {
        // Clump count scales with size: smallest gets 0-1, largest gets 1-3.
        float sizeT = Mathf.Clamp01((sizeScale - 0.525f) / (1.2f - 0.525f));
        int maxClumps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, sizeT)));
        // Stone's spawn weight gates the count. weight=0.05 → ~100% per try.
        int clumpCount = 0;
        for (int i = 0; i < maxClumps; i++)
            if (rng.NextDouble() < stoneMat.spawnWeight * 20.0)
                clumpCount++;
        if (clumpCount == 0) return;

        for (int c = 0; c < clumpCount; c++)
        {
            int targetSize = 2 + rng.Next(5); // 2..6
            GrowOneStoneClump(rng, kind, hp, material, stoneMat, targetSize);
        }
    }

    private static void GrowOneStoneClump(
        System.Random rng,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material, VoxelMaterial stoneMat,
        int targetSize)
    {
        // Pick a random plain-Dirt starting cell (not core, not already stone).
        var dirtCells = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (kind[x, y] == VoxelKind.Dirt && material[x, y] != stoneMat)
                    dirtCells.Add((x, y));
        if (dirtCells.Count == 0) return;
        var startCell = dirtCells[rng.Next(dirtCells.Count)];

        // First cell goes in unconditionally — the 2-deep cap is a constraint
        // on growing into the third concentric layer, not on placing a single
        // standalone stone cell.
        material[startCell.x, startCell.y] = stoneMat;
        hp[startCell.x, startCell.y] = stoneMat.baseHp;

        var clump = new HashSet<(int x, int y)> { startCell };
        var frontier = new List<(int x, int y)> { startCell };

        int safety = 0;
        while (clump.Count < targetSize && frontier.Count > 0 && safety++ < 200)
        {
            int idx = rng.Next(frontier.Count);
            var cur = frontier[idx];
            // Try 4 directions in random order; accept the first valid neighbor.
            var dirs = new (int dx, int dy)[] { (1,0), (-1,0), (0,1), (0,-1) };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }
            bool grew = false;
            foreach (var d in dirs)
            {
                int nx = cur.x + d.dx;
                int ny = cur.y + d.dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize) continue;
                if (kind[nx, ny] != VoxelKind.Dirt) continue;     // skip Empty/Core
                if (material[nx, ny] == stoneMat) continue;       // already in clump
                if (WouldExceedTwoDeep(nx, ny, material, stoneMat)) continue;

                clump.Add((nx, ny));
                frontier.Add((nx, ny));
                material[nx, ny] = stoneMat;
                hp[nx, ny] = stoneMat.baseHp;
                grew = true;
                break;
            }
            if (!grew) frontier.RemoveAt(idx);
        }
    }

    // Return true if marking (x,y) as stone would put it more than 2 cells
    // deep inside the stone region. Equivalently: every cell within manhattan
    // distance 2 of (x,y), excluding (x,y) itself, must already be stone.
    private static bool WouldExceedTwoDeep(
        int x, int y, VoxelMaterial[,] material, VoxelMaterial stoneMat)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 2) continue;
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize)
                    return false; // out-of-bounds counts as "non-stone escape"
                if (material[nx, ny] != stoneMat)
                    return false;
            }
        }
        return true; // every cell within 2 steps is stone — would exceed
    }

    private static void PlaceGold(
        System.Random rng, float sizeT,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material,
        VoxelMaterial goldMat, VoxelMaterial stoneMat)
    {
        // Gold is rare. Roll up to maxGold attempts; bigger asteroids get more.
        int maxGold = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, sizeT)));
        int goldCount = 0;
        for (int i = 0; i < maxGold; i++)
            if (rng.NextDouble() < goldMat.spawnWeight * 100.0) // weight 0.005 → 50% per try
                goldCount++;
        if (goldCount == 0) return;

        // Build list of plain-dirt cells adjacent to existing stone, plus
        // a fallback list of any plain-dirt cells (for the standalone path).
        var adjacentToStone = new List<(int x, int y)>();
        var anyDirt = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (kind[x, y] != VoxelKind.Dirt) continue;
                if (material[x, y] == stoneMat) continue;     // already stone
                if (material[x, y] == goldMat) continue;      // already gold
                anyDirt.Add((x, y));
                if (HasNeighborMaterial(x, y, material, stoneMat))
                    adjacentToStone.Add((x, y));
            }
        }

        for (int g = 0; g < goldCount; g++)
        {
            (int x, int y) cell;
            if (adjacentToStone.Count > 0)
            {
                int idx = rng.Next(adjacentToStone.Count);
                cell = adjacentToStone[idx];
                adjacentToStone.RemoveAt(idx);
                anyDirt.Remove(cell);
            }
            else if (anyDirt.Count > 0)
            {
                int idx = rng.Next(anyDirt.Count);
                cell = anyDirt[idx];
                anyDirt.RemoveAt(idx);
            }
            else
            {
                break;
            }
            material[cell.x, cell.y] = goldMat;
            hp[cell.x, cell.y] = goldMat.baseHp;
        }
    }

    private static bool HasNeighborMaterial(int x, int y, VoxelMaterial[,] material, VoxelMaterial target)
    {
        if (target == null) return false;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= GridSize || ny >= GridSize) continue;
                if (material[nx, ny] == target) return true;
            }
        }
        return false;
    }

    private static void PlaceExplosives(
        System.Random rng,
        VoxelKind[,] kind, int[,] hp, VoxelMaterial[,] material,
        VoxelMaterial explosiveMat, VoxelMaterial dirtMat)
    {
        // Even rarer than gold. weight=0.002 → 40% per try across 2 attempts.
        int maxExplosive = 2;
        int explosiveCount = 0;
        for (int i = 0; i < maxExplosive; i++)
            if (rng.NextDouble() < explosiveMat.spawnWeight * 200.0)
                explosiveCount++;
        if (explosiveCount == 0) return;

        // Plain-dirt candidate pool — reference equality against dirtMat so a
        // future material with displayName "X" can't accidentally pass the
        // filter. Stone, gold, and cores are excluded by virtue of having
        // their own material reference.
        var dirtCells = new List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (kind[x, y] == VoxelKind.Dirt && material[x, y] == dirtMat)
                    dirtCells.Add((x, y));

        for (int e = 0; e < explosiveCount && dirtCells.Count > 0; e++)
        {
            // Try a few times to find a non-adjacent slot.
            for (int attempt = 0; attempt < 8 && dirtCells.Count > 0; attempt++)
            {
                int idx = rng.Next(dirtCells.Count);
                var cell = dirtCells[idx];
                if (HasNeighborMaterial(cell.x, cell.y, material, explosiveMat))
                {
                    dirtCells.RemoveAt(idx);
                    continue;
                }
                material[cell.x, cell.y] = explosiveMat;
                hp[cell.x, cell.y] = explosiveMat.baseHp;
                dirtCells.RemoveAt(idx);
                break;
            }
        }
    }

    // ---------- legacy paint helpers (still used by Meteor.ApplyBlast for ClearVoxel) ----------

    public static void PaintDirtVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, DirtTopColor, DirtBottomColor);
    }

    public static void PaintCoreVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, CoreTopColor, CoreBottomColor);
    }

    public static void PaintVoxel(Texture2D tex, int gx, int gy)
    {
        PaintDirtVoxel(tex, gx, gy);
    }

    public static void PaintBlockWithPalette(Texture2D tex, int gx, int gy, Color topCol, Color bottomCol)
    {
        int px0 = gx * VoxelPixelSize;
        int py0 = gy * VoxelPixelSize;

        float t = (float)gy / (GridSize - 1);
        Color baseCol = Color.Lerp(bottomCol, topCol, t);
        Color hi = Color.Lerp(baseCol, Color.white, 0.18f);
        Color lo = Color.Lerp(baseCol, Color.black, 0.35f);

        for (int y = 0; y < VoxelPixelSize; y++)
        {
            for (int x = 0; x < VoxelPixelSize; x++)
            {
                Color c = baseCol;
                if (x == 0 || y == VoxelPixelSize - 1) c = hi;
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
