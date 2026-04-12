using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class BayStatsTests
    {
        private BayStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<BayStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
        }

        [Test]
        public void DronesPerBay_MaxLevelTwo()
        {
            Assert.AreEqual(2, _stats.dronesPerBay.maxLevel);
        }

        [Test]
        public void DronesPerBay_CannotExceedMaxLevel()
        {
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            Assert.AreEqual(2, _stats.dronesPerBay.level);
            _stats.ApplyUpgrade(BayStatId.DronesPerBay);
            Assert.AreEqual(2, _stats.dronesPerBay.level, "IsMaxed blocks further upgrades");
        }

        [Test]
        public void ReloadSpeed_UncappedByDefault()
        {
            Assert.AreEqual(0, _stats.reloadSpeed.maxLevel);
        }

        [Test]
        public void NextCost_MatchesGrowthFormula()
        {
            var s = _stats.reloadSpeed;
            Assert.AreEqual(s.baseCost, s.NextCost);
            s.level = 2;
            int expected = Mathf.RoundToInt(s.baseCost * Mathf.Pow(s.costGrowth, 2));
            Assert.AreEqual(expected, s.NextCost);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var s = _stats.reloadSpeed;
            float at0 = s.CurrentValue;
            s.level = 4;
            Assert.AreEqual(at0 + 4f * s.perLevelAdd, s.CurrentValue, 1e-5);
        }

        [Test]
        public void ApplyUpgrade_IncrementsOnlyTargetStat_FiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;
            _stats.ApplyUpgrade(BayStatId.ReloadSpeed);
            Assert.AreEqual(1, _stats.reloadSpeed.level);
            Assert.AreEqual(0, _stats.dronesPerBay.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void TotalSpentOnUpgrades_MatchesSumOfNextCostAcrossLevels()
        {
            int expected = 0;
            for (int i = 0; i < 3; i++) { expected += _stats.reloadSpeed.NextCost; _stats.ApplyUpgrade(BayStatId.ReloadSpeed); }
            Assert.AreEqual(expected, _stats.TotalSpentOnUpgrades());
        }
    }
}
