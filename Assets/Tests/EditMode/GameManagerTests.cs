using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class GameManagerTests
    {
        private GameObject _go;
        private GameManager _gm;

        [SetUp]
        public void SetUp()
        {
            // GameManager is a singleton via a static Instance field. Tear down any
            // leftover Instance from a previous test before each run so [Test]s are
            // isolated.
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }
            _go = new GameObject("TestGameManager", typeof(GameManager));
            _gm = _go.GetComponent<GameManager>();
            TestHelpers.InvokeAwake(_gm);
            Assert.AreSame(_gm, GameManager.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _gm = null;
        }

        [Test]
        public void AddMoney_IncrementsAndFiresEvent()
        {
            int events = 0;
            int lastValue = -1;
            _gm.OnMoneyChanged += v => { events++; lastValue = v; };

            _gm.AddMoney(50);

            Assert.AreEqual(50, _gm.Money);
            Assert.AreEqual(1, events);
            Assert.AreEqual(50, lastValue);
        }

        [Test]
        public void AddMoney_NegativeOrZero_IsNoOp()
        {
            int events = 0;
            _gm.OnMoneyChanged += _ => events++;

            _gm.AddMoney(0);
            _gm.AddMoney(-5);

            Assert.AreEqual(0, _gm.Money);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void TrySpend_InsufficientFunds_ReturnsFalse_NoDeduction()
        {
            _gm.SetMoney(10);
            int events = 0;
            _gm.OnMoneyChanged += _ => events++;

            bool ok = _gm.TrySpend(20);

            Assert.IsFalse(ok);
            Assert.AreEqual(10, _gm.Money);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void TrySpend_Exact_DeductsAndFiresEvent()
        {
            _gm.SetMoney(100);
            int events = 0;
            int lastValue = -1;
            _gm.OnMoneyChanged += v => { events++; lastValue = v; };

            bool ok = _gm.TrySpend(100);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, _gm.Money);
            Assert.AreEqual(1, events);
            Assert.AreEqual(0, lastValue);
        }

        [Test]
        public void TrySpend_NegativeAmount_ReturnsFalse()
        {
            _gm.SetMoney(50);
            bool ok = _gm.TrySpend(-10);
            Assert.IsFalse(ok);
            Assert.AreEqual(50, _gm.Money);
        }

        [Test]
        public void SetMoney_ClampsNegativeToZero()
        {
            _gm.SetMoney(-100);
            Assert.AreEqual(0, _gm.Money);
        }

        [Test]
        public void SetMoney_FiresEventEvenWhenUnchanged()
        {
            _gm.SetMoney(50);
            int events = 0;
            _gm.OnMoneyChanged += _ => events++;

            _gm.SetMoney(50);

            // SetMoney is the debug path — it unconditionally refreshes listeners so
            // UI sync stays correct even when the value happens to match.
            Assert.AreEqual(1, events);
        }

    }
}
