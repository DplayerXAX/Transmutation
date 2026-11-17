using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Request-file checks for the dark tunnels and the conductor baton (no menu).
/// Write one command to Temp/CaveBaton.request with Scene_World open:
///   probe  - Play, wait for the terrain, write which columns near the Goo cave are air (Logs/CaveProbe.txt), stop.
///   test   - Play, wait, walk the player through the tunnels, try the baton, save screenshots to
///            ArtCaptures/DarkTunnels and a log to Logs/CaveBatonTest.txt, stop.
///   bake   - (edit mode) bake Scene_World's saved chunk meshes again, so the carved tunnels show.
/// Progress goes to Temp/CaveBaton.status.
/// Batch (isolated copy): Unity.exe -batchmode -projectPath P -executeMethod CaveBatonCheck.Batch [-caveBake] -caveTest
/// </summary>
[InitializeOnLoad]
public static class CaveBatonCheck
{
    private const string Request = "Temp/CaveBaton.request", Status = "Temp/CaveBaton.status";
    private const string Key = "CaveBatonCheck";
    private static double started = -1;
    private static int step;
    private static double stepTime;

    static CaveBatonCheck()
    {
        EditorApplication.update += Tick;
        if (InBatch) EditorApplication.update += () => { if (EditorApplication.timeSinceStartup > 1200.0) EditorApplication.Exit(3); };
    }

    private static bool InBatch => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-caveTest") >= 0;

    public static void Batch()
    {
        EditorSettings.asyncShaderCompilation = false;
        string[] args = System.Environment.GetCommandLineArgs();
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_World.unity");
        if (System.Array.IndexOf(args, "-caveBake") >= 0) Bake();
        if (!InBatch) { EditorApplication.Exit(0); return; }
        Command = "test";
        started = -1;
        step = 0;
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Bakes the open Scene_World's chunk meshes (after a change to the carved tunnels).</summary>
    public static void Bake()
    {
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        var log = new System.Collections.Generic.List<string>();
        WorldMapDress.BakeChunks(world, log);
        File.WriteAllText("Logs/CaveBake.txt", string.Join(System.Environment.NewLine, log));
    }

    private static string Command { get => SessionState.GetString(Key, ""); set => SessionState.SetString(Key, value); }

    private static void Tick()
    {
        if (Command == "")
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            string command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            if (command == "bake") { Bake(); File.WriteAllText(Status, "baked"); return; }
            if (command != "probe" && command != "test") { File.WriteAllText(Status, "unknown command " + command); return; }
            Command = command;
            started = -1;
            step = 0;
            File.WriteAllText(Status, "entering play");
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (started < 0) started = EditorApplication.timeSinceStartup;
            var world = Object.FindFirstObjectByType<ProceduralWorld>();
            double t = EditorApplication.timeSinceStartup - started;
            if (world == null || !world.GenerationComplete)
            {
                File.WriteAllText(Status, $"waiting for terrain {t:0}s {(world != null ? world.LoadProgress : 0f):P0}");
                if (t > 180) Finish("terrain timeout");
                return;
            }
            if (Command == "probe") { Probe(world); Finish("probe done"); }
            else TestStep(world);
        }
        catch (System.Exception e) { Finish("error: " + e); }
    }

    private static void Finish(string status)
    {
        Command = "";
        File.WriteAllText(Status, status);
        EditorApplication.ExitPlaymode();
        if (InBatch) EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    // One line per column: x z then the air runs "bottom..top" in y.
    private static void Probe(ProceduralWorld world)
    {
        var sb = new StringBuilder();
        const float step = 2f;
        for (float x = -90f; x <= 90f; x += step)
        for (float z = -260f; z <= 0f; z += step)
        {
            sb.Append(x).Append(' ').Append(z);
            bool air = false;
            float start = 0f;
            for (float y = -70f; y <= 20f; y += step)
            {
                bool a = world.SampleDensity(new Vector3(x, y, z)) <= 0f;
                if (a && !air) { start = y; air = true; }
                if (!a && air) { sb.Append(' ').Append(start).Append("..").Append(y - step); air = false; }
            }
            if (air) sb.Append(' ').Append(start).Append("..top");
            sb.Append('\n');
        }
        File.WriteAllText("Logs/CaveProbe.txt", sb.ToString());
    }

    // ---------------- Test walk ----------------

    private static readonly StringBuilder log = new StringBuilder();
    private static CaveGlowCreature conducted;
    private static Vector3 conductedFrom, conductedTo, droppedFrom;
    private static Creature held;

    private static void TestStep(ProceduralWorld world)
    {
        var tunnels = Object.FindFirstObjectByType<DarkTunnels>();
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        if (tunnels == null || !tunnels.Built || carrier == null || carrier.PlayerBody == null)
        {
            if (EditorApplication.timeSinceStartup - started > 200) { Write("no tunnels / player"); Finish("no tunnels"); }
            return;
        }
        Rigidbody body = carrier.PlayerBody;
        Camera cam = carrier.CarryCamera != null ? carrier.CarryCamera : Camera.main;
        double now = EditorApplication.timeSinceStartup;
        if (now < stepTime) return;
        File.WriteAllText(Status, "test step " + step);
        switch (step)
        {
            case 0:
                log.Clear();
                Write("caves built, spiral mouth " + DarkTunnelShape.Mouth + ", tower exit " + tunnels.EntranceWorld + ", hub " + tunnels.HallWorld);
                Write("baton held from the start: " + (tunnels.Baton != null && tunnels.Baton.Held) + ", is tool " + (tunnels.Baton != null && tunnels.Baton.IsTool));
                var slots0 = Object.FindFirstObjectByType<BodyMounts>();
                Write($"player started at {body.position} (spawn underground), hotbar selected {(slots0 != null ? slots0.Selected : -1)} tool slot {(slots0 != null ? slots0.ToolSlot : -1)}");
                Shot(cam, "00_start_underground");
                Place(body, cam, tunnels.EntranceOutsideWorld + new Vector3(0f, 1f, -5f), DarkTunnelShape.Mouth);
                Wait(2.5);
                break;
            case 1:
                Shot(cam, "01_spiral_pit");
                Place(body, cam, tunnels.EntranceWorld + Vector3.up * 0.5f, tunnels.HallWorld);
                Wait(3);
                break;
            case 2:
                Shot(cam, "02_tower_door_into_cavern");
                Write($"at tower door: darkness {DarkTunnels.Darkness:0.00}, fog end {RenderSettings.fogEndDistance:0.0}, player y {body.position.y:0.0}");
                Place(body, cam, tunnels.HallWorld + new Vector3(4f, 0.5f, 0f), new Vector3(-124f, -64f, 164f)); // the mirror lake
                Wait(3);
                break;
            case 3:
                Shot(cam, "03_cavern_hub");
                Write($"mirror pools: {Object.FindObjectsByType<MirrorPool>(FindObjectsSortMode.None).Length}, render supported {MirrorPool.DebugSupported}, renders {MirrorPool.DebugRenders}, ticks {MirrorPool.DebugTicks}, no pool chosen {MirrorPool.DebugNoBest}, frame {Time.frameCount}, nearest pool {MirrorPool.DebugNearest:0.0} m seen {MirrorPool.DebugNearestSeen} from {MirrorPool.DebugCamera}");
                if (MirrorPool.DebugTexture != null)
                {
                    var keep = RenderTexture.active;
                    RenderTexture.active = MirrorPool.DebugTexture;
                    var mt = new Texture2D(MirrorPool.DebugTexture.width, MirrorPool.DebugTexture.height, TextureFormat.RGB24, false);
                    mt.ReadPixels(new Rect(0, 0, mt.width, mt.height), 0, 0);
                    RenderTexture.active = keep;
                    File.WriteAllBytes("ArtCaptures/DarkTunnels/03b_mirror_texture.png", mt.EncodeToPNG());
                    Object.DestroyImmediate(mt);
                }
                foreach (var mp in Object.FindObjectsByType<MirrorPool>(FindObjectsSortMode.None))
                    ShotFrom(cam, mp.transform.position + new Vector3(0f, 2.2f, -7f), mp.transform.position + new Vector3(0f, 0f, 2f), "03c_pool_" + mp.GetInstanceID());
                Write($"hub: darkness {DarkTunnels.Darkness:0.00}, player y {body.position.y:0.0}");
                foreach (var g in Object.FindObjectsByType<CaveGlowCreature>(FindObjectsSortMode.None))
                    Write($"  {g.GetType().Name} at {g.transform.position}");
                Write("water pieces: " + Object.FindObjectsByType<StrangeWater>(FindObjectsSortMode.None).Length);
                Place(body, cam, tunnels.SkeletonWorld + new Vector3(4f, 0.5f, 0f), tunnels.SkeletonWorld + Vector3.up * 1.5f);
                Wait(3);
                break;
            case 4:
            {
                Shot(cam, "04_skeleton");
                var bones = Object.FindFirstObjectByType<BatonSkeleton>();
                Write($"player y at skeleton {body.position.y:0.0} (floor about {tunnels.SkeletonWorld.y:0.0}), skeleton {(bones != null ? "built" : "missing")}");
                // Look at the nearest hanging pool and floating blob from a few metres.
                StrangeWater pool = null, blob = null;
                foreach (var w in Object.FindObjectsByType<StrangeWater>(FindObjectsSortMode.None))
                {
                    if (w.name.StartsWith("Hanging") && pool == null) pool = w;
                    if (w.name.StartsWith("Floating") && blob == null) blob = w;
                }
                if (pool != null) ShotFrom(cam, pool.transform.position + new Vector3(6f, -7f, 2f), pool.transform.position, "05_hanging_pool");
                if (blob != null) ShotFrom(cam, blob.transform.position + new Vector3(3.5f, 0.5f, 1.5f), blob.transform.position, "06_floating_blob");
                // Conduct the Tone Crate: play it alone and point to its east.
                var crate = Object.FindFirstObjectByType<ToneCrate>();
                var baton = tunnels.Baton;
                if (crate != null && baton != null)
                {
                    if (!baton.Held) baton.DebugGive();
                    Place(body, cam, crate.transform.position + new Vector3(0f, 3f, -11f), crate.transform.position);
                    conducted = crate;
                    conductedFrom = crate.transform.position;
                    conductedTo = crate.transform.position + Vector3.right * 12f;
                    baton.DebugConduct(crate.Note, conductedTo, 2.5f);
                }
                else Write("crate " + (crate != null) + " baton " + (baton != null));
                Wait(4);
                break;
            }
            case 5:
            {
                Shot(cam, "07_tone_crate");
                if (conducted != null)
                    Write($"tone crate from {conductedFrom}, led east: now {conducted.transform.position} (moved {Vector3.Distance(conducted.transform.position, conductedFrom):0.0} m)");
                var maw = Object.FindFirstObjectByType<LureMaw>();
                Write("lure maw: " + (maw != null ? maw.transform.position.ToString() : "missing"));
                // Then a Glim, from wherever the camera stands.
                var baton = tunnels.Baton;
                GlimLantern glim = Object.FindFirstObjectByType<GlimLantern>();
                if (glim != null && baton != null)
                {
                    Place(body, cam, glim.transform.position + new Vector3(0f, 0f, -8f), glim.transform.position);
                    conducted = glim;
                    conductedFrom = glim.transform.position;
                    conductedTo = glim.transform.position + Vector3.right * 3f;
                    baton.DebugConduct(glim.Note, conductedTo, 3f);
                }
                Wait(3.5);
                break;
            }
            case 6:
            {
                Shot(cam, "08_conducting_glim");
                if (conducted != null)
                    Write($"glim led towards {conductedTo}: now {conducted.transform.position} ({Vector3.Distance(conducted.transform.position, conductedTo):0.0} m off)");
                Write("muted while a note played (should be 0 now): " + System.Linq.Enumerable.Count(CaveGlowCreature.All, c => c.Muted) + "; layers: glim " + AudioManager.GetLayerWeight("BGM_glim").ToString("0.00") + " maw " + AudioManager.GetLayerWeight("BGM_maw").ToString("0.00"));
                Write($"frame ms {Time.unscaledDeltaTime * 1000f:0.0}");
                // Grip check: where the baton sits in the view, then drop it and see it land.
                var baton = tunnels.Baton;
                Place(body, cam, tunnels.HallWorld + Vector3.up * 0.5f, tunnels.HallWorld + new Vector3(0f, 0f, 10f));
                if (baton != null && !baton.Held) baton.DebugGive();
                Wait(1.5);
                break;
            }
            case 7:
            {
                var baton = tunnels.Baton;
                if (baton != null)
                {
                    Vector3 grip = baton.transform.TransformPoint(new Vector3(0f, 0.06f, 0f));
                    Vector3 tipDir = baton.transform.up;
                    Vector3 rel = grip - cam.transform.position;
                    Write($"baton held {baton.Held}: camera {cam.name} at {cam.transform.position} scale {cam.transform.lossyScale}, grip {grip}, grip in view (right {Vector3.Dot(rel, cam.transform.right):0.00}, up {Vector3.Dot(rel, cam.transform.up):0.00}, forward {Vector3.Dot(rel, cam.transform.forward):0.00}), tip direction in view {cam.transform.InverseTransformDirection(tipDir)}; main camera {(Camera.main != null ? Camera.main.name + " at " + Camera.main.transform.position : "none")}");
                    Shot(cam, "09_baton_in_hand");
                    var slots = Object.FindFirstObjectByType<BodyMounts>();
                    if (slots != null) slots.Select(0);
                    Write($"wheel to slot 0: baton held {baton.Held}, baton active {baton.gameObject.activeSelf}");
                    if (slots != null) slots.Select(slots.ToolSlot);
                    Write($"wheel back to the tool slot: baton held {baton.Held}");
                    droppedFrom = baton.transform.position;
                }
                Wait(4);
                break;
            }
            case 8:
            {
                var baton = tunnels.Baton;
                if (baton != null)
                {
                    Write($"baton still held after a few seconds: {baton.Held}");
                    var slots = Object.FindFirstObjectByType<BodyMounts>();
                    if (slots != null) slots.Select(0);
                    var glim = Object.FindFirstObjectByType<GlimLantern>();
                    if (glim != null && carrier.TryCarryFromInteraction(glim))
                    {
                        Wait(1.5);
                        held = glim;
                        break;
                    }
                }
                Wait(0.5);
                break;
            }
            case 9:
            {
                if (held != null)
                {
                    Vector3 rel = held.transform.position - cam.transform.position;
                    Write($"glim held: pivot in view (right {Vector3.Dot(rel, cam.transform.right):0.00}, up {Vector3.Dot(rel, cam.transform.up):0.00}, forward {Vector3.Dot(rel, cam.transform.forward):0.00})");
                    Shot(cam, "10_glim_in_hand");
                }
                Write("done");
                Finish("test done");
                break;
            }
        }
        step++;
    }

    private static void Wait(double seconds) => stepTime = EditorApplication.timeSinceStartup + seconds;

    private static void Place(Rigidbody body, Camera cam, Vector3 at, Vector3 lookAt)
    {
        body.position = at;
        body.transform.position = at;
        body.linearVelocity = Vector3.zero;
        Look(body, cam, lookAt);
    }

    private static void Look(Rigidbody body, Camera cam, Vector3 lookAt)
    {
        var controller = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        Vector3 d = lookAt - cam.transform.position;
        if (controller != null) controller.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
    }

    private static void Shot(Camera cam, string name)
    {
        Directory.CreateDirectory("ArtCaptures/DarkTunnels");
        var rt = new RenderTexture(1280, 720, 24);
        var old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = old;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes("ArtCaptures/DarkTunnels/" + name + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    private static void ShotFrom(Camera cam, Vector3 from, Vector3 at, string name)
    {
        var look = new GameObject("Check camera").AddComponent<Camera>();
        look.CopyFrom(cam);
        look.transform.position = from;
        look.transform.LookAt(at);
        Shot(look, name);
        Object.DestroyImmediate(look.gameObject);
    }

    private static void Write(string line)
    {
        log.AppendLine(line);
        File.WriteAllText("Logs/CaveBatonTest.txt", log.ToString());
    }
}
