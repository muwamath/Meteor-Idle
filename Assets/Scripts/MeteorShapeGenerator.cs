using System.Collections.Generic;
using UnityEngine;

public static class MeteorShapeGenerator
{
    private const int Size = 128;
    private const float BaseRadius = 50f;
    private const float LumpAmp = 0.25f;

    private static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

    public static Sprite GetSprite(int seed)
    {
        if (cache.TryGetValue(seed, out var cached) && cached != null) return cached;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = $"Meteor_{seed}"
        };

        var rng = new System.Random(seed);
        float phase1 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase2 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float phase3 = (float)(rng.NextDouble() * Mathf.PI * 2);
        float amp1 = 0.55f + (float)rng.NextDouble() * 0.25f;
        float amp2 = 0.25f + (float)rng.NextDouble() * 0.20f;
        float amp3 = 0.10f + (float)rng.NextDouble() * 0.10f;

        Color top = new Color(0.545f, 0.451f, 0.333f, 1f);   // #8B7355
        Color bot = new Color(0.290f, 0.227f, 0.165f, 1f);   // #4A3A2A
        Color rimColor = new Color(0.12f, 0.09f, 0.06f, 1f);
        Color craterColor = new Color(0.22f, 0.16f, 0.11f, 1f);
        Vector2 lightDir = new Vector2(-0.7f, 0.7f).normalized; // upper-left

        int craterCount = 5 + rng.Next(6);
        var craters = new List<(Vector2 center, float r)>(craterCount);
        for (int i = 0; i < craterCount; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.PI * 2);
            float d = (float)(rng.NextDouble() * (BaseRadius * 0.65f));
            Vector2 c = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
            float r = 3f + (float)rng.NextDouble() * 6f;
            craters.Add((c, r));
        }

        Vector2 center = new Vector2(Size * 0.5f - 0.5f, Size * 0.5f - 0.5f);
        var pixels = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = x - center.x;
                float dy = y - center.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float theta = Mathf.Atan2(dy, dx);

                float lump =
                    amp1 * Mathf.Sin(theta * 3f + phase1) +
                    amp2 * Mathf.Sin(theta * 5f + phase2) +
                    amp3 * Mathf.Sin(theta * 9f + phase3);

                float radius = BaseRadius * (1f + LumpAmp * lump);

                if (dist > radius)
                {
                    pixels[y * Size + x] = new Color(0, 0, 0, 0);
                    continue;
                }

                float t = Mathf.InverseLerp(-BaseRadius, BaseRadius, dy);
                Color baseCol = Color.Lerp(bot, top, t);

                Vector2 offset = new Vector2(dx, dy) / Mathf.Max(1f, radius);
                float light = Mathf.Clamp01(0.6f + 0.5f * Vector2.Dot(offset, lightDir));
                Color lit = baseCol * light;
                lit.a = 1f;

                foreach (var c in craters)
                {
                    float cd = ((new Vector2(dx, dy)) - c.center).magnitude;
                    if (cd < c.r)
                    {
                        float k = 1f - (cd / c.r);
                        lit = Color.Lerp(lit, craterColor, 0.6f * k);
                    }
                }

                if (radius - dist < 1.5f)
                {
                    lit = Color.Lerp(lit, rimColor, 0.8f);
                }

                pixels[y * Size + x] = lit;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = $"MeteorSprite_{seed}";
        cache[seed] = sprite;
        return sprite;
    }
}
