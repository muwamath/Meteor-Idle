using System.Collections;
using UnityEngine;

namespace MeteorIdle.Tests.PlayMode
{
    // Shared helpers for PlayMode tests. Each test inherits from this fixture to
    // get scene setup/teardown and spawn-helper methods. PlayMode tests run the
    // real game loop — Awake fires automatically when a GameObject becomes
    // active, so no reflection is needed (unlike EditMode tests).
    public abstract class PlayModeTestFixture
    {
        protected GameManager _gameManager;
        protected Transform _poolParent;

        protected IEnumerator SetupScene()
        {
            if (GameManager.Instance == null)
            {
                var gmGo = new GameObject("TestGameManager", typeof(GameManager));
                _gameManager = gmGo.GetComponent<GameManager>();
            }
            else
            {
                _gameManager = GameManager.Instance;
            }

            var poolParentGo = new GameObject("TestPoolParent");
            _poolParent = poolParentGo.transform;

            yield return null;
        }

        // Creates a MeteorSpawner with the real Meteor prefab and pool parent
        // assigned BEFORE Awake runs. Necessary because MeteorSpawner.Awake
        // prewarms the pool immediately, which throws if meteorPrefab is null.
        protected MeteorSpawner SpawnTestSpawner()
        {
#if UNITY_EDITOR
            var meteorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Meteor>(
                "Assets/Prefabs/Meteor.prefab");

            var go = new GameObject("TestMeteorSpawner");
            go.SetActive(false); // keep inactive so AddComponent doesn't fire Awake yet
            var spawner = go.AddComponent<MeteorSpawner>();

            var so = new UnityEditor.SerializedObject(spawner);
            so.FindProperty("meteorPrefab").objectReferenceValue = meteorPrefab;
            so.FindProperty("poolParent").objectReferenceValue = _poolParent;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(true); // now Awake fires with valid fields
            return spawner;
#else
            throw new System.NotSupportedException("TestSpawner creation is editor-only");
#endif
        }

        protected void TeardownScene()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go == null) continue;
                if (go.name.StartsWith("Test")) Object.Destroy(go);
            }
        }

        protected Meteor SpawnTestMeteor(Vector3 position, int seed = 1, float scale = 1f)
        {
            var go = new GameObject(
                "TestMeteor",
                typeof(SpriteRenderer),
                typeof(CircleCollider2D),
                typeof(Meteor));
            // Assign the Meteors physics layer so RailgunRound's per-frame
            // raycasts (which filter against this layer only) can find the
            // test meteor. Missile collisions still work because Unity's
            // default Physics2D collision matrix allows all-vs-all unless
            // explicitly disabled.
            int meteorsLayer = LayerMask.NameToLayer("Meteors");
            if (meteorsLayer >= 0) go.layer = meteorsLayer;

            var meteor = go.GetComponent<Meteor>();

            // Iter 2: inject the MaterialRegistry before Spawn so the
            // generator emits VoxelMaterial[,] for stone/gold/explosive
            // placements. Without this, material[,] stays null and the
            // Iter 1 backward-compat path runs (cores still work, but new
            // material variety is invisible).
#if UNITY_EDITOR
            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>(
                "Assets/Data/MaterialRegistry.asset");
            if (registry != null)
            {
                var f = typeof(Meteor).GetField("materialRegistry",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f?.SetValue(meteor, registry);
            }
            var coreDropPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<CoreDrop>(
                "Assets/Prefabs/CoreDrop.prefab");
            if (coreDropPrefab != null)
            {
                var f = typeof(Meteor).GetField("coreDropPrefab",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f?.SetValue(meteor, coreDropPrefab);
            }
#endif

            meteor.Spawn(null, position, seed, scale);
            return meteor;
        }

        protected Missile SpawnTestMissile(Vector3 position)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Missile>(
                "Assets/Prefabs/Missile.prefab");
            var missile = Object.Instantiate(prefab, position, Quaternion.identity);
            missile.name = "TestMissile";
            return missile;
#else
            throw new System.NotSupportedException("TestMissile spawn is editor-only");
#endif
        }

        protected (CollectorDrone drone, DroneTestEnvironment env) SpawnTestDroneWithEnv(
            Vector3 position,
            Vector3 bayPosition)
        {
            var env = new DroneTestEnvironment { BayPosition = bayPosition, BayDoorsOpen = true };
            var go = new GameObject("TestDrone", typeof(CollectorDrone));
            go.transform.position = position;
            var drone = go.GetComponent<CollectorDrone>();
            drone.Initialize(env,
                thrust: 8f, damping: 0.5f,
                batteryCapacity: 60f, cargoCapacity: 1,
                reserveThresholdFraction: 0.4f,
                pickupRadius: 0.4f, dockRadius: 0.5f);
            return (drone, env);
        }

        protected RailgunRound SpawnTestRailgunRound(
            Vector3 spawnPos,
            Vector2 direction,
            float speed,
            int weight,
            int caliber)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<RailgunRound>(
                "Assets/Prefabs/RailgunRound.prefab");
            var round = Object.Instantiate(prefab);
            round.name = "TestRailgunRound";
            round.Configure(spawnPos, direction, speed, weight, caliber);
            return round;
#else
            throw new System.NotSupportedException("TestRailgunRound spawn is editor-only");
#endif
        }
    }

    public class DroneTestEnvironment : ICollectorDroneEnvironment
    {
        public Vector3 BayPosition { get; set; }
        public bool BayDoorsOpen { get; set; }
        public int TotalDeposited;
        public void RequestOpenDoors()  { BayDoorsOpen = true; }
        public void RequestCloseDoors() { BayDoorsOpen = false; }
        public void Deposit(int value)
        {
            TotalDeposited += value;
            if (GameManager.Instance != null) GameManager.Instance.AddMoney(value);
        }
        public CoreDrop FindNearestUnclaimedDrop(Vector3 from, float maxDistance)
        {
            if (GameManager.Instance == null) return null;
            CoreDrop best = null;
            float bestD = float.MaxValue;
            foreach (var d in GameManager.Instance.ActiveDrops)
            {
                if (d == null || d.IsClaimed || !d.IsAlive) continue;
                float dist = Vector3.Distance(from, d.Position);
                if (dist > maxDistance) continue;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }
    }
}
