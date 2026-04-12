using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class DroneStatsTests
    {
        private DroneStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<DroneStats>();
            _stats.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_stats != null) Object.DestroyImmediate(_stats);
        }

        [Test]
        public void NextCost_MatchesGrowthFormula()
        {
            var stat = _stats.thrust;
            Assert.AreEqual(stat.baseCost, stat.NextCost);
            stat.level = 2;
            int expected = Mathf.RoundToInt(stat.baseCost * Mathf.Pow(stat.costGrowth, 2));
            Assert.AreEqual(expected, stat.NextCost);
        }

        [Test]
        public void CurrentValue_GrowsLinearlyWithLevel()
        {
            var stat = _stats.thrust;
            float at0 = stat.CurrentValue;
            stat.level = 3;
            Assert.AreEqual(at0 + 3f * stat.perLevelAdd, stat.CurrentValue, 1e-5);
        }

        [Test]
        public void ApplyUpgrade_IncrementsOnlyTargetStat_FiresEvent()
        {
            int events = 0;
            _stats.OnChanged += () => events++;
            _stats.ApplyUpgrade(DroneStatId.Thrust);
            Assert.AreEqual(1, _stats.thrust.level);
            Assert.AreEqual(0, _stats.batteryCapacity.level);
            Assert.AreEqual(0, _stats.cargoCapacity.level);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void Get_ReturnsCorrectStat()
        {
            Assert.AreSame(_stats.thrust,          _stats.Get(DroneStatId.Thrust));
            Assert.AreSame(_stats.batteryCapacity, _stats.Get(DroneStatId.BatteryCapacity));
            Assert.AreSame(_stats.cargoCapacity,   _stats.Get(DroneStatId.CargoCapacity));
        }

        [Test]
        public void ResetRuntime_ZerosAllLevels()
        {
            _stats.ApplyUpgrade(DroneStatId.Thrust);
            _stats.ApplyUpgrade(DroneStatId.CargoCapacity);
            _stats.ResetRuntime();
            Assert.AreEqual(0, _stats.thrust.level);
            Assert.AreEqual(0, _stats.cargoCapacity.level);
        }

        [Test]
        public void TotalSpentOnUpgrades_MatchesSumOfNextCostAcrossLevels()
        {
            int expected = 0;
            for (int i = 0; i < 3; i++) { expected += _stats.thrust.NextCost; _stats.ApplyUpgrade(DroneStatId.Thrust); }
            Assert.AreEqual(expected, _stats.TotalSpentOnUpgrades());
        }
    }
}
