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

        var clear = new Color[TextureSize * TextureSize];
        texture.SetPixels(clear);

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

        float t = (float)gy / (GridSize - 1);
        Color baseCol = Color.Lerp(BottomColor, TopColor, t);
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
