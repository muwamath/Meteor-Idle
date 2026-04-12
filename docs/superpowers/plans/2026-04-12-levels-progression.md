# Levels, Progression, and Boss — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 150-level progression system with automatic core-threshold advancement, boss fights every 10 levels, and a scrolling level progress strip UI.

**Architecture:** New `LevelState` singleton drives difficulty scaling through `MeteorSpawner` and `Meteor`. Level advancement is triggered from `GameManager.AddMoney` when money >= threshold. Boss spawning replaces normal spawning at every 10th level. A 5-cell scrolling UI strip at the top of the screen shows progression. All scaling values are serialized fields for later tuning (Iter 5).

**Tech Stack:** Unity 6, C#, Unity MCP for editor automation, TextMeshPro for UI.

**Spec:** [docs/superpowers/specs/2026-04-12-levels-progression-design.md](../specs/2026-04-12-levels-progression-design.md)

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `Assets/Scripts/LevelState.cs` | Singleton: current level, threshold calculation, advancement, boss state, difficulty multipliers |
| `Assets/Scripts/UI/LevelStripUI.cs` | 5-cell scrolling level strip, progress overlay, cell animations |
| `Assets/Scripts/UI/LevelCell.cs` | Individual cell in the strip: state rendering (beaten/current/upcoming/boss) |
| `Assets/Tests/EditMode/LevelStateTests.cs` | Threshold formula, advancement, boss gating, block reset |
| `Assets/Tests/PlayMode/LevelProgressionTests.cs` | End-to-end: advancement triggers, boss spawn/fail/success |

### Modified Files
| File | Change |
|------|--------|
| `Assets/Scripts/GameManager.cs` | After `AddMoney`, check `LevelState` threshold and trigger advancement |
| `Assets/Scripts/MeteorSpawner.cs` | Read `LevelState` for spawn rate, size range, HP multiplier; pause during boss; spawn boss meteor |
| `Assets/Scripts/Meteor.cs` | Accept HP/value multipliers from level; boss meteor flag + slow fall speed; signal boss death/escape |
| `Assets/Scripts/VoxelMeteorGenerator.cs` | Accept core count override and HP multiplier; boss color palette |
| `Assets/Scripts/UI/MoneyDisplay.cs` | Reposition below level strip (scene change, minimal code) |
| `Assets/Art/` | New procedural boss meteor palette PNG |

---

## Task 1: LevelState Singleton — Core Logic

**Files:**
- Create: `Assets/Scripts/LevelState.cs`
- Test: `Assets/Tests/EditMode/LevelStateTests.cs`

- [ ] **Step 1: Write failing tests for threshold formula and level properties**

```csharp
// Assets/Tests/EditMode/LevelStateTests.cs
using NUnit.Framework;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    public class LevelStateTests
    {
        private LevelState state;
        private GameObject go;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("LevelState", typeof(LevelState));
            state = go.GetComponent<LevelState>();
            TestHelpers.InvokeAwake(state);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
        }

        [Test]
        public void StartsAtLevel1()
        {
            Assert.AreEqual(1, state.CurrentLevel);
        }

        [Test]
        public void CurrentBlock_Level1Through10_IsBlock0()
        {
            Assert.AreEqual(0, state.CurrentBlock);
        }

        [Test]
        public void LevelInBlock_Level1_Is1()
        {
            Assert.AreEqual(1, state.LevelInBlock);
        }

        [Test]
        public void IsBossLevel_Level1_IsFalse()
        {
            Assert.IsFalse(state.IsBossLevel);
        }

        [Test]
        public void Threshold_Level1_IsBaseCost()
        {
            // baseCost=10, growthRate=1.08, level 1: 10 * 1.08^0 = 10
            Assert.AreEqual(10, state.Threshold);
        }

        [Test]
        public void Threshold_ScalesExponentially()
        {
            // Advance to level 5 via reflection to check scaling
            SetLevel(state, 5);
            // 10 * 1.08^4 ≈ 13.6 → rounds to int
            int expected = Mathf.RoundToInt(10f * Mathf.Pow(1.08f, 4));
            Assert.AreEqual(expected, state.Threshold);
        }

        [Test]
        public void TryAdvance_BelowThreshold_ReturnsFalse()
        {
            Assert.IsFalse(state.TryAdvance(5));
            Assert.AreEqual(1, state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AtThreshold_AdvancesAndReturnsTrue()
        {
            // Threshold at level 1 is 10
            Assert.IsTrue(state.TryAdvance(10));
            Assert.AreEqual(2, state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AboveThreshold_AdvancesAndReturnsTrue()
        {
            Assert.IsTrue(state.TryAdvance(50));
            Assert.AreEqual(2, state.CurrentLevel);
        }

        [Test]
        public void TryAdvance_AtBossLevel_ReturnsFalse()
        {
            SetLevel(state, 10);
            Assert.IsTrue(state.IsBossLevel);
            Assert.IsFalse(state.TryAdvance(999999));
        }

        [Test]
        public void TryAdvance_FiresOnLevelChanged()
        {
            int firedCount = 0;
            state.OnLevelChanged += () => firedCount++;
            state.TryAdvance(10);
            Assert.AreEqual(1, firedCount);
        }

        [Test]
        public void BossDefeated_AdvancesToNextBlock()
        {
            SetLevel(state, 10);
            state.BossDefeated();
            Assert.AreEqual(11, state.CurrentLevel);
            Assert.AreEqual(1, state.CurrentBlock);
        }

        [Test]
        public void BossFailed_ResetsToBlockStart()
        {
            SetLevel(state, 20);
            state.BossFailed();
            Assert.AreEqual(11, state.CurrentLevel);
        }

        [Test]
        public void BossFailed_Block1_ResetsToLevel1()
        {
            SetLevel(state, 10);
            state.BossFailed();
            Assert.AreEqual(1, state.CurrentLevel);
        }

        [Test]
        public void BossFailed_FiresOnBossFailed()
        {
            SetLevel(state, 10);
            int firedCount = 0;
            state.OnBossFailed += () => firedCount++;
            state.BossFailed();
            Assert.AreEqual(1, firedCount);
        }

        // Helper to set level via reflection for testing
        private static void SetLevel(LevelState s, int level)
        {
            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(s, level);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Expected: Compilation errors — `LevelState` doesn't exist yet.

- [ ] **Step 3: Write LevelState implementation**

```csharp
// Assets/Scripts/LevelState.cs
using System;
using UnityEngine;

namespace MeteorIdle
{
    public class LevelState : MonoBehaviour
    {
        public static LevelState Instance { get; private set; }

        [Header("Threshold Curve")]
        [SerializeField] private float baseCost = 10f;
        [SerializeField] private float growthRate = 1.08f;

        [NonSerialized] private int currentLevel = 1;

        public int CurrentLevel => currentLevel;
        public int CurrentBlock => (currentLevel - 1) / 10;
        public int LevelInBlock => (currentLevel - 1) % 10 + 1;
        public bool IsBossLevel => LevelInBlock == 10;

        public int Threshold => IsBossLevel ? 0 : Mathf.RoundToInt(baseCost * Mathf.Pow(growthRate, currentLevel - 1));

        public event Action OnLevelChanged;
        public event Action OnBossSpawned;
        public event Action OnBossFailed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Called by GameManager after adding money. Returns true and the threshold cost
        /// if the level advances, false if not (below threshold or boss level).
        /// </summary>
        public bool TryAdvance(int currentMoney)
        {
            if (IsBossLevel) return false;
            int threshold = Threshold;
            if (currentMoney < threshold) return false;

            currentLevel++;
            OnLevelChanged?.Invoke();
            return true;
        }

        public void BossDefeated()
        {
            currentLevel++;
            OnLevelChanged?.Invoke();
        }

        public void BossFailed()
        {
            int blockStart = CurrentBlock * 10 + 1;
            currentLevel = blockStart;
            OnBossFailed?.Invoke();
            OnLevelChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Expected: All LevelStateTests pass. All existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/LevelState.cs Assets/Tests/EditMode/LevelStateTests.cs
git commit -m "Add LevelState singleton with threshold formula, boss gating, block reset"
```

---

## Task 2: Difficulty Multipliers on LevelState

**Files:**
- Modify: `Assets/Scripts/LevelState.cs`
- Test: `Assets/Tests/EditMode/LevelStateTests.cs`

- [ ] **Step 1: Write failing tests for difficulty multipliers**

Add to `LevelStateTests.cs`:

```csharp
[Test]
public void SpawnInterval_Level1_IsCalm()
{
    // Level 1 should have wide spawn intervals
    Assert.Greater(state.SpawnInitialInterval, 10f);
    Assert.Greater(state.SpawnMinInterval, 5f);
}

[Test]
public void SpawnInterval_HighLevel_IsFaster()
{
    SetLevel(state, 100);
    Assert.Less(state.SpawnMinInterval, state.SpawnInitialInterval);
    // Should be noticeably faster than level 1
    var lvl1State = SetupFreshState();
    Assert.Less(state.SpawnMinInterval, lvl1State.SpawnMinInterval);
    Object.DestroyImmediate(lvl1State.gameObject);
}

[Test]
public void MeteorSizeRange_Level1_IsSmall()
{
    var (min, max) = state.MeteorSizeRange;
    Assert.LessOrEqual(max, 0.7f); // small meteors at level 1
}

[Test]
public void MeteorSizeRange_HighLevel_IsLarger()
{
    SetLevel(state, 80);
    var (min, max) = state.MeteorSizeRange;
    Assert.Greater(max, 0.9f);
}

[Test]
public void HpMultiplier_Level1_Is1()
{
    Assert.AreEqual(1f, state.HpMultiplier, 0.01f);
}

[Test]
public void HpMultiplier_ScalesWithLevel()
{
    SetLevel(state, 50);
    Assert.Greater(state.HpMultiplier, 1f);
}

[Test]
public void CoreValueMultiplier_ScalesWithLevel()
{
    SetLevel(state, 50);
    Assert.Greater(state.CoreValueMultiplier, 1f);
}

[Test]
public void CoreCountBonus_Level1_Is0()
{
    Assert.AreEqual(0, state.CoreCountBonus);
}

[Test]
public void CoreCountBonus_HighLevel_IsPositive()
{
    SetLevel(state, 100);
    Assert.Greater(state.CoreCountBonus, 0);
}

private LevelState SetupFreshState()
{
    var freshGo = new GameObject("FreshLevelState", typeof(LevelState));
    var fresh = freshGo.GetComponent<LevelState>();
    TestHelpers.InvokeAwake(fresh);
    return fresh;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Expected: FAIL — properties don't exist yet.

- [ ] **Step 3: Add difficulty multiplier properties to LevelState**

Add to `LevelState.cs`:

```csharp
[Header("Spawn Rate Scaling")]
[SerializeField] private float level1InitialInterval = 15f;
[SerializeField] private float level1MinInterval = 8f;
[SerializeField] private float level150InitialInterval = 5f;
[SerializeField] private float level150MinInterval = 2f;

[Header("Meteor Size Scaling")]
[SerializeField] private float level1SizeMin = 0.35f;
[SerializeField] private float level1SizeMax = 0.6f;
[SerializeField] private float level150SizeMin = 0.8f;
[SerializeField] private float level150SizeMax = 1.2f;

[Header("HP & Value Scaling")]
[SerializeField] private float hpScalePerLevel = 0.02f;   // +2% per level
[SerializeField] private float valueScalePerLevel = 0.015f; // +1.5% per level (shallower than threshold)

[Header("Core Count Scaling")]
[SerializeField] private int coreCountBonusEveryNLevels = 25; // +1 core per meteor every 25 levels

private float LevelT => Mathf.Clamp01((currentLevel - 1f) / 149f); // 0 at level 1, 1 at level 150

public float SpawnInitialInterval => Mathf.Lerp(level1InitialInterval, level150InitialInterval, LevelT);
public float SpawnMinInterval => Mathf.Lerp(level1MinInterval, level150MinInterval, LevelT);

public (float min, float max) MeteorSizeRange =>
    (Mathf.Lerp(level1SizeMin, level150SizeMin, LevelT),
     Mathf.Lerp(level1SizeMax, level150SizeMax, LevelT));

public float HpMultiplier => 1f + hpScalePerLevel * (currentLevel - 1);
public float CoreValueMultiplier => 1f + valueScalePerLevel * (currentLevel - 1);
public int CoreCountBonus => coreCountBonusEveryNLevels > 0
    ? (currentLevel - 1) / coreCountBonusEveryNLevels
    : 0;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/LevelState.cs Assets/Tests/EditMode/LevelStateTests.cs
git commit -m "Add difficulty multiplier properties to LevelState (spawn rate, size, HP, value, cores)"
```

---

## Task 3: Wire LevelState into GameManager for Auto-Advancement

**Files:**
- Modify: `Assets/Scripts/GameManager.cs`
- Test: `Assets/Tests/EditMode/GameManagerTests.cs`

- [ ] **Step 1: Write failing tests for auto-advancement**

Add to `GameManagerTests.cs`:

```csharp
[Test]
public void AddMoney_WhenThresholdMet_DeductsCostAndAdvancesLevel()
{
    // Set up LevelState in the test scene
    var lsGo = new GameObject("LevelState", typeof(LevelState));
    var ls = lsGo.GetComponent<LevelState>();
    TestHelpers.InvokeAwake(ls);

    // LevelState.Threshold at level 1 = 10
    gm.SetMoney(0);
    gm.AddMoney(10);

    Assert.AreEqual(2, ls.CurrentLevel);
    Assert.AreEqual(0, gm.Money); // 10 - 10 threshold = 0

    Object.DestroyImmediate(lsGo);
}

[Test]
public void AddMoney_BelowThreshold_DoesNotAdvance()
{
    var lsGo = new GameObject("LevelState", typeof(LevelState));
    var ls = lsGo.GetComponent<LevelState>();
    TestHelpers.InvokeAwake(ls);

    gm.SetMoney(0);
    gm.AddMoney(5);

    Assert.AreEqual(1, ls.CurrentLevel);
    Assert.AreEqual(5, gm.Money);

    Object.DestroyImmediate(lsGo);
}

[Test]
public void AddMoney_ExceedsThreshold_KeepsRemainder()
{
    var lsGo = new GameObject("LevelState", typeof(LevelState));
    var ls = lsGo.GetComponent<LevelState>();
    TestHelpers.InvokeAwake(ls);

    gm.SetMoney(0);
    gm.AddMoney(15);

    Assert.AreEqual(2, ls.CurrentLevel);
    Assert.AreEqual(5, gm.Money); // 15 - 10 = 5

    Object.DestroyImmediate(lsGo);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: FAIL — GameManager.AddMoney doesn't check LevelState yet.

- [ ] **Step 3: Modify GameManager.AddMoney to check LevelState**

In `GameManager.AddMoney`, after adding to money and firing `OnMoneyChanged`, add:

```csharp
// After: OnMoneyChanged?.Invoke(money);
if (LevelState.Instance != null)
{
    int threshold = LevelState.Instance.Threshold;
    if (threshold > 0 && LevelState.Instance.TryAdvance(money))
    {
        money -= threshold;
        OnMoneyChanged?.Invoke(money);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Expected: All pass including existing GameManager tests (they have no LevelState in scene, so the null check skips advancement).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GameManager.cs Assets/Tests/EditMode/GameManagerTests.cs
git commit -m "Wire auto-advancement: GameManager.AddMoney deducts threshold and advances level"
```

---

## Task 4: Wire LevelState into MeteorSpawner

**Files:**
- Modify: `Assets/Scripts/MeteorSpawner.cs`

- [ ] **Step 1: Modify MeteorSpawner to read LevelState for spawn rate and size**

In `CurrentInterval()`, replace the fixed lerp with LevelState values when available:

```csharp
public float CurrentInterval()
{
    float initial, min;
    if (LevelState.Instance != null)
    {
        initial = LevelState.Instance.SpawnInitialInterval;
        min = LevelState.Instance.SpawnMinInterval;
    }
    else
    {
        initial = initialInterval;
        min = minInterval;
    }
    float t = Mathf.Clamp01(elapsed / rampDurationSeconds);
    return Mathf.Lerp(initial, min, t);
}
```

In `SpawnOne()`, replace the fixed size range with LevelState values:

```csharp
private void SpawnOne()
{
    float sizeMin = 0.525f;
    float sizeMax = 1.2f;
    if (LevelState.Instance != null)
    {
        (sizeMin, sizeMax) = LevelState.Instance.MeteorSizeRange;
    }
    float size = Random.Range(sizeMin, sizeMax);
    // ... rest of spawn logic unchanged
}
```

- [ ] **Step 2: Add boss spawning pause and trigger**

Add fields and methods to `MeteorSpawner`:

```csharp
[SerializeField] private float bossFallSpeed = 0.3f;
[SerializeField] private float bossSize = 1.2f;

private bool bossActive;
private Meteor activeBoss;

private void OnEnable()
{
    if (LevelState.Instance != null)
    {
        LevelState.Instance.OnLevelChanged += OnLevelChanged;
    }
}

private void OnDisable()
{
    if (LevelState.Instance != null)
    {
        LevelState.Instance.OnLevelChanged -= OnLevelChanged;
    }
}

private void OnLevelChanged()
{
    if (LevelState.Instance.IsBossLevel)
    {
        SpawnBoss();
    }
    else
    {
        bossActive = false;
        elapsed = 0f; // reset spawn ramp for new level
    }
}

private void SpawnBoss()
{
    bossActive = true;
    activeBoss = pool.Get();
    float x = 0f; // center of screen
    activeBoss.Spawn(this, new Vector3(x, spawnY, 0f), Random.Range(0, int.MaxValue), bossSize);
    activeBoss.SetBossMode(bossFallSpeed);
}
```

In `Update()`, gate normal spawning on `!bossActive`:

```csharp
private void Update()
{
    if (bossActive) return; // no normal spawns during boss
    // ... existing spawn timer logic
}
```

- [ ] **Step 3: Run existing tests to check for regressions**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Run: `mcp__UnityMCP__run_tests mode=PlayMode assembly_names=MeteorIdle.Tests.PlayMode`
Expected: All existing tests pass. MeteorSpawnerIntervalTests still work because LevelState.Instance is null in those tests, falling back to serialized values.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/MeteorSpawner.cs
git commit -m "Wire MeteorSpawner to LevelState: level-scaled spawn rate, size, boss spawning"
```

---

## Task 5: Meteor Boss Mode + Level Scaling

**Files:**
- Modify: `Assets/Scripts/Meteor.cs`
- Modify: `Assets/Scripts/VoxelMeteorGenerator.cs`

- [ ] **Step 1: Add boss mode to Meteor**

Add to `Meteor.cs`:

```csharp
private bool isBoss;
private float bossSpeed;

public bool IsBoss => isBoss;

public void SetBossMode(float fallSpeed)
{
    isBoss = true;
    bossSpeed = fallSpeed;
}
```

In `Meteor.Update()`, use `bossSpeed` for boss fall velocity instead of the normal fall speed. When a boss falls offscreen (below `despawnY`), call `LevelState.Instance.BossFailed()` instead of normal despawn:

```csharp
// In the existing offscreen/despawn check:
if (isBoss && transform.position.y < despawnY)
{
    LevelState.Instance?.BossFailed();
    spawner.Release(this);
    return;
}
```

On boss death (aliveCount reaches 0), call `LevelState.Instance.BossDefeated()`:

```csharp
// In the existing death/fade logic where aliveCount hits 0:
if (isBoss)
{
    LevelState.Instance?.BossDefeated();
}
```

Reset `isBoss = false` in `Spawn()` so pooled meteors don't carry boss state.

- [ ] **Step 2: Apply HP and core value multipliers from LevelState**

In `Meteor.Spawn()`, after generating the voxel grid, apply HP multiplier:

```csharp
if (LevelState.Instance != null)
{
    float hpMult = LevelState.Instance.HpMultiplier;
    if (hpMult > 1f)
    {
        for (int x = 0; x < GridSize; x++)
            for (int y = 0; y < GridSize; y++)
                if (hp[x, y] > 0)
                    hp[x, y] = Mathf.CeilToInt(hp[x, y] * hpMult);
    }
}
```

Replace the constant `CoreBaseValue` with a property that reads level scaling:

```csharp
public int CoreValue => LevelState.Instance != null
    ? Mathf.RoundToInt(CoreBaseValue * LevelState.Instance.CoreValueMultiplier)
    : CoreBaseValue;
```

Update all references from `CoreBaseValue` to `CoreValue` in the payout path.

- [ ] **Step 3: Add core count bonus to VoxelMeteorGenerator**

In `VoxelMeteorGenerator.Generate()`, after calculating `coreCount` from `sizeT`, add the bonus:

```csharp
if (LevelState.Instance != null)
    coreCount += LevelState.Instance.CoreCountBonus;
```

- [ ] **Step 4: Run all tests**

Run: `mcp__UnityMCP__run_tests mode=EditMode assembly_names=MeteorIdle.Tests.Editor`
Run: `mcp__UnityMCP__run_tests mode=PlayMode assembly_names=MeteorIdle.Tests.PlayMode`
Expected: All pass. Existing tests have no LevelState.Instance, so multipliers default to 1x / 0 bonus.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Meteor.cs Assets/Scripts/VoxelMeteorGenerator.cs
git commit -m "Add boss mode to Meteor + level-scaled HP, core value, core count"
```

---

## Task 6: Boss Visual Treatment — Procedural Art

**Files:**
- Modify: `Assets/Scripts/VoxelMeteorGenerator.cs`
- Art generated via `execute_code`

- [ ] **Step 1: Add boss color palette to VoxelMeteorGenerator**

Add a boss flag to the Generate method or a post-generation recolor. The boss palette: dark grey base, crimson/red vein accents for cores, darker stone. Still uses the same voxel grid — just different colors.

```csharp
// Add to VoxelMeteorGenerator, called after normal texture generation when isBoss:
public static void ApplyBossPalette(Texture2D texture, VoxelKind[,] kind, VoxelMaterial[,] material, int gridSize, int blockSize)
{
    Color darkBase = new Color(0.2f, 0.15f, 0.15f);
    Color darkEdge = new Color(0.1f, 0.08f, 0.08f);
    Color coreTop = new Color(0.85f, 0.1f, 0.1f);
    Color coreBottom = new Color(0.55f, 0.05f, 0.05f);
    Color coreEdge = new Color(0.35f, 0.02f, 0.02f);

    for (int gx = 0; gx < gridSize; gx++)
    {
        for (int gy = 0; gy < gridSize; gy++)
        {
            if (kind[gx, gy] == VoxelKind.Empty) continue;

            bool isCore = material[gx, gy] != null && material[gx, gy].displayName == "Core";
            Color top = isCore ? coreTop : darkBase;
            Color bottom = isCore ? coreBottom : darkBase;
            Color edge = isCore ? coreEdge : darkEdge;

            int px = gx * blockSize;
            int py = gy * blockSize;
            for (int dx = 0; dx < blockSize; dx++)
            {
                for (int dy = 0; dy < blockSize; dy++)
                {
                    bool isEdge = dx == 0 || dy == 0 || dx == blockSize - 1 || dy == blockSize - 1;
                    float t = (float)dy / (blockSize - 1);
                    Color c = isEdge ? edge : Color.Lerp(bottom, top, t);
                    texture.SetPixel(px + dx, py + dy, c);
                }
            }
        }
    }
    texture.Apply();
}
```

- [ ] **Step 2: Call ApplyBossPalette from Meteor.Spawn when boss**

In `Meteor.Spawn()`, after texture generation:

```csharp
if (isBoss)
{
    VoxelMeteorGenerator.ApplyBossPalette(texture, kind, material, GridSize, BlockSize);
}
```

- [ ] **Step 3: Play-test in editor to verify boss looks distinct**

Run: `manage_editor play`, spawn a boss via debug overlay or `execute_code`, take screenshot, verify dark/crimson palette. `manage_editor stop`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/VoxelMeteorGenerator.cs Assets/Scripts/Meteor.cs
git commit -m "Add boss meteor visual treatment: dark/crimson voxel palette"
```

---

## Task 7: Boss Failure — Clear In-Flight Meteors + Reset

**Files:**
- Modify: `Assets/Scripts/MeteorSpawner.cs`
- Modify: `Assets/Scripts/LevelState.cs`

- [ ] **Step 1: Add ClearAllMeteors to MeteorSpawner**

```csharp
public void ClearAllMeteors()
{
    // Return all active meteors to pool
    for (int i = pool.active.Count - 1; i >= 0; i--)
    {
        var m = pool.active[i];
        if (m != null && m.gameObject.activeSelf)
        {
            pool.Release(m);
        }
    }
    bossActive = false;
    activeBoss = null;
}
```

- [ ] **Step 2: Subscribe MeteorSpawner to LevelState.OnBossFailed**

In `MeteorSpawner.OnEnable`:

```csharp
LevelState.Instance.OnBossFailed += ClearAllMeteors;
```

In `MeteorSpawner.OnDisable`:

```csharp
LevelState.Instance.OnBossFailed -= ClearAllMeteors;
```

This means: boss falls offscreen → `LevelState.BossFailed()` fires event → `MeteorSpawner.ClearAllMeteors()` removes everything → `OnLevelChanged` resets level → normal spawning resumes.

- [ ] **Step 3: Also clear active CoreDrops on boss failure**

In `GameManager`, subscribe to `OnBossFailed` and return all active drops to pool:

```csharp
private void OnEnable()
{
    if (LevelState.Instance != null)
        LevelState.Instance.OnBossFailed += ClearActiveDrops;
}

private void OnDisable()
{
    if (LevelState.Instance != null)
        LevelState.Instance.OnBossFailed -= ClearActiveDrops;
}

private void ClearActiveDrops()
{
    for (int i = activeDrops.Count - 1; i >= 0; i--)
    {
        var drop = activeDrops[i];
        if (drop != null)
        {
            coreDropPool.Release(drop);
        }
    }
    activeDrops.Clear();
}
```

- [ ] **Step 4: Run all tests**

Run both test suites. Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/MeteorSpawner.cs Assets/Scripts/GameManager.cs
git commit -m "Boss failure: clear in-flight meteors and CoreDrops, reset to block start"
```

---

## Task 8: Level Progress Strip UI — Cell Component

**Files:**
- Create: `Assets/Scripts/UI/LevelCell.cs`

- [ ] **Step 1: Create LevelCell MonoBehaviour**

```csharp
// Assets/Scripts/UI/LevelCell.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MeteorIdle
{
    public class LevelCell : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private Image progressOverlay;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image bossIcon;
        [SerializeField] private RectTransform targetIndicator; // rotating target for active cell

        [Header("Colors")]
        [SerializeField] private Color beatenColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color currentColor = new Color(0.2f, 0.6f, 0.2f, 1f);
        [SerializeField] private Color upcomingColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color bossUpcomingColor = new Color(0.7f, 0.2f, 0.2f, 1f);

        private float targetRotationSpeed = 30f; // degrees per second

        public enum CellState { Beaten, Current, Upcoming, BossUpcoming, BossCurrent }

        private CellState state;

        public void Configure(int level, CellState cellState)
        {
            state = cellState;
            bool isBossLevel = level % 10 == 0;

            levelLabel.text = $"Lv {level}";
            bossIcon.gameObject.SetActive(isBossLevel && cellState != CellState.Beaten);
            targetIndicator.gameObject.SetActive(cellState == CellState.Current || cellState == CellState.BossCurrent);
            progressOverlay.gameObject.SetActive(cellState == CellState.Current);
            progressLabel.gameObject.SetActive(cellState == CellState.Current);

            switch (cellState)
            {
                case CellState.Beaten:
                    backgroundImage.color = beatenColor;
                    break;
                case CellState.Current:
                    backgroundImage.color = currentColor;
                    break;
                case CellState.BossCurrent:
                    backgroundImage.color = bossUpcomingColor;
                    progressLabel.text = "DEFEAT THE BOSS";
                    progressLabel.gameObject.SetActive(true);
                    progressOverlay.gameObject.SetActive(false);
                    break;
                case CellState.BossUpcoming:
                    backgroundImage.color = bossUpcomingColor;
                    break;
                case CellState.Upcoming:
                    backgroundImage.color = upcomingColor;
                    break;
            }
        }

        public void UpdateProgress(float fillAmount, int current, int threshold)
        {
            if (progressOverlay.gameObject.activeSelf)
            {
                progressOverlay.fillAmount = fillAmount;
            }
            if (progressLabel.gameObject.activeSelf && state == CellState.Current)
            {
                progressLabel.text = $"{current} / {threshold}";
            }
        }

        private void Update()
        {
            if (targetIndicator.gameObject.activeSelf)
            {
                targetIndicator.Rotate(0f, 0f, -targetRotationSpeed * Time.deltaTime);
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/UI/LevelCell.cs
git commit -m "Add LevelCell component: beaten/current/upcoming/boss states, progress overlay, rotating target"
```

---

## Task 9: Level Progress Strip UI — Strip Controller

**Files:**
- Create: `Assets/Scripts/UI/LevelStripUI.cs`

- [ ] **Step 1: Create LevelStripUI MonoBehaviour**

```csharp
// Assets/Scripts/UI/LevelStripUI.cs
using UnityEngine;

namespace MeteorIdle
{
    public class LevelStripUI : MonoBehaviour
    {
        [SerializeField] private LevelCell[] cells; // exactly 5, assigned in scene
        [SerializeField] private RectTransform stripRoot;

        private int displayedCenterLevel;

        private void Start()
        {
            if (LevelState.Instance != null)
            {
                LevelState.Instance.OnLevelChanged += RefreshStrip;
                LevelState.Instance.OnBossFailed += RefreshStrip;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnMoneyChanged += OnMoneyChanged;
            }
            RefreshStrip();
        }

        private void OnDestroy()
        {
            if (LevelState.Instance != null)
            {
                LevelState.Instance.OnLevelChanged -= RefreshStrip;
                LevelState.Instance.OnBossFailed -= RefreshStrip;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnMoneyChanged -= OnMoneyChanged;
            }
        }

        private void OnMoneyChanged(int money)
        {
            UpdateProgressOnCurrentCell();
        }

        public void RefreshStrip()
        {
            if (LevelState.Instance == null) return;

            int current = LevelState.Instance.CurrentLevel;
            displayedCenterLevel = current;

            // cells[0] and cells[1] are left neighbors, cells[2] is center, cells[3] and cells[4] are right
            for (int i = 0; i < 5; i++)
            {
                int level = current + (i - 2); // -2, -1, 0, +1, +2

                if (level < 1)
                {
                    cells[i].gameObject.SetActive(false);
                    continue;
                }

                cells[i].gameObject.SetActive(true);
                bool isBoss = level % 10 == 0;

                LevelCell.CellState state;
                if (level < current)
                {
                    state = LevelCell.CellState.Beaten;
                }
                else if (level == current)
                {
                    state = isBoss ? LevelCell.CellState.BossCurrent : LevelCell.CellState.Current;
                }
                else
                {
                    state = isBoss ? LevelCell.CellState.BossUpcoming : LevelCell.CellState.Upcoming;
                }

                cells[i].Configure(level, state);
            }

            UpdateProgressOnCurrentCell();
        }

        private void UpdateProgressOnCurrentCell()
        {
            if (LevelState.Instance == null) return;
            if (LevelState.Instance.IsBossLevel) return;

            int threshold = LevelState.Instance.Threshold;
            int money = GameManager.Instance != null ? GameManager.Instance.Money : 0;
            float fill = threshold > 0 ? Mathf.Clamp01((float)money / threshold) : 0f;

            // Center cell is always index 2
            cells[2].UpdateProgress(fill, money, threshold);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/UI/LevelStripUI.cs
git commit -m "Add LevelStripUI controller: 5-cell strip, progress updates, level-change refresh"
```

---

## Task 10: Build Level Strip in Scene

**Files:**
- Scene: `Assets/Scenes/Game.unity` (via MCP)
- Art: procedural target indicator PNG

- [ ] **Step 1: Generate procedural art for the target indicator and boss warning icon**

Use `mcp__UnityMCP__execute_code` to generate:
1. A rotating target/crosshair sprite (small, ~32x32, voxel-style crosshair with 1px lines)
2. A boss warning icon (small, ~24x24, voxel skull or hazard triangle)

Both via `Texture2D.SetPixel` + `EncodeToPNG`, saved to `Assets/Art/`.

- [ ] **Step 2: Create the LevelCell prefab**

Use MCP to build the prefab:
- Root: `RectTransform` with `Image` (background)
- Child: `TMP_Text` for level label (centered, top half)
- Child: `Image` for progress overlay (type=Filled, fillMethod=Horizontal, green at ~40% alpha)
- Child: `TMP_Text` for progress label (bottom of cell, small font)
- Child: `Image` for boss icon (centered, warning sprite)
- Child: `RectTransform` for target indicator (centered, crosshair sprite)
- Attach `LevelCell.cs` component, wire SerializeField refs

- [ ] **Step 3: Build the strip in the scene**

Use MCP to create in `Game.unity`:
- `LevelStrip` GameObject under the existing Canvas
- Anchored to top-center, full width
- Contains 5 `LevelCell` instances: cells 0,1 (left, normal width), cell 2 (center, 3x width), cells 3,4 (right, normal width)
- Attach `LevelStripUI.cs`, wire the 5 cell references
- Semi-transparent background so meteors pass through (low alpha or no background image)

- [ ] **Step 4: Reposition MoneyDisplay below the strip**

Move the MoneyDisplay anchor to sit directly below the level strip. This is a RectTransform change via MCP, no code change.

- [ ] **Step 5: Add LevelState to the scene**

Use MCP to add a `LevelState` component to an appropriate GameObject (GameManager or a new `LevelManager` GO). Wire default serialized values.

- [ ] **Step 6: Play-test in editor**

`manage_editor play` → verify strip shows, progress fills, level label correct. Use debug overlay to set money above threshold and confirm advancement + strip scroll. `manage_editor stop`.

- [ ] **Step 7: Commit**

```bash
git add Assets/
git commit -m "Build level strip UI in scene: 5-cell strip, LevelCell prefab, repositioned MoneyDisplay"
```

---

## Task 11: Base Stat Rebalance

**Files:**
- Modify: ScriptableObject assets via MCP `execute_code`
- Modify: `Assets/Scripts/Meteor.cs` (CoreBaseValue)

- [ ] **Step 1: Rebalance weapon base stats via execute_code**

Use `mcp__UnityMCP__execute_code` to load and modify the ScriptableObject assets:

**TurretStats (Missile):**
- FireRate: base 0.3 (was 0.5)
- MissileSpeed: base 2 (was 4)
- Damage: base 1 (unchanged)
- BlastRadius: base 0.05 (was 0.10)
- RotationSpeed: base 20 (was 30)
- Homing: base 0 (unchanged)

**RailgunStats:**
- FireRate: base 0.1 (was 0.2)
- Speed: base 4 (was 6)
- Weight: base 2 (was 4)
- RotationSpeed: base 15 (was 20)
- Caliber: base 1 (unchanged)

**DroneStats:**
- Thrust: reduce by ~40% from current base
- BatteryCapacity: reduce by ~30% from current base
- CargoCapacity: 1 (unchanged)
- Braking: reduce proportionally

**BayStats:**
- ReloadSpeed: reduce by ~30% from current base
- DronesPerBay: 1 (unchanged)

- [ ] **Step 2: Reduce CoreBaseValue**

In `Meteor.cs`, change:
```csharp
public const int CoreBaseValue = 3; // was 5, lower base for longer progression
```

- [ ] **Step 3: Adjust upgrade cost curves**

Use `execute_code` to set baseCost and costGrowth on each stat asset so early upgrades are cheap ($2-5) and late upgrades are expensive. These are placeholder values for Iter 5 tuning.

- [ ] **Step 4: Play-test level 1 feel**

`manage_editor play` → verify missiles are slow, railgun charges forever, meteors are tiny pebbles. Should feel weak but functional. `manage_editor stop`.

- [ ] **Step 5: Commit**

```bash
git add Assets/
git commit -m "Rebalance base stats: weak weapons, small meteors, low core value for 150-level progression"
```

---

## Task 12: PlayMode Tests for Level Progression

**Files:**
- Create: `Assets/Tests/PlayMode/LevelProgressionTests.cs`

- [ ] **Step 1: Write PlayMode tests**

```csharp
// Assets/Tests/PlayMode/LevelProgressionTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    public class LevelProgressionTests : PlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator AddMoney_AboveThreshold_AdvancesLevel()
        {
            yield return SetupScene();

            var lsGo = new GameObject("LevelState", typeof(LevelState));
            var ls = lsGo.GetComponent<LevelState>();

            Assert.AreEqual(1, ls.CurrentLevel);

            // Threshold at level 1 = 10
            GameManager.Instance.SetMoney(0);
            GameManager.Instance.AddMoney(10);

            Assert.AreEqual(2, ls.CurrentLevel);
            Assert.AreEqual(0, GameManager.Instance.Money);

            Object.Destroy(lsGo);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator BossMeteor_FallsOffscreen_ResetsToBlockStart()
        {
            yield return SetupScene();

            var lsGo = new GameObject("LevelState", typeof(LevelState));
            var ls = lsGo.GetComponent<LevelState>();

            // Set to boss level
            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(ls, 10);
            Assert.IsTrue(ls.IsBossLevel);

            // Spawn a boss meteor below despawn threshold
            var meteor = SpawnTestMeteor(new Vector3(0f, -20f, 0f));
            meteor.SetBossMode(0.3f);

            yield return new WaitForSeconds(0.5f);

            // Boss should have triggered failure
            Assert.AreEqual(1, ls.CurrentLevel);

            Object.Destroy(lsGo);
            TeardownScene();
        }

        [UnityTest]
        public IEnumerator BossMeteor_Killed_AdvancesToNextBlock()
        {
            yield return SetupScene();

            var lsGo = new GameObject("LevelState", typeof(LevelState));
            var ls = lsGo.GetComponent<LevelState>();

            var field = typeof(LevelState).GetField("currentLevel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(ls, 10);

            // Spawn boss and destroy all its voxels
            var meteor = SpawnTestMeteor(new Vector3(0f, 3f, 0f));
            meteor.SetBossMode(0.3f);

            // Kill all voxels via ApplyBlast with huge radius
            meteor.ApplyBlast(meteor.transform.position, 10f);

            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(11, ls.CurrentLevel);

            Object.Destroy(lsGo);
            TeardownScene();
        }
    }
}
```

- [ ] **Step 2: Run PlayMode tests**

Run: `mcp__UnityMCP__run_tests mode=PlayMode assembly_names=MeteorIdle.Tests.PlayMode`
Expected: All pass including new and existing tests.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/PlayMode/LevelProgressionTests.cs
git commit -m "Add PlayMode tests: level advancement, boss failure reset, boss kill advancement"
```

---

## Task 13: Code Review

- [ ] **Step 1: Dispatch code-reviewer agent**

Run the `superpowers:requesting-code-review` skill. Review all changes since the branch started. Focus on:
- LevelState singleton correctness (race conditions with GameManager)
- Boss spawn/death/failure flow completeness
- Null safety on LevelState.Instance across all integration points
- Test coverage gaps
- UI strip edge cases

- [ ] **Step 2: Address review findings**

Fix any issues found. Run both test suites again.

- [ ] **Step 3: Commit fixes**

```bash
git add -A
git commit -m "Address code review findings"
```

---

## Task 14: Final Verification — WebGL Dev Build

- [ ] **Step 1: Run both test suites**

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```
Expected: All green.

- [ ] **Step 2: Build WebGL dev**

Use `mcp__UnityMCP__execute_code` to call `BuildScripts.BuildWebGLDev()`.

- [ ] **Step 3: Serve and test in browser**

Run `tools/serve-webgl-dev.sh` in background. Navigate to `localhost:8000` via chrome-devtools-mcp. Verify:
- Level strip visible at top, 5 cells, current level highlighted
- Money display below strip
- Earn cores → progress bar fills → level advances automatically
- Reach level 10 → boss spawns alone, slow fall, dark/crimson visual
- Kill boss → advance to 11
- Let boss escape → reset to level 1
- Meteors at level 1 are tiny pebbles
- Weapons feel slow at base stats

Close tab, kill server.

- [ ] **Step 4: Identity scrub**

```bash
python3 tools/identity-scrub.py
python3 tools/identity-scrub.py main..HEAD
```

- [ ] **Step 5: Update CLAUDE.md and README.md**

Add level system, boss, and UI strip to both docs. Update test counts.

- [ ] **Step 6: Commit docs**

```bash
git add CLAUDE.md README.md
git commit -m "Update CLAUDE.md and README.md for Iter 4: levels, boss, progress strip"
```

- [ ] **Step 7: Hand back to user for sign-off**
