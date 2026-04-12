---
name: meteor-idle-doc-sync
description: Use at the end of every Meteor Idle iteration to verify docs/superpowers/roadmap.md, CLAUDE.md, and README.md are up to date with what actually shipped. Produces a punch list of staleness and concrete proposed edits.
---

# Meteor Idle — Doc Sync Checklist

Run this at the end of every iteration, **before** merging an `iter/*` branch to `main` (or immediately after, if the iteration finished without a formal sync step). The goal is a punch list of staleness across the three living docs, plus concrete proposed edits the user can approve.

The three docs in scope:

1. `docs/superpowers/roadmap.md`
2. `CLAUDE.md`
3. `README.md`

Out of scope: `docs/reference/game-systems.md` (deeper reference, updated separately), `docs/superpowers/specs/*`, `docs/superpowers/plans/*` (frozen artifacts, never updated after an iteration ships).

---

## Step 1 — Gather ground truth

Before opening any doc, collect the facts you'll measure against. Do these in parallel where possible.

### 1a. What shipped

Find the last commit that touched the docs so you know the window to audit:

```
git log --oneline -1 --format="%h %s" -- CLAUDE.md README.md docs/superpowers/roadmap.md
```

Call that SHA `LAST_DOC_SYNC`. Then:

```
git log --oneline $LAST_DOC_SYNC..HEAD
git diff --stat $LAST_DOC_SYNC..HEAD
```

Scan the commit list and diff-stat for anything user-visible, anything that added new files, anything that changed test counts, and anything that exposed a new gotcha worth recording.

### 1b. New files

List every new C# file since LAST_DOC_SYNC:

```
git diff --name-only --diff-filter=A $LAST_DOC_SYNC..HEAD -- 'Assets/Scripts/**/*.cs' 'Assets/Editor/**/*.cs'
```

And every new art/resource asset:

```
git diff --name-only --diff-filter=A $LAST_DOC_SYNC..HEAD -- 'Assets/Art/**/*.png' 'Assets/Prefabs/**/*.prefab' 'Assets/Data/**/*.asset' 'Assets/Resources/**'
```

Every new file is a candidate for a project-layout mention in CLAUDE.md and/or README.md.

### 1c. Authoritative test counts

**Do not trust existing doc counts.** Run the tests fresh via MCP:

```
mcp__UnityMCP__run_tests mode=EditMode
mcp__UnityMCP__run_tests mode=PlayMode
```

Record the totals from both runs. These are the only numbers the docs should reflect. If either run fails, **stop** — the iteration isn't ready to merge and doc sync is premature.

### 1d. New serialized fields

Grep for new `[SerializeField]` declarations introduced in the window:

```
git diff $LAST_DOC_SYNC..HEAD -- 'Assets/Scripts/**/*.cs' | grep -E '^\+.*\[SerializeField\]'
```

Every new one is a potential project-layout mention AND a reminder to check that its corresponding prefab/scene YAML matches the declared default (the SerializeField drift trap from the 2026-04-12 bugfix incident).

### 1e. Any new gotchas

From memory of the iteration: did you get bitten by anything new? A Unity API quirk, a WebGL behavior, a physics edge case, a MCP tool interaction? If yes, CLAUDE.md's "Unity MCP gotchas" section may need a new entry.

---

## Step 2 — Audit `docs/superpowers/roadmap.md`

Read the file top to bottom. Check each of:

- **"Last revised" date (near the top):** does it match today's date (or the last doc-touching commit date)? If older than the last shipped iteration, it's stale.
- **Shipped section:** does every merged branch since `LAST_DOC_SYNC` have an entry? Every `iter/*` branch that landed on main should be listed. Sub-iteration shipments (bugfix passes, small features) either get their own entry or a sub-bullet under the nearest iteration.
- **Test counts in shipped entries:** do they match what Unity just reported? A roadmap entry saying "271 tests" is wrong if Unity now reports 272.
- **Next Up section:** any entry that's now stale because the work was superseded, partially shipped outside the formal iteration, or no longer planned? Flag for removal or rewrite.
- **Sizing rule / process notes:** still accurate, or did the iteration expose a reason to adjust?

Record every finding as a punch-list item.

---

## Step 3 — Audit `CLAUDE.md`

Read top to bottom. Check:

- **Project layout tree:** for every new `.cs` file from Step 1b, is there a mention? The tree is intentionally terse (not every file listed) but new systems and new scripts generally get a line. Use judgment: a one-file cosmetic tweak doesn't need a mention; a new subsystem does.
- **Test counts:** the `Tests/` block near the bottom of the project layout should match Unity's reported totals.
- **Conventions section:** did the iteration introduce or change a workflow pattern (new build step, new verification gate, new editor command, new tool)? If yes, the conventions need an update.
- **Unity MCP gotchas section:** any new gotcha from Step 1e? Add it.
- **Before committing / Before promoting sections:** still accurate? Iterations sometimes tighten or relax these.
- **"Detailed reference" pointer:** still correct? (Low chance of drift.)

Record every finding.

---

## Step 4 — Audit `README.md`

Read top to bottom. Check:

- **Play it / Status blurb:** does the status paragraph still accurately describe what a new visitor would see? If the iteration added a player-visible system (new weapon, new panel, new UI element), status may need a sentence.
- **Building and deploying:** only needs updates if the build pipeline changed (new script, new menu item, new flag).
- **How to play:** every player-visible interaction the iteration added should be mentioned here. New buttons, new panels, new visual feedback elements — all go in this section.
- **Weapons sub-sections:** only if a weapon changed fundamentally.
- **Technology / test counts:** bump to authoritative counts from Step 1c.
- **Project layout tree:** same project-layout check as CLAUDE.md.
- **Design and plan documents list:** did the iteration produce a new spec or plan file in `docs/superpowers/`? If yes, add a link.

Record every finding.

---

## Step 5 — Report the punch list

Produce a single structured report. One section per file. One line per finding. Use checkbox syntax so the user can tick items off as edits land:

```
docs/superpowers/roadmap.md:
  - [ ] line 3: "Last revised" date stale — says "2026-04-12", should be "2026-04-15"
  - [ ] missing Shipped entry for <iter/feature-name>
  - [ ] Iter 5 Next Up entry is stale — tuning was partially done in passing

CLAUDE.md:
  - [ ] line 36: EditMode test count stale — says 223, Unity reports 224
  - [ ] line 32: UI script list missing "OptionsPanel"
  - [ ] gotchas section missing: new SerializeField drift trap

README.md:
  - [ ] line 94: test count stale — says 223 EditMode, Unity reports 224
  - [ ] How to play section missing: gear icon and Options panel
```

If every doc is clean, report:

```
docs/superpowers/roadmap.md: ✅ up to date
CLAUDE.md: ✅ up to date
README.md: ✅ up to date
```

---

## Step 6 — Propose concrete edits (optional, on user request)

For each punch-list item, draft the exact `old_string` / `new_string` pair (or the exact insertion point and content for additions). Present them grouped by file so the user can scan one file at a time and approve in one batch.

Example:

```
CLAUDE.md:
  - Edit at line 32 (UI script list):
    OLD: UI/                   MoneyDisplay, LevelStripUI, LevelCell, MissileUpgradePanel, RailgunUpgradePanel, DroneUpgradePanel, BuildSlotPanel, UpgradeButton, ModalClickCatcher, PanelManager
    NEW: UI/                   MoneyDisplay, LevelStripUI, LevelCell, MissileUpgradePanel, RailgunUpgradePanel, DroneUpgradePanel, BuildSlotPanel, UpgradeButton, ModalClickCatcher, PanelManager, OptionsButton, OptionsPanel
```

Wait for user approval before applying. Apply in one commit per doc or one commit total — user's choice.

---

## What this checklist does NOT do

- Does not run the build or deploy — those are separate iteration-close steps.
- Does not edit docs without explicit approval.
- Does not auto-bump the "Last revised" date — surface the stale date in the punch list and let the user decide.
- Does not audit `docs/reference/game-systems.md` or spec/plan files.
- Does not audit test count numbers in the spec/plan files themselves (those are frozen snapshots from when each iteration shipped).

---

## Invocation

This skill lives in the repo at `tools/skills/doc-sync.md`. To invoke it:

- **In chat:** "Run the doc-sync checklist in tools/skills/doc-sync.md." The assistant will read this file and walk through the steps.
- **Via the Skill tool (preferred):** symlink or copy this file to `~/.claude/skills/meteor-idle-doc-sync/SKILL.md` on your machine, then invoke with `skill: "meteor-idle-doc-sync"`. Claude Code's skill discovery picks up files under `~/.claude/skills/` automatically; it does NOT auto-discover files under arbitrary repo paths.

The in-repo copy at `tools/skills/doc-sync.md` is the source of truth. Re-sync the symlink whenever this file changes.
