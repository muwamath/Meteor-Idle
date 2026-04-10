using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public class Meteor : MonoBehaviour
{
    [SerializeField] private float fallSpeedMin = 1.2f;
    [SerializeField] private float fallSpeedMax = 2.0f;
    [SerializeField] private float driftMax = 0.4f;
    [SerializeField] private float groundY = -7f;
    [SerializeField] private ParticleSystem debrisPrefab;
    [SerializeField] private FloatingText floatingTextPrefab;

    private SpriteRenderer sr;
    private CircleCollider2D col;
    private Vector2 velocity;
    private float maxHp;
    private float hp;
    private int reward;
    private MeteorSpawner owner;
    private bool dead;

    public float Hp => hp;
    public bool IsAlive => !dead && gameObject.activeInHierarchy;

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
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
        transform.localScale = Vector3.one * sizeScale;

        sr.sprite = MeteorShapeGenerator.GetSprite(seed);
        sr.color = Color.white;

        float drift = Random.Range(-driftMax, driftMax);
        float fall  = Random.Range(fallSpeedMin, fallSpeedMax);
        velocity = new Vector2(drift, -fall);

        maxHp = Mathf.Max(1f, Mathf.Round(sizeScale * 3f));
        hp = maxHp;
        reward = Mathf.Max(1, Mathf.RoundToInt(maxHp * 2f));

        col.radius = 0.45f; // sprite is 128px @ 100ppu = 1.28 world units; lumpy edge ~0.45
    }

    private void Update()
    {
        if (dead) return;
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0, 0, 20f * Time.deltaTime);
        if (transform.position.y < groundY)
        {
            ReturnSilently();
        }
    }

    public void TakeDamage(float amount)
    {
        if (dead) return;
        hp -= amount;
        if (hp <= 0f) Die();
    }

    private void Die()
    {
        if (dead) return;
        dead = true;

        if (debrisPrefab != null)
        {
            var burst = Instantiate(debrisPrefab, transform.position, Quaternion.identity);
            burst.Play();
            Destroy(burst.gameObject, 2f);
        }

        if (floatingTextPrefab != null)
        {
            var ft = Instantiate(floatingTextPrefab, transform.position, Quaternion.identity);
            ft.Show($"+${reward}");
        }

        if (GameManager.Instance != null) GameManager.Instance.AddMoney(reward);
        owner?.Release(this);
    }

    private void ReturnSilently()
    {
        dead = true;
        owner?.Release(this);
    }
}
