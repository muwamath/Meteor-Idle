using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class RailgunStatsTests
    {
        private RailgunStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<RailgunStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
            _stats = null;
        }

        [Test]
        public void Stat_CurrentValue_BaseAtLevelZero()
        {
            Assert.AreEqual(_stats.fireRate.baseValue, _stats.fireRate.CurrentValue, 1e-5);
            Assert.AreEqual(_stats.weight.baseValue, _stats.weight.CurrentValue, 1e-5);
            Assert.AreEqual(_stats.caliber.baseValue, _stats.caliber.CurrentValue, 1e-5);
        }

        [Test]
        public void Stat_NextCost_FollowsGrowthFormula()
        {
            var stat = _stats.weight;
            Assert.AreEqual(stat.baseCost, stat.NextCost);

            stat.level = 3;
            int expected = Mathf.RoundToInt(stat.baseCost * Mathf.Pow(stat.costGrowth, 3));
            Assert.AreEqual(expected, stat.NextCost);
        }

        [Test]
        public void ApplyUpgrade_IncrementsLevel_AndFiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ApplyUpgrade(RailgunStatId.Speed);

            Assert.AreEqual(1, _stats.speed.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void ApplyUpgrade_AffectsOnlyTheTargetStat()
        {
            _stats.ApplyUpgrade(RailgunStatId.Weight);

            Assert.AreEqual(1, _stats.weight.level);
            Assert.AreEqual(0, _stats.fireRate.level);
            Assert.AreEqual(0, _stats.rotationSpeed.level);
            Assert.AreEqual(0, _stats.speed.level);
            Assert.AreEqual(0, _stats.caliber.level);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var stat = _stats.speed;
            float at0 = stat.CurrentValue;
            stat.level = 5;
            float at5 = stat.CurrentValue;
            Assert.AreEqual(at0 + 5f * stat.perLevelAdd, at5, 1e-5);
        }

        [Test]
        public void Get_ReturnsCorrectStat()
        {
            Assert.AreSame(_stats.fireRate,      _stats.Get(RailgunStatId.FireRate));
            Assert.AreSame(_stats.rotationSpeed, _stats.Get(RailgunStatId.RotationSpeed));
            Assert.AreSame(_stats.speed,         _stats.Get(RailgunStatId.Speed));
            Assert.AreSame(_stats.weight,        _stats.Get(RailgunStatId.Weight));
            Assert.AreSame(_stats.caliber,       _stats.Get(RailgunStatId.Caliber));
        }

        [Test]
        public void ResetRuntime_ZerosAllLevels_AndFiresEvent()
        {
            _stats.ApplyUpgrade(RailgunStatId.Speed);
            _stats.ApplyUpgrade(RailgunStatId.Speed);
            _stats.ApplyUpgrade(RailgunStatId.Weight);
            Assert.AreEqual(2, _stats.speed.level);
            Assert.AreEqual(1, _stats.weight.level);

            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ResetRuntime();

            Assert.AreEqual(0, _stats.speed.level);
            Assert.AreEqual(0, _stats.weight.level);
            Assert.AreEqual(1, events);
        }
    }
}
