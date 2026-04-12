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
        public void Threshold_BossLevel_IsZero()
        {
            SetLevel(10);
            Assert.AreEqual(0, _state.Threshold);
        }

        // --- Core kill advancement tests ---

        [Test]
        public void Threshold_Level1_ReturnsBaseCoreKills()
        {
            // baseCoreKills=3, exponent=0.5: 3 * Pow(1, 0.5) = 3
            Assert.AreEqual(3, _state.Threshold);
        }

        [Test]
        public void Threshold_ScalesWithLevel()
        {
            SetLevel(10);
            // Not a boss level check — level 10 IS boss. Use level 9.
            SetLevel(9);
            int expected = Mathf.RoundToInt(3f * Mathf.Pow(9f, 0.5f)); // ~9
            Assert.AreEqual(expected, _state.Threshold);
        }

        [Test]
        public void RecordCoreKill_IncrementsCounter()
        {
            _state.RecordCoreKill();
            Assert.AreEqual(1, _state.CoreKillsThisBlock);
        }

        [Test]
        public void RecordCoreKill_BelowThreshold_DoesNotAdvance()
        {
            _state.RecordCoreKill();
            Assert.AreEqual(1, _state.CurrentLevel);
        }

        [Test]
        public void RecordCoreKill_AtThreshold_AdvancesLevel()
        {
            int threshold = _state.Threshold; // 3 at level 1
            for (int i = 0; i < threshold; i++)
                _state.RecordCoreKill();
            Assert.AreEqual(2, _state.CurrentLevel);
            Assert.AreEqual(0, _state.CoreKillsThisBlock);
        }

        [Test]
        public void RecordCoreKill_AtBossLevel_IsNoOp()
        {
            SetLevel(10);
            Assert.IsTrue(_state.IsBossLevel);
            _state.RecordCoreKill();
            Assert.AreEqual(0, _state.CoreKillsThisBlock);
        }

        [Test]
        public void RecordCoreKill_FiresOnCoreKillRecorded()
        {
            int firedCount = 0;
            _state.OnCoreKillRecorded += () => firedCount++;
            _state.RecordCoreKill();
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void RecordCoreKill_OnAdvance_FiresOnLevelChanged()
        {
            int firedCount = 0;
            _state.OnLevelChanged += () => firedCount++;
            int threshold = _state.Threshold;
            for (int i = 0; i < threshold; i++)
                _state.RecordCoreKill();
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void BossFailed_ResetsCoreKillCounter()
        {
            _state.RecordCoreKill();
            _state.RecordCoreKill();
            Assert.AreEqual(2, _state.CoreKillsThisBlock);
            SetLevel(10);
            _state.BossFailed();
            Assert.AreEqual(0, _state.CoreKillsThisBlock);
        }

        [Test]
        public void BossDefeated_ResetsCoreKillCounter()
        {
            SetLevel(10);
            // Can't RecordCoreKill on boss level, so set counter via reflection
            var field = typeof(LevelState).GetField("coreKillsThisBlock",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_state, 5);
            _state.BossDefeated();
            Assert.AreEqual(0, _state.CoreKillsThisBlock);
        }

        // --- Boss/block tests ---

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

        // --- Difficulty multiplier tests ---

        [Test]
        public void SpawnInterval_Level1_IsCalm()
        {
            Assert.Greater(_state.SpawnInitialInterval, 10f);
            Assert.Greater(_state.SpawnMinInterval, 5f);
        }

        [Test]
        public void SpawnInterval_HighLevel_IsFaster()
        {
            float level1Min = _state.SpawnMinInterval;
            SetLevel(100);
            Assert.Less(_state.SpawnMinInterval, level1Min);
        }

        [Test]
        public void MeteorSizeRange_Level1_IsSmall()
        {
            var (min, max) = _state.MeteorSizeRange;
            Assert.LessOrEqual(max, 0.7f);
        }

        [Test]
        public void MeteorSizeRange_HighLevel_IsLarger()
        {
            SetLevel(80);
            var (min, max) = _state.MeteorSizeRange;
            Assert.Greater(max, 0.9f);
        }

        [Test]
        public void HpMultiplier_Level1_Is1()
        {
            Assert.AreEqual(1f, _state.HpMultiplier, 0.01f);
        }

        [Test]
        public void HpMultiplier_ScalesWithLevel()
        {
            SetLevel(50);
            Assert.Greater(_state.HpMultiplier, 1f);
        }

        [Test]
        public void CoreValueMultiplier_ScalesWithLevel()
        {
            SetLevel(50);
            Assert.Greater(_state.CoreValueMultiplier, 1f);
        }

        [Test]
        public void CoreCountBonus_Level1_Is0()
        {
            Assert.AreEqual(0, _state.CoreCountBonus);
        }

        [Test]
        public void CoreCountBonus_Level26_Is1()
        {
            SetLevel(26);
            Assert.AreEqual(1, _state.CoreCountBonus);
        }

        [Test]
        public void CoreCountBonus_Level100_IsPositive()
        {
            SetLevel(100);
            Assert.Greater(_state.CoreCountBonus, 0);
        }

        private void SetLevel(int level)
        {
            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_state, level);
        }
    }
}
