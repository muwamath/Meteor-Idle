using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class SlotManagerBuildCostTests
    {
        private GameObject _go;
        private SlotManager _slotManager;
        private FieldInfo _builtPurchasedCountField;

        [SetUp]
        public void SetUp()
        {
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

        // --- Missile (defaults: {1, 1}) -------------------------------

        [Test]
        public void Missile_InTable_FirstPurchase()
        {
            SetPurchasedCount(0);
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_InTable_SecondPurchase()
        {
            SetPurchasedCount(1);
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_Overflow_FirstStepOffTable()
        {
            // count=2, len=2 → 1 * (2 - 2 + 2) = 2.
            SetPurchasedCount(2);
            Assert.AreEqual(2, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        [Test]
        public void Missile_Overflow_DeepStep()
        {
            // count=5, len=2 → 1 * (5 - 2 + 2) = 5.
            SetPurchasedCount(5);
            Assert.AreEqual(5, _slotManager.NextBuildCost(WeaponType.Missile));
        }

        // --- Railgun (defaults: {1, 1}) -------------------------------

        [Test]
        public void Railgun_InTable_FirstPurchase()
        {
            SetPurchasedCount(0);
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_InTable_SecondPurchase()
        {
            SetPurchasedCount(1);
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_Overflow_FirstStepOffTable()
        {
            // count=2, len=2 → 1 * 2 = 2.
            SetPurchasedCount(2);
            Assert.AreEqual(2, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        [Test]
        public void Railgun_Overflow_DeepStep()
        {
            // count=5, len=2 → 1 * 5 = 5.
            SetPurchasedCount(5);
            Assert.AreEqual(5, _slotManager.NextBuildCost(WeaponType.Railgun));
        }

        // --- Shared slot tier across weapons ------------------------------

        [Test]
        public void PurchasedCount_IsSharedAcrossWeapons()
        {
            SetPurchasedCount(1);
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Missile));
            Assert.AreEqual(1, _slotManager.NextBuildCost(WeaponType.Railgun));
        }
    }
}
