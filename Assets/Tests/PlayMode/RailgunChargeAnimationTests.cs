#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode test for RailgunTurret's 4-step quantized barrel charge color
    // animation. The voxel aesthetic forbids smooth color Lerp — the barrel
    // sprite must step through four discrete colors as chargeTimer / chargeDuration
    // crosses 0.25, 0.50, 0.75. A regression that accidentally Lerp'd between
    // them (or reordered the stops) would be a visual aesthetic break; this
    // test is the gate on that.
    //
    // Setup uses the real BaseSlot.prefab because RailgunTurret lives under a
    // weapon child with serialized references (barrel, muzzle, barrelSprite,
    // roundPrefab, statsTemplate) that are painful to reconstruct in code.
    // Build(Railgun) activates the child, InitializeForBuild clones the stats,
    // and we advance chargeTimer via reflection to known fractions of the full
    // charge duration so the test runs instantly instead of waiting the real
    // 5-second baseline fire period.
    public class RailgunChargeAnimationTests : PlayModeTestFixture
    {
        // Mirror of RailgunTurret.ChargeStops (private static). Sourced from
        // RailgunTurret.cs — keep in sync if the aesthetic ever changes.
        private static readonly Color[] ExpectedStops = new Color[]
        {
            new Color(1f,    1f,    1f,    1f),    // dead white
            new Color(0.808f,0.910f,0.996f,1f),    // CEE8FE
            new Color(0.659f,0.839f,0.996f,1f),    // A8D6FE
            new Color(0.576f,0.855f,0.996f,1f),    // 93DAFE
        };

        private BaseSlot _slot;
        private RailgunTurret _turret;
        private SpriteRenderer _barrelSprite;
        private FieldInfo _chargeTimerField;
        private float _chargeDuration;

        private IEnumerator SetupRailgunTurret()
        {
            yield return SetupScene();

            var slotPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<BaseSlot>(
                "Assets/Prefabs/BaseSlot.prefab");
            Assert.IsNotNull(slotPrefab, "BaseSlot.prefab must load from Assets/Prefabs");

            _slot = Object.Instantiate(slotPrefab);
            _slot.name = "TestBaseSlot";
            _slot.transform.position = new Vector3(-100f, -100f, 0f);
            _slot.Build(WeaponType.Railgun, 0);

            _turret = _slot.GetComponentInChildren<RailgunTurret>(true);
            Assert.IsNotNull(_turret, "RailgunTurret should exist under BaseSlot prefab");

            // Clear any spawner the Awake fallback may have picked up from
            // another test's leftover scene so the turret has no target and
            // never attempts to Fire during our ticks.
            _turret.SetRuntimeRefs(null);

            _barrelSprite = GetPrivateField<SpriteRenderer>(_turret, "barrelSprite");
            Assert.IsNotNull(_barrelSprite, "barrelSprite should be serialized on the prefab");

            var stats = _turret.Stats;
            Assert.IsNotNull(stats, "statsInstance should be set by InitializeForBuild");
            _chargeDuration = 1f / Mathf.Max(0.05f, stats.fireRate.CurrentValue);

            _chargeTimerField = typeof(RailgunTurret).GetField(
                "chargeTimer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_chargeTimerField, "chargeTimer field not found");
        }

        private IEnumerator AssertStopAt(float tFraction, int expectedIdx)
        {
            _chargeTimerField.SetValue(_turret, _chargeDuration * tFraction);
            yield return null; // one Update tick — color is written inside Update

            Color actual = _barrelSprite.color;
            Color expected = ExpectedStops[expectedIdx];
            Assert.AreEqual(expected.r, actual.r, 0.01f, $"R at t={tFraction}");
            Assert.AreEqual(expected.g, actual.g, 0.01f, $"G at t={tFraction}");
            Assert.AreEqual(expected.b, actual.b, 0.01f, $"B at t={tFraction}");
        }

        [UnityTest]
        public IEnumerator ChargeStop_Zero_AtLowCharge()
        {
            yield return SetupRailgunTurret();
            // t=0.10 → 0.10*4 = 0.4 → FloorToInt = 0 → dead white
            yield return AssertStopAt(0.10f, 0);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator ChargeStop_One_AtFirstBucket()
        {
            yield return SetupRailgunTurret();
            // t=0.35 → 1.4 → 1 → #CEE8FE
            yield return AssertStopAt(0.35f, 1);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator ChargeStop_Two_AtSecondBucket()
        {
            yield return SetupRailgunTurret();
            // t=0.60 → 2.4 → 2 → #A8D6FE
            yield return AssertStopAt(0.60f, 2);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator ChargeStop_Three_AtFullCharge()
        {
            yield return SetupRailgunTurret();
            // t=0.85 → 3.4 → 3 → #93DAFE (full charge, held until fire)
            yield return AssertStopAt(0.85f, 3);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator InitializeForBuild_ResetsToDeadWhite()
        {
            yield return SetupRailgunTurret();

            // Dirty the charge state to a late bucket, then re-InitializeForBuild:
            // the turret must snap back to ChargeStops[0].
            _chargeTimerField.SetValue(_turret, _chargeDuration * 0.85f);
            yield return null; // let Update paint the sprite to stop 3
            Assert.AreEqual(ExpectedStops[3].r, _barrelSprite.color.r, 0.01f);

            _turret.InitializeForBuild();
            Assert.AreEqual(ExpectedStops[0].r, _barrelSprite.color.r, 0.01f);
            Assert.AreEqual(ExpectedStops[0].g, _barrelSprite.color.g, 0.01f);
            Assert.AreEqual(ExpectedStops[0].b, _barrelSprite.color.b, 0.01f);

            TeardownScene();
        }

        // --- reflection helper ---------------------------------------------

        private static T GetPrivateField<T>(object target, string name) where T : class
        {
            var t = target.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(target) as T;
                t = t.BaseType;
            }
            return null;
        }
    }
}
#endif
