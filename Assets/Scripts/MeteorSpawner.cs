using UnityEngine;

public class MeteorSpawner : MonoBehaviour
{
    [SerializeField] private Meteor meteorPrefab;
    [SerializeField] private Transform poolParent;
    [SerializeField] private float spawnY = 10f;
    [SerializeField] private float spawnXRange = 15f;
    [SerializeField] private float initialInterval = 12.0f;
    [SerializeField] private float minInterval = 4.5f;
    [SerializeField] private float rampDurationSeconds = 180f;
    [SerializeField] private int prewarm = 12;

    private SimplePool<Meteor> pool;
    private float timer;
    private float elapsed;
    private bool bossActive;
    private Meteor activeBoss;

    private void Awake()
    {
        pool = new SimplePool<Meteor>(meteorPrefab, poolParent != null ? poolParent : transform, prewarm);
    }

    private void Start()
    {
        if (LevelState.Instance != null)
        {
            LevelState.Instance.OnLevelChanged += OnLevelChanged;
            LevelState.Instance.OnBossFailed += OnBossFailed;
        }
    }

    private void OnDestroy()
    {
        if (LevelState.Instance != null)
        {
            LevelState.Instance.OnLevelChanged -= OnLevelChanged;
            LevelState.Instance.OnBossFailed -= OnBossFailed;
        }
    }

    private void Update()
    {
        if (bossActive) return;

        elapsed += Time.deltaTime;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            SpawnOne();
            timer = CurrentInterval();
        }
    }

    private float CurrentInterval()
    {
        float initial, min;
        if (LevelState.Instance != null)
        {
            initial = LevelState.Instance.SpawnInitialInterval;
            min = LevelState.Instance.SpawnMinInterval;
        }
        else
        {
            initial = initialInterval;
            min = minInterval;
        }
        float t = Mathf.Clamp01(elapsed / rampDurationSeconds);
        return Mathf.Lerp(initial, min, t);
    }

    private void SpawnOne()
    {
        var meteor = pool.Get();
        float range = spawnXRange;
        if (Camera.main != null)
            range = Camera.main.orthographicSize * Camera.main.aspect * 0.9f;
        float x = Random.Range(-range, range);
        int seed = Random.Range(0, int.MaxValue);

        float sizeMin = 0.525f;
        float sizeMax = 1.2f;
        if (LevelState.Instance != null)
        {
            (sizeMin, sizeMax) = LevelState.Instance.MeteorSizeRange;
        }
        float size = Random.Range(sizeMin, sizeMax);
        meteor.Spawn(this, new Vector3(x, spawnY, 0f), seed, size);
    }

    private void OnLevelChanged()
    {
        if (LevelState.Instance != null && LevelState.Instance.IsBossLevel)
        {
            SpawnBoss();
        }
        else
        {
            bossActive = false;
            elapsed = 0f; // reset spawn ramp for new level
        }
    }

    private void OnBossFailed()
    {
        ClearAllMeteors();
    }

    private void SpawnBoss()
    {
        bossActive = true;
        activeBoss = pool.Get();
        float x = 0f;
        int seed = Random.Range(0, int.MaxValue);
        float size = LevelState.Instance != null ? LevelState.Instance.BossSize : 1.2f;
        activeBoss.Spawn(this, new Vector3(x, spawnY, 0f), seed, size);
        float fallSpeed = LevelState.Instance != null ? LevelState.Instance.BossFallSpeed : 0.3f;
        activeBoss.SetBossMode(fallSpeed);
        LevelState.Instance?.NotifyBossSpawned();
    }

    public void ClearAllMeteors()
    {
        for (int i = pool.Active.Count - 1; i >= 0; i--)
        {
            var m = pool.Active[i];
            if (m != null && m.gameObject.activeSelf)
            {
                pool.Release(m);
            }
        }
        bossActive = false;
        activeBoss = null;
    }

    public void Release(Meteor meteor) => pool.Release(meteor);

    public System.Collections.Generic.IReadOnlyList<Meteor> ActiveMeteors => pool.Active;
}
