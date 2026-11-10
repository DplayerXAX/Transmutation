using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch smoke test for Scene_World: -executeMethod WorldMapPlayCheck.Run (isolated copy only). Enters Play mode,
/// waits for the world, then records where the player stands, whether the landmarks built, every error logged,
/// and a frame from the player's camera to ArtCaptures/WorldMap/Play.txt / Play_*.png.
/// </summary>
[InitializeOnLoad]
public static class WorldMapPlayCheck
{
    private const string Key = "WorldMapPlayCheck.Active";
    private const string Out = "ArtCaptures/WorldMap/Play.txt";
    private static readonly List<string> errors = new List<string>();
    private static double started = -1;
    private static int stage;
    private static double completeAt = -1;
    private static double fromEnter = -1;

    static WorldMapPlayCheck()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && errors.Count < 40)
                errors.Add(type + ": " + message + "\n" + string.Join("\n", stack.Split('\n').Take(4)));
        };
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene(WorldMapBuild.ScenePath);
        SessionState.SetBool(Key, true);
        SessionState.SetFloat("WorldMapPlayCheck.Enter", (float)EditorApplication.timeSinceStartup);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup > 900) { File.WriteAllText(Out, "TIMEOUT\n" + string.Join("\n", errors)); EditorApplication.Exit(2); }
        if (!EditorApplication.isPlaying) return;
        if (started < 0) started = EditorApplication.timeSinceStartup;
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        double t = EditorApplication.timeSinceStartup - started;
        if (completeAt < 0 && world != null && world.GenerationComplete) { completeAt = t; fromEnter = EditorApplication.timeSinceStartup - SessionState.GetFloat("WorldMapPlayCheck.Enter", 0f); }
        if (stage == 0 && world != null && world.GenerationComplete && t > 12) stage = 1;
        if (stage == 0 && t < 240) return;
        if (stage == 1 || stage == 0)
        {
            stage = 2;
            var lines = new List<string>();
            var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
            Vector3 p = player != null ? player.transform.position : Vector3.zero;
            lines.Add("World complete: " + (world != null && world.GenerationComplete) + " after " + completeAt.ToString("0.0") + " s after the first Play frame, " + fromEnter.ToString("0.0") + " s after pressing Play (incl. domain reload); saved chunk meshes used: " + (world != null && world.UsedChunkCache));
            if (player != null)
            {
                world.ColumnHeights(p, out float surface, out _, out _);
                bool hit = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 6f, ~0, QueryTriggerInteraction.Ignore);
                lines.Add("Player at " + p + ", surface " + surface.ToString("0.0") + ", ground under feet: " + (hit ? ground.collider.name + " " + ground.distance.ToString("0.0") + " m" : "NONE"));
                var body = player.GetComponent<Rigidbody>();
                if (body != null) lines.Add("Player velocity " + body.linearVelocity);
            }
            var kiln = Object.FindFirstObjectByType<BoneKiln>();
            lines.Add("Bone mother: " + (kiln != null && kiln.Root != null ? "built, sinkhole " + kiln.SinkholeIndex + " root " + kiln.Root.position : "NOT built"));
            var spire = Object.FindFirstObjectByType<HangingSpire>();
            lines.Add("Spire: " + (spire != null ? "sinkhole " + spire.SinkholeIndex + " children " + spire.transform.childCount : "missing"));
            var all = Object.FindObjectsByType<Creature>(FindObjectsSortMode.None);
            lines.Add("Creatures alive: " + all.Count(c => !c.IsDead));
            foreach (var group in all.GroupBy(c => Region(c.transform)))
                lines.Add("  " + group.Key + ": " + group.Count() + " (" + string.Join(", ", group.GroupBy(c => c.GetType().Name).Select(g => g.Key + " " + g.Count())) + ")");
            var fallen = all.Where(c => c.transform.position.y < -80f).ToArray();
            lines.Add("Fell out of the world: " + fallen.Length + (fallen.Length > 0 ? " — " + string.Join(", ", fallen.Take(10).Select(c => c.name)) : ""));
            var cam = Camera.main != null ? Camera.main : Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.isActiveAndEnabled);
            if (cam != null) Shot(cam, "Play_Spawn");
            lines.Add("Errors logged: " + errors.Count);
            lines.AddRange(errors);
            Directory.CreateDirectory(Path.GetDirectoryName(Out));
            File.WriteAllText(Out, string.Join("\n", lines));
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(0);
        }
    }

    private static string Region(Transform t)
    {
        while (t.parent != null && t.parent.parent != null) t = t.parent;
        return t.name;
    }

    private static void Shot(Camera cam, string name)
    {
        var rt = new RenderTexture(1280, 960, 24);
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); tex.Apply();
        File.WriteAllBytes("ArtCaptures/WorldMap/" + name + ".png", tex.EncodeToPNG());
        cam.targetTexture = old; RenderTexture.active = null; rt.Release();
    }
}
