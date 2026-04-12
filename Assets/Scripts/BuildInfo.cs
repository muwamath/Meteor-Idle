using UnityEngine;

// Read once at first access. The actual contents come from
// Resources/BuildInfo.txt, which BuildScripts.cs writes immediately
// before each build (gitignored). In editor or when no build has
// been run, the file is missing and we report "dev / unknown".
//
// File format: "<sha>|<utc-iso-date>|<variant>" where variant is
// "prod" or "dev". OptionsPanel parses and renders.
public static class BuildInfo
{
    private static bool loaded;
    private static string sha = "dev";
    private static string date = "unknown";
    private static string variant = "editor";

    public static string Sha     { get { Load(); return sha; } }
    public static string Date    { get { Load(); return date; } }
    public static string Variant { get { Load(); return variant; } }

    public static string DisplayLine
    {
        get
        {
            Load();
            return $"{sha} · {date} UTC · {variant}";
        }
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        var ta = Resources.Load<TextAsset>("BuildInfo");
        if (ta == null || string.IsNullOrEmpty(ta.text)) return;
        var parts = ta.text.Split('|');
        if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0])) sha = parts[0];
        if (parts.Length >= 2) date = parts[1];
        if (parts.Length >= 3) variant = parts[2];
    }
}
