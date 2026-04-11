#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScripts
{
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
            EditorApplication.Exit(1);
        }
    }

    // Development WebGL build. DEVELOPMENT_BUILD is auto-defined by Unity when
    // BuildOptions.Development is set, which unlocks DebugOverlay and any other
    // debug-only surfaces wrapped in #if UNITY_EDITOR || DEVELOPMENT_BUILD.
    // Output lives under build/WebGL-dev/ so it never collides with the prod
    // build that gets deployed to gh-pages.
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
            EditorApplication.Exit(1);
        }
    }
}
#endif
