using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds a Windows player of the NeoRacer scene alone (the project's build list
// holds every AutoDRIVE scene). Batchmode:
//   Unity.exe -batchmode -projectPath <project> -executeMethod NeoRacerPlayerBuild.Build
//             -neoracerBuildDir <output folder> -logFile <log>
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
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = System.IO.Path.Combine(args[at + 1], "AutoDRIVE Simulator.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        // The project builds with IL2CPP, which needs Unity's Windows IL2CPP
        // module; Mono builds with the editor alone. The project's own
        // setting is put back after the build.
        ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
        BuildSummary s = report.summary;
        Debug.Log("NeoRacerPlayerBuild: " + s.result + ", " + s.totalErrors + " errors, " +
                  (s.totalSize / 1048576) + " MB, " + s.totalTime + " -> " + s.outputPath);
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
