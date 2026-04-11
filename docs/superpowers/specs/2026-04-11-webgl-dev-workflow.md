# Local WebGL Dev Workflow — Design

Scope extension on `iter/aim-fixes` (Iter 0). Adds a local WebGL dev build flavor so the manual verification gate runs against the same runtime users will see on gh-pages, while keeping the debug overlay available during development and stripped in production.

Date: 2026-04-11.

## Problem

The current verify-gate runs in Unity Editor play mode. That catches logic bugs but not WebGL-specific regressions (input handling, compression, loader JS, texture formats, framerate). Users only ever run the WebGL build, so editor play mode verifying is a false sense of safety.

`DebugOverlay` is gated by `#if UNITY_EDITOR`, so it's stripped from every player build, including any build used for manual verification. The user wants to keep debug controls during verify, but still strip them from the gh-pages build.

## Goals

- Final manual verify before merge/push/deploy runs against a **local WebGL build**, not Unity Editor play mode.
- Debug overlay (and any future debug-only surfaces) are present in the local dev build.
- Production builds pushed to gh-pages strip the debug overlay via existing `#else` stub pattern.
- The deploy pipeline makes it *impossible* to accidentally ship a dev build — not "try to avoid it", but a hard gate.
- Editor play mode stays usable for fast iterative debugging during development. It's just not the final gate.

## Non-goals

- No Unity version upgrade.
- No change to the existing `tools/build-webgl.sh` or `tools/deploy-webgl.sh` contracts beyond adding sentinel plumbing — they still build/deploy prod.
- No remote hosting changes. GH Pages stays the prod target.
- No automated CI. Builds stay local.

## Design

### 1. `DebugOverlay` gating

Change the preprocessor guard in `Assets/Scripts/Debug/DebugOverlay.cs`:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ... full implementation ...
#else
// ... inactive stub ...
#endif
```

`DEVELOPMENT_BUILD` is Unity's built-in scripting define, automatically set when `BuildOptions.Development` is passed to `BuildPipeline.BuildPlayer`. No custom define, no project settings change.

The existing `#else` stub (stubs the class as an immediately-inactive `MonoBehaviour`) stays unchanged. It covers player builds without `DEVELOPMENT_BUILD` — i.e., prod.

**Rule for future debug surfaces:** wrap them in the same `#if UNITY_EDITOR || DEVELOPMENT_BUILD` block. Never use plain `#if UNITY_EDITOR` for anything a human wants to see during runtime verification.

### 2. `BuildScripts.BuildWebGLDev`

Add a second static method in `Assets/Editor/BuildScripts.cs` that mirrors `BuildWebGL` but sets `BuildOptions.Development` and outputs to `build/WebGL-dev/`:

```csharp
public static void BuildWebGLDev()
{
    var opts = new BuildPlayerOptions
    {
        scenes = new[] { "Assets/Scenes/Game.unity" },
        locationPathName = "build/WebGL-dev",
        target = BuildTarget.WebGL,
        options = BuildOptions.Development,
    };
    // ... same reporting + exit behavior as BuildWebGL ...
}
```

The two methods share no code — they're both short, and trying to factor the common bits out would cost more readability than it saves. YAGNI.

### 3. Sentinel file for dev-vs-prod distinction

When `tools/build-webgl-dev.sh` completes a successful build, it touches `build/WebGL-dev/.dev-build-marker`. The file is empty; its mere presence means "this directory holds a dev build."

When `tools/build-webgl.sh` completes a successful prod build, it **deletes** any `build/WebGL/.dev-build-marker` left over from a previous state (symmetric cleanup — re-running a prod build after a dev build always lands in a deploy-safe state, no manual cleanup needed).

When `tools/deploy-webgl.sh` starts, it checks for `build/WebGL/.dev-build-marker`. If found, it aborts with a clear error message telling the user to run `tools/build-webgl.sh` (prod) before deploying. This runs *before* the identity scrub so failures are cheap to detect.

**Why a sentinel and not Unity's internal markers:** our sentinel is a contract we own. Unity's `DEVELOPMENT_BUILD` define leaks into multiple places (loader JS, profiler config) and the strings vary across Unity versions. Grepping for them is fragile. A dotfile is unambiguous and zero-overhead.

### 4. `tools/build-webgl-dev.sh`

Near-copy of `tools/build-webgl.sh`:

- `BUILD_DIR="${REPO_ROOT}/build/WebGL-dev"`
- `LOG_FILE="${REPO_ROOT}/build/webgl-dev-build.log"`
- `-executeMethod BuildScripts.BuildWebGLDev`
- After the index.html existence check: `touch "${BUILD_DIR}/.dev-build-marker"`
- Final hint: "Serve locally with `tools/serve-webgl-dev.sh`"

Same Editor-open refusal, same Unity install check, same tail-40 log on failure.

### 5. `tools/serve-webgl-dev.sh`

One-purpose wrapper: checks port 8000 is free, then runs `python3 -m http.server 8000 --directory build/WebGL-dev`. If the port is busy, prints which process holds it (`lsof -iTCP:8000 -sTCP:LISTEN`) and exits non-zero.

Foreground process by default — the user sees the access log and hits Ctrl-C to stop. When Claude runs the script via MCP, it runs in the background and gets killed explicitly after verification.

### 6. `tools/build-webgl.sh` cleanup

Add one line after the existing size/success report:

```bash
rm -f "${BUILD_DIR}/.dev-build-marker"
```

This handles the "I just ran a dev build, now I'm running prod for real" case without requiring `rm -rf`.

### 7. `tools/deploy-webgl.sh` sentinel check

Insert near the top, immediately after the `index.html` existence check, before the uncommitted-source check:

```bash
if [[ -f "${BUILD_DIR}/.dev-build-marker" ]]; then
    echo "error: ${BUILD_DIR} contains a development build (.dev-build-marker sentinel found)." >&2
    echo "       Development builds must not be deployed to gh-pages." >&2
    echo "       Run tools/build-webgl.sh (prod, not dev) before deploying." >&2
    exit 1
fi
```

### 8. `.gitignore`

No change needed. `[Bb]uild/` already covers `build/WebGL-dev/`.

### 9. `CLAUDE.md` updates

Three sections change:

- **Manual play-verify loop** — steps 5–8 (`manage_editor play` … `manage_editor stop`) become: close Unity, run `tools/build-webgl-dev.sh`, run `tools/serve-webgl-dev.sh`, navigate via `chrome-devtools-mcp`, check console, kill server, close tab. Editor play mode is noted as still allowed for fast iterative debugging but not as the final gate.
- **Before promoting a branch to `main`** — step 2 (manual play-mode session) changes to "local WebGL dev build session."
- **WebGL build and GitHub Pages deploy** — add bullets for `build-webgl-dev.sh` and `serve-webgl-dev.sh`, document the sentinel rule, note that prod-build is what ships to gh-pages.

### 10. `README.md` updates

- The "Building and deploying the WebGL player" section adds a dev-flavor bullet.
- The debug overlay line changes from "only exists in editor play mode — stripped from player builds" to "exists in editor play mode and in local dev WebGL builds — stripped from production builds pushed to gh-pages".

## Risk

- **Low.** One preprocessor change, one new editor method, two new shell scripts, two touched shell scripts, two doc updates. No existing behavior changes for prod builds. The sentinel check makes the deploy pipeline strictly safer.
- **Watch:** `DEVELOPMENT_BUILD` adds `Development Build` watermark overlay to the WebGL build by default. That's desirable (visually confirms "this is not prod") but if you find it obtrusive we can suppress it via `PlayerSettings.WebGL.showDevToolsStripInPlayer` — not recommended.

## Sizing

- **Commit A — C# + spec:** 3 files, ~40 LOC changes.
- **Commit B — Shell scripts:** 4 files (2 new, 2 modified), ~60 LOC changes.
- **Commit C — Docs:** 2 files, ~30 LOC changes.
- **Verification:** run the new flow against Iter 0. No code changes.

Within the ≤ 3 files or ≤ ~200 lines per commit rule.
