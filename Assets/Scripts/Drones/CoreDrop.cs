using UnityEngine;

// A pooled floating entity spawned when a Core voxel is destroyed. Drifts
// downward at roughly 15% of base meteor speed until a drone grabs it or it
// falls off the bottom of the screen. Pays `value` when a drone deposits it
// at a bay — not on break.
[RequireComponent(typeof(SpriteRenderer))]
public class CoreDrop : MonoBehaviour
{
    [SerializeField] private float driftSpeed = 0.08f;
    [SerializeField] private float despawnY = -9.2f;
    [SerializeField] private Color dropColor = new Color(0.75f, 0.25f, 0.25f, 1f);

    private SpriteRenderer sr;
    private int value;
    private bool claimed;
    private bool alive;

    public int Value => value;
    public bool IsClaimed => claimed;
    public bool IsAlive => alive && gameObject.activeInHierarchy;
    public Vector3 Position => transform.position;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite == null)
        {
            // Fallback sprite so EditMode tests don't need a prefab: a single
            // red pixel stretched to ~0.3 world units.
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, dropColor);
            tex.Apply();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 3f);
        }
    }

    public void Spawn(Vector3 position, int value)
    {
        transform.position = position;
        this.value = value;
        claimed = false;
        alive = true;
        if (sr != null) sr.color = Color.white;
    }

    private void Update()
    {
        TickDrift(Time.deltaTime);
    }

    public void TickDrift(float dt)
    {
        if (!alive) return;
        transform.position += new Vector3(0f, -driftSpeed * dt, 0f);
        if (transform.position.y < despawnY) alive = false;
    }

    // Single-winner claim: first drone to call TryClaim gets the drop.
    // Later callers see IsClaimed==true and pick a different target.
    public bool TryClaim()
    {
        if (claimed || !alive) return false;
        claimed = true;
        return true;
    }

    // Called by a drone once the drop has been physically delivered to the
    // bay. Pays out and returns the drop to the pool.
    public void Consume()
    {
        alive = false;
        claimed = false;
        gameObject.SetActive(false);
    }
}
