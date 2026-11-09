using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch only: runs LeafMapPerfProbe (full: scene content + every script type) on Scene_LeafMap and quits when
/// ArtCaptures/LeafMap/Perf.txt is written. -executeMethod LeafMapPerfBatch.Run
/// </summary>
public static class LeafMapPerfBatch
{
    private static double started;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        Directory.CreateDirectory("ArtCaptures/LeafMap");
        if (File.Exists("ArtCaptures/LeafMap/Perf.txt")) File.Delete("ArtCaptures/LeafMap/Perf.txt");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_LeafMap.unity", OpenSceneMode.Single);
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Watch;
        LeafMapPerfProbe.Begin();
    }

    private static void Watch()
    {
        bool done = File.Exists("ArtCaptures/LeafMap/Perf.txt") && !EditorApplication.isPlaying;
        if (done || EditorApplication.timeSinceStartup - started > 1300.0) EditorApplication.Exit(done ? 0 : 2);
    }
}
