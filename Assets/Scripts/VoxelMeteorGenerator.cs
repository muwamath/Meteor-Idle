using System.Collections.Generic;
using UnityEngine;

public static class VoxelMeteorGenerator
{
    public const int GridSize = 10;
    public const int VoxelPixelSize = 15;
    public const int TextureSize = GridSize * VoxelPixelSize;

    // Dirt palette — unchanged from pre-Iter-1.
    private static readonly Color DirtTopColor    = new Color(0.545f, 0.451f, 0.333f, 1f); // #8B7355
    private static readonly Color DirtBottomColor = new Color(0.290f, 0.227f, 0.165f, 1f); // #4A3A2A

    // Core palette — single baseline hue for Iter 1. Iter 2 will drive multiple
    // palettes off an AsteroidType asset field using exactly this structure.
    private static readonly Color CoreTopColor    = new Color(0.75f, 0.25f, 0.25f, 1f);
    private static readonly Color CoreBottomColor = new Color(0.35f, 0.10f, 0.10f, 1f);

    // Takes sizeScale so the generator can compute per-meteor core count and HP,
    // and emits a VoxelKind[,] + int[,] hp pair instead of the old bool[,] grid.
    public static void Generate(
        int seed,
        float sizeScale,
        out VoxelKind[,] kind,
        out int[,] hp,
        out Texture2D texture,
        out int aliveCount)
    {
        kind = new VoxelKind[GridSize, GridSize];
        hp   = new int[GridSize, GridSize];
        var rng = new System.Random(seed);

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
                    aliveCount++;
                }
            }
        }

        // --- core count + HP scale with sizeScale (per spec tables) ---
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
        // Deterministic Fisher-Yates shuffle over the top-poolSize innermost
        // cells. Uses the same rng already threaded through the generator so
        // core placement is reproducible per-seed.
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
        }

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
                if (kind[x, y] == VoxelKind.Dirt)      PaintDirtVoxel(texture, x, y);
                else if (kind[x, y] == VoxelKind.Core) PaintCoreVoxel(texture, x, y);
            }
        }

        texture.Apply();
    }

    public static void PaintDirtVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, DirtTopColor, DirtBottomColor);
    }

    public static void PaintCoreVoxel(Texture2D tex, int gx, int gy)
    {
        PaintBlockWithPalette(tex, gx, gy, CoreTopColor, CoreBottomColor);
    }

    // Legacy alias — some older call sites may still reference PaintVoxel.
    // Routes through dirt since that's what the old behavior was.
    public static void PaintVoxel(Texture2D tex, int gx, int gy)
    {
        PaintDirtVoxel(tex, gx, gy);
    }

    private static void PaintBlockWithPalette(Texture2D tex, int gx, int gy, Color topCol, Color bottomCol)
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
