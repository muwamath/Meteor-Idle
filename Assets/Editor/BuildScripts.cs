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
}
#endif
