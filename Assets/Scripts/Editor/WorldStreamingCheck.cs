using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Request-file check of WorldStreaming (no menu). Write "run" to Temp/StreamCheck.request with Scene_World open: the
/// editor enters Play, carries the player to a few surface spots, and at each one counts streamed objects inside the
/// wake radius that are still off and saves a Game view shot to ArtCaptures/Streaming. Result: Temp/StreamCheck.status.
/// </summary>
[InitializeOnLoad]
public static class WorldStreamingCheck
{
    private const string Request = "Temp/StreamCheck.request", Status = "Temp/StreamCheck.status", Key = "WorldStreamingCheck";
    private const string Folder = "ArtCaptures/Streaming";
    private const double StartWait = 14.0, SettleWait = 7.0;
    private static readonly Vector3[] Spots =
    {
        new Vector3(-174f, 60f, -105f),  // starting forest
        new Vector3(-100f, 40f, 10f),    // flower field edge
        new Vector3(30f, 60f, 110f),     // ruins side of the crack
    };

    private static double stepStart = -1;
    private static int spot = -1;
    private static readonly StringBuilder report = new StringBuilder();

    static WorldStreamingCheck()
    {
        EditorApplication.update += Tick;
        // Play stopped by hand before the check finished: forget it, so the next Play is a normal one.
        EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key, 0) != 0) { SessionState.SetInt(Key, 0); stepStart = -1; File.WriteAllText(Status, "stopped: Play ended early"); } };
    }

    private static void Tick()
    {
        if (SessionState.GetInt(Key, 0) == 0)
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            SessionState.SetInt(Key, 1);
            File.WriteAllText(Status, "entering play");
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!EditorApplication.isPlaying) return;
        try { Advance(); }
        catch (System.Exception e) { Finish("error: " + e); }
    }

    private static void Advance()
    {
        double now = EditorApplication.timeSinceStartup;
        if (stepStart < 0) { stepStart = now; spot = -1; report.Clear(); return; }
        if (now - stepStart < (spot < 0 ? StartWait : SettleWait)) return;
        if (spot >= 0) Inspect(spot);
        spot++;
        if (spot >= Spots.Length) { Finish("done\n" + report); return; }
        Carry(Spots[spot]);
        stepStart = now;
        File.WriteAllText(Status, "at spot " + spot);
    }

    private static void Carry(Vector3 from)
    {
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        Rigidbody body = carrier != null ? carrier.PlayerBody : null;
        if (body == null) throw new System.InvalidOperationException("no player body");
        Vector3 at = from;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 200f, 1 << 7, QueryTriggerInteraction.Ignore)) at = hit.point + Vector3.up * 1.5f;
        body.position = at;
        body.transform.position = at;
        body.linearVelocity = Vector3.zero;
    }

    private static void Inspect(int index)
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform root = WorldStreaming.FindRoot(scene);
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        Vector3 at = carrier.PlayerBody.position;
        int on = 0, missing = 0, far = 0;
        var parts = new List<Renderer>();
        foreach (WorldStreaming.Group group in WorldStreaming.Groups)
        {
            Transform t = WorldStreaming.FindGroup(root, group.path);
            if (t == null) { report.AppendLine("missing group " + group.path); continue; }
            t.GetComponentsInChildren(true, parts);
            foreach (Renderer part in parts)
            {
                float distance = Vector3.Distance(at, part.bounds.center) - part.bounds.extents.magnitude;
                if (distance > group.radius * 0.9f) { far++; continue; }
                if (part.gameObject.activeInHierarchy) on++; else missing++;
            }
        }
        Directory.CreateDirectory(Folder);
        ScreenCapture.CaptureScreenshot($"{Folder}/spot_{index}.png");
        report.AppendLine($"spot {index} at {at}: {on} near objects on, {missing} near objects still off, {far} beyond the radius");
    }

    private static void Finish(string result)
    {
        SessionState.SetInt(Key, 0);
        stepStart = -1;
        File.WriteAllText(Status, result);
        EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }
}
