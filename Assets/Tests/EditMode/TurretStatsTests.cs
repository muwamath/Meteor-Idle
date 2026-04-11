using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class TurretStatsTests
    {
        private TurretStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<TurretStats>();
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
            Assert.AreEqual(_stats.damage.baseValue, _stats.damage.CurrentValue, 1e-5);
        }

        [Test]
        public void Stat_NextCost_FollowsGrowthFormula()
        {
            var stat = _stats.damage;
            int level0 = stat.NextCost;
            Assert.AreEqual(stat.baseCost, level0);

            stat.level = 1;
            int expectedLevel1 = Mathf.RoundToInt(stat.baseCost * stat.costGrowth);
            Assert.AreEqual(expectedLevel1, stat.NextCost);

            stat.level = 3;
            int expectedLevel3 = Mathf.RoundToInt(stat.baseCost * Mathf.Pow(stat.costGrowth, 3));
            Assert.AreEqual(expectedLevel3, stat.NextCost);
        }

        [Test]
        public void ApplyUpgrade_IncrementsLevel_AndFiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ApplyUpgrade(StatId.FireRate);

            Assert.AreEqual(1, _stats.fireRate.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void ApplyUpgrade_AffectsOnlyTheTargetStat()
        {
            _stats.ApplyUpgrade(StatId.Damage);

            Assert.AreEqual(1, _stats.damage.level);
            Assert.AreEqual(0, _stats.fireRate.level);
            Assert.AreEqual(0, _stats.missileSpeed.level);
            Assert.AreEqual(0, _stats.rotationSpeed.level);
            Assert.AreEqual(0, _stats.blastRadius.level);
            Assert.AreEqual(0, _stats.homing.level);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var stat = _stats.fireRate;
            float atLevel0 = stat.CurrentValue;
            stat.level = 4;
            float atLevel4 = stat.CurrentValue;
            Assert.AreEqual(atLevel0 + 4f * stat.perLevelAdd, atLevel4, 1e-5);
        }

        [Test]
        public void Get_ReturnsCorrectStat()
        {
            Assert.AreSame(_stats.fireRate,     _stats.Get(StatId.FireRate));
            Assert.AreSame(_stats.rotationSpeed,_stats.Get(StatId.RotationSpeed));
            Assert.AreSame(_stats.missileSpeed, _stats.Get(StatId.MissileSpeed));
            Assert.AreSame(_stats.damage,       _stats.Get(StatId.Damage));
            Assert.AreSame(_stats.blastRadius,  _stats.Get(StatId.BlastRadius));
            Assert.AreSame(_stats.homing,       _stats.Get(StatId.Homing));
        }

        [Test]
        public void ResetRuntime_ZerosAllLevels_AndFiresEvent()
        {
            _stats.ApplyUpgrade(StatId.Damage);
            _stats.ApplyUpgrade(StatId.Damage);
            _stats.ApplyUpgrade(StatId.FireRate);
            Assert.AreEqual(2, _stats.damage.level);
            Assert.AreEqual(1, _stats.fireRate.level);

            int events = 0;
            _stats.OnChanged += () => events++;

            _stats.ResetRuntime();

            Assert.AreEqual(0, _stats.damage.level);
            Assert.AreEqual(0, _stats.fireRate.level);
            Assert.AreEqual(1, events);
        }
    }
}
