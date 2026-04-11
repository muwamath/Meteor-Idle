using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Exercises MeteorSpawner.CurrentInterval — the calm→fast ramp formula the
    // user explicitly tuned down. CLAUDE.md warns not to regress these numbers.
    // Both elapsed (input) and CurrentInterval (private) are reached via
    // reflection; Awake is never invoked because the ramp formula touches no
    // pool state, and MeteorSpawner.Awake would throw without a meteor prefab.
    public class MeteorSpawnerIntervalTests
    {
        private GameObject _go;
        private MeteorSpawner _spawner;
        private FieldInfo _elapsedField;
        private MethodInfo _currentIntervalMethod;

        // Mirror the serialized defaults in MeteorSpawner.cs. If the defaults
        // ever drift, update here alongside the source — the test's whole job
        // is to pin them.
        private const float InitialInterval = 4.0f;
        private const float MinInterval = 1.5f;
        private const float RampDurationSeconds = 180f;

        [SetUp]
        public void SetUp()
        {
            // Start inactive so AddComponent can't accidentally fire Awake
            // (which would NRE on the null meteorPrefab).
            _go = new GameObject("TestSpawner");
            _go.SetActive(false);
            _spawner = _go.AddComponent<MeteorSpawner>();

            var t = typeof(MeteorSpawner);
            _elapsedField = t.GetField("elapsed", BindingFlags.NonPublic | BindingFlags.Instance);
            _currentIntervalMethod = t.GetMethod(
                "CurrentInterval",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_elapsedField, "elapsed field not found");
            Assert.IsNotNull(_currentIntervalMethod, "CurrentInterval method not found");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _spawner = null;
        }

        private float CallCurrentInterval(float elapsedSeconds)
        {
            _elapsedField.SetValue(_spawner, elapsedSeconds);
            return (float)_currentIntervalMethod.Invoke(_spawner, null);
        }

        [Test]
        public void AtZeroElapsed_ReturnsInitialInterval()
        {
            Assert.AreEqual(InitialInterval, CallCurrentInterval(0f), 1e-5);
        }

        [Test]
        public void AtFullRamp_ReturnsMinInterval()
        {
            Assert.AreEqual(MinInterval, CallCurrentInterval(RampDurationSeconds), 1e-5);
        }

        [Test]
        public void BeyondFullRamp_ClampsToMinInterval()
        {
            // Once past rampDurationSeconds the formula clamps — spawns should
            // never drop below minInterval no matter how long the run lasts.
            Assert.AreEqual(MinInterval, CallCurrentInterval(RampDurationSeconds * 10f), 1e-5);
        }

        [Test]
        public void AtMidpoint_LerpsHalfway()
        {
            float expected = Mathf.Lerp(InitialInterval, MinInterval, 0.5f);
            Assert.AreEqual(expected, CallCurrentInterval(RampDurationSeconds * 0.5f), 1e-5);
        }

        [Test]
        public void MonotonicallyDecreasing_AcrossRamp()
        {
            // The ramp is a lerp — every step forward in elapsed must return
            // an interval less-than-or-equal-to the previous step.
            float prev = CallCurrentInterval(0f);
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float now = CallCurrentInterval(t * RampDurationSeconds);
                Assert.LessOrEqual(now, prev + 1e-5f,
                    $"interval went UP between t={(i - 1) / 10f} and t={t}");
                prev = now;
            }
        }
    }
}
