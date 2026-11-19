using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds a Windows player of one NeoRacer scene alone (the project's build list
// holds every AutoDRIVE scene): the NeoRacer scene, or the one -neoracerScene
// names (the two-car scene: "Assets/Scenes/NeoRacer - AuE-X930 - Two Cars.unity").
// Batchmode:
//   Unity.exe -batchmode -projectPath <project> -executeMethod NeoRacerPlayerBuild.Build
//             -neoracerBuildDir <output folder> [-neoracerScene <scene>] -logFile <log>
public static class NeoRacerPlayerBuild
{
    const string Scene = "Assets/Scenes/NeoRacer - AuE-X930.unity";

    public static void Build()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-neoracerBuildDir");
        if (at < 0 || at + 1 >= args.Length)
        {
            Debug.LogError("NeoRacerPlayerBuild: pass -neoracerBuildDir <output folder>");
            EditorApplication.Exit(2);
            return;
        }
        int named = Array.IndexOf(args, "-neoracerScene");
        string scene = named >= 0 && named + 1 < args.Length ? args[named + 1] : Scene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
        {
            Debug.LogError("NeoRacerPlayerBuild: no scene at " + scene);
            EditorApplication.Exit(2);
            return;
        }
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = System.IO.Path.Combine(args[at + 1], "AutoDRIVE Simulator.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        // The project builds with IL2CPP, which needs Unity's Windows IL2CPP
        // module; Mono builds with the editor alone. The project's own
        // setting is put back after the build.
        ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            // BuildPlayer saves the project's settings: a build that throws must not leave Mono in them
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
        }
        BuildSummary s = report.summary;
        Debug.Log("NeoRacerPlayerBuild: " + s.result + ", " + s.totalErrors + " errors, " +
                  (s.totalSize / 1048576) + " MB, " + s.totalTime + " -> " + s.outputPath);
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
