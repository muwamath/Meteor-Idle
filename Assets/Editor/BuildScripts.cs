#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScripts
{
    // Sentinel filename written inside a dev build and checked by
    // tools/deploy-webgl.sh to refuse deploying development builds. Owned by
    // the C# build methods (single source of truth) so it works regardless of
    // how the build was kicked off — CLI wrapper, execute_code via MCP,
    // editor menu item, or Test Runner.
    private const string DevBuildMarker = ".dev-build-marker";

    [MenuItem("Meteor Idle/Build/WebGL (Prod)")]
    public static void BuildWebGL()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Game.unity" },
            locationPathName = "build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        Debug.Log($"[BuildScripts] WebGL build result: {summary.result}, size: {summary.totalSize} bytes, duration: {summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
        {
            // Only exit the editor on failure when running headless via CLI.
            // When running inside a live editor (execute_code or menu item),
            // exiting would kill the user's session — log and return instead.
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        // Symmetric cleanup: a fresh prod build must never leave a stale dev
        // sentinel in place. Safe no-op when the file doesn't exist.
        var stale = Path.Combine("build/WebGL", DevBuildMarker);
        if (File.Exists(stale)) File.Delete(stale);
    }

    // Development WebGL build. DEVELOPMENT_BUILD is auto-defined by Unity when
    // BuildOptions.Development is set, which unlocks DebugOverlay and any other
    // debug-only surfaces wrapped in #if UNITY_EDITOR || DEVELOPMENT_BUILD.
    // Output lives under build/WebGL-dev/ so it never collides with the prod
    // build that gets deployed to gh-pages.
    [MenuItem("Meteor Idle/Build/WebGL (Dev)")]
    public static void BuildWebGLDev()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Game.unity" },
            locationPathName = "build/WebGL-dev",
            target = BuildTarget.WebGL,
            options = BuildOptions.Development,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        Debug.Log($"[BuildScripts] WebGL dev build result: {summary.result}, size: {summary.totalSize} bytes, duration: {summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        // Write the sentinel so tools/deploy-webgl.sh refuses to ship this
        // build even if someone points it at the dev output directory.
        var marker = Path.Combine("build/WebGL-dev", DevBuildMarker);
        File.WriteAllText(marker, "");
    }
}
#endif
