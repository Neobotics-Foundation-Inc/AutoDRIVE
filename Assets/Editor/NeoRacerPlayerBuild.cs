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
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary s = report.summary;
        Debug.Log("NeoRacerPlayerBuild: " + s.result + ", " + s.totalErrors + " errors, " +
                  (s.totalSize / 1048576) + " MB, " + s.totalTime + " -> " + s.outputPath);
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
