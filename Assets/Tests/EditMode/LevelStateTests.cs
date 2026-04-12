using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class LevelStateTests
    {
        private LevelState _state;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            if (LevelState.Instance != null)
                Object.DestroyImmediate(LevelState.Instance.gameObject);
            _go = new GameObject("LevelState", typeof(LevelState));
            _state = _go.GetComponent<LevelState>();
            TestHelpers.InvokeAwake(_state);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _state = null;
        }

        [Test]
        public void StartsAtLevel1()
        {
            Assert.AreEqual(1, _state.CurrentLevel);
        }

        [Test]
        public void CurrentBlock_Level1Through10_IsBlock0()
        {
            Assert.AreEqual(0, _state.CurrentBlock);
        }

        [Test]
        public void LevelInBlock_Level1_Is1()
        {
            Assert.AreEqual(1, _state.LevelInBlock);
        }

        [Test]
        public void IsBossLevel_Level1_IsFalse()
        {
            Assert.IsFalse(_state.IsBossLevel);
        }

        [Test]
        public void IsBossLevel_Level10_IsTrue()
        {
            SetLevel(10);
            Assert.IsTrue(_state.IsBossLevel);
        }

        [Test]
        public void Threshold_Level1_IsBaseCost()
        {
            // baseCost=10, growthRate=1.08, level 1: 10 * 1.08^0 = 10
            Assert.AreEqual(10, _state.Threshold);
        }

        [Test]
        public void Threshold_ScalesExponentially()
        {
            SetLevel(5);
            int expected = Mathf.RoundToInt(10f * Mathf.Pow(1.08f, 4));
            Assert.AreEqual(expected, _state.Threshold);
        }

        [Test]
        public void Threshold_BossLevel_IsZero()
        {
            SetLevel(10);
            Assert.AreEqual(0, _state.Threshold);
        }

        [Test]
        public void TryAdvance_BelowThreshold_ReturnsFalse()
        {
            Assert.IsFalse(_state.TryAdvance(5));
            Assert.AreEqual(1, _state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AtThreshold_AdvancesAndReturnsTrue()
        {
            Assert.IsTrue(_state.TryAdvance(10));
            Assert.AreEqual(2, _state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AboveThreshold_AdvancesAndReturnsTrue()
        {
            Assert.IsTrue(_state.TryAdvance(50));
            Assert.AreEqual(2, _state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AtBossLevel_ReturnsFalse()
        {
            SetLevel(10);
            Assert.IsTrue(_state.IsBossLevel);
            Assert.IsFalse(_state.TryAdvance(999999));
        }

        [Test]
        public void TryAdvance_FiresOnLevelChanged()
        {
            int firedCount = 0;
            _state.OnLevelChanged += () => firedCount++;
            _state.TryAdvance(10);
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void TryAdvance_BelowThreshold_DoesNotFireEvent()
        {
            int firedCount = 0;
            _state.OnLevelChanged += () => firedCount++;
            _state.TryAdvance(5);
            Assert.AreEqual(0, firedCount);
        }

        [Test]
        public void BossDefeated_AdvancesToNextBlock()
        {
            SetLevel(10);
            _state.BossDefeated();
            Assert.AreEqual(11, _state.CurrentLevel);
            Assert.AreEqual(1, _state.CurrentBlock);
        }

        [Test]
        public void BossFailed_Block1_ResetsToLevel1()
        {
            SetLevel(10);
            _state.BossFailed();
            Assert.AreEqual(1, _state.CurrentLevel);
        }

        [Test]
        public void BossFailed_Block2_ResetsToLevel11()
        {
            SetLevel(20);
            _state.BossFailed();
            Assert.AreEqual(11, _state.CurrentLevel);
        }

        [Test]
        public void BossFailed_FiresOnBossFailed()
        {
            SetLevel(10);
            int firedCount = 0;
            _state.OnBossFailed += () => firedCount++;
            _state.BossFailed();
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void BossFailed_AlsoFiresOnLevelChanged()
        {
            SetLevel(10);
            int firedCount = 0;
            _state.OnLevelChanged += () => firedCount++;
            _state.BossFailed();
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void LevelInBlock_Level11_Is1()
        {
            SetLevel(11);
            Assert.AreEqual(1, _state.LevelInBlock);
        }

        [Test]
        public void LevelInBlock_Level20_Is10()
        {
            SetLevel(20);
            Assert.AreEqual(10, _state.LevelInBlock);
        }

        private void SetLevel(int level)
        {
            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_state, level);
        }
    }
}
