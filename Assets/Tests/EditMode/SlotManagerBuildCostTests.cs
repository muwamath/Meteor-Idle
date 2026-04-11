using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Exercises SlotManager.NextBuildCost — the per-weapon build-cost escalation
    // formula. In-table values come straight from the serialized arrays
    // (missileBuildCosts, railgunBuildCosts); beyond the table the last entry
    // scales linearly as costs[^1] * (count - len + 2). This economy formula
    // has zero gameplay tests, so a silent regression here would break balance
    // without any test turning red.
    public class SlotManagerBuildCostTests
    {
        private GameObject _go;
        private SlotManager _slotManager;
        private FieldInfo _builtPurchasedCountField;

        [SetUp]
        public void SetUp()
        {
            // SlotManager.Start bails out gracefully when slotPrefab is null and
            // Awake is a no-op, so a bare component is safe even if EditMode
            // happens to fire lifecycle methods.
            _go = new GameObject("TestSlotManager");
            _slotManager = _go.AddComponent<SlotManager>();
            _builtPurchasedCountField = typeof(SlotManager).GetField(
                "builtPurchasedCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_builtPurchasedCountField, "builtPurchasedCount field not found");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _slotManager = null;
        }

        private void SetPurchasedCount(int n)
        {
            _builtPurchasedCountField.SetValue(_slotManager, n);
        }

        // --- Missile (defaults: {100, 300}) -------------------------------

        [Test]
        public void Missile_InTable_FirstPurchase()
        {
            SetPurchasedCount(0);
            Assert.AreEqual(100, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_InTable_SecondPurchase()
        {
            SetPurchasedCount(1);
            Assert.AreEqual(300, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_Overflow_FirstStepOffTable()
        {
            // count=2, len=2 → 300 * (2 - 2 + 2) = 600.
            SetPurchasedCount(2);
            Assert.AreEqual(600, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_Overflow_DeepStep()
        {
            // count=5, len=2 → 300 * (5 - 2 + 2) = 1500.
            SetPurchasedCount(5);
            Assert.AreEqual(1500, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        // --- Railgun (defaults: {200, 600}) -------------------------------

        [Test]
        public void Railgun_InTable_FirstPurchase()
        {
            SetPurchasedCount(0);
            Assert.AreEqual(200, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_InTable_SecondPurchase()
        {
            SetPurchasedCount(1);
            Assert.AreEqual(600, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_Overflow_FirstStepOffTable()
        {
            // count=2, len=2 → 600 * 2 = 1200.
            SetPurchasedCount(2);
            Assert.AreEqual(1200, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_Overflow_DeepStep()
        {
            // count=5, len=2 → 600 * 5 = 3000.
            SetPurchasedCount(5);
            Assert.AreEqual(3000, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        // --- Shared slot tier across weapons ------------------------------

        [Test]
        public void PurchasedCount_IsSharedAcrossWeapons()
        {
            // Whichever weapon you bought for your Nth slot, it still counts
            // as your Nth purchase for the escalation table — the two weapons
            // read the same builtPurchasedCount.
            SetPurchasedCount(1);
            Assert.AreEqual(300, _slotManager.NextBuildCost(WeaponType.Missile));
            Assert.AreEqual(600, _slotManager.NextBuildCost(WeaponType.Railgun));
        }
    }
}
