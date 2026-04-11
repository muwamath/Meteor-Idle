using UnityEngine;

// Stretched-sprite VFX spawned by RailgunRound when the round despawns. The
// sprite is positioned at the midpoint of muzzle->impact, rotated to align
// with the line, and scaled along its X axis to match the line length. Y
// scale grows with caliber for thicker streaks on upgraded shots. Alpha
// fades through 4 quantized steps over `duration` seconds — voxel aesthetic
// requires no smooth lerp.
public class RailgunStreak : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private float duration = 2f;

    // 4-step quantized fade. Source-texture-pixel-width step beats a smooth
    // lerp for the voxel aesthetic — each visible drop is chunky and obvious.
    private static readonly float[] AlphaSteps = { 1f, 0.66f, 0.33f, 0f };

    private float timer;

    private void Awake()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duration);
        int idx = Mathf.Min(Mathf.FloorToInt(t * AlphaSteps.Length), AlphaSteps.Length - 1);
        if (sr != null)
        {
            var c = sr.color;
            c.a = AlphaSteps[idx];
            sr.color = c;
        }

        if (t >= 1f) Destroy(gameObject);
    }

    public void Configure(Vector3 from, Vector3 to, int caliber)
    {
        // Position at the midpoint of the line, rotated to align with it,
        // scaled along x to match the world-space length. Source texture
        // is 4 px wide = 0.04 world units at 100 ppu — divide line length
        // by 0.04 to get the scale-x factor.
        Vector3 mid = (from + to) * 0.5f;
        transform.position = mid;

        Vector3 delta = to - from;
        float length = Mathf.Max(0.04f, delta.magnitude);
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        float scaleX = length / 0.04f;
        // Caliber 1 -> 1.0, caliber 2 -> 1.5, caliber 3 -> 2.0
        float scaleY = 1f + Mathf.Max(0, caliber - 1) * 0.5f;
        transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }
}
