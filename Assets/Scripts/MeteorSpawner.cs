using UnityEngine;

public class MeteorSpawner : MonoBehaviour
{
    [SerializeField] private Meteor meteorPrefab;
    [SerializeField] private Transform poolParent;
    [SerializeField] private float spawnY = 10f;
    [SerializeField] private float spawnXRange = 15f;
    [SerializeField] private float initialInterval = 2.5f;
    [SerializeField] private float minInterval = 0.5f;
    [SerializeField] private float rampDurationSeconds = 120f;
    [SerializeField] private int prewarm = 12;

    private SimplePool<Meteor> pool;
    private float timer;
    private float elapsed;

    private void Awake()
    {
        pool = new SimplePool<Meteor>(meteorPrefab, poolParent != null ? poolParent : transform, prewarm);
        // Spawned meteors need to start deactivated; SimplePool handles that.
        // Reset their owner on first Get by calling Spawn.
    }

    private void Update()
    {
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
        float t = Mathf.Clamp01(elapsed / rampDurationSeconds);
        return Mathf.Lerp(initialInterval, minInterval, t);
    }

    private void SpawnOne()
    {
        var meteor = pool.Get();
        float x = Random.Range(-spawnXRange, spawnXRange);
        int seed = Random.Range(0, int.MaxValue);
        float size = Random.Range(0.7f, 1.6f);
        meteor.Spawn(this, new Vector3(x, spawnY, 0f), seed, size);
    }

    public void Release(Meteor meteor) => pool.Release(meteor);

    public System.Collections.Generic.IReadOnlyList<Meteor> ActiveMeteors => pool.Active;
}
