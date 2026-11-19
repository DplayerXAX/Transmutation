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
    private static NoteSeed seedTest;
    private static CaveGlowCreature navCreature;
    private static Vector3 navGoal;
    private static float navStart;
    private static string navName;

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
        if (!DarkTunnels.UseBaton) { SongStep(world, tunnels, body, cam); step++; return; }
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
                ToneCrate crate = null;
                foreach (var m in Object.FindObjectsByType<ToneCrate>(FindObjectsSortMode.None))
                    if (m.SeedsNeeded == 0 && m.transform.position.y < -60f) { crate = m; break; }
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
                int rockPoints = 0, tunnelPoints = 0;
                foreach (Vector3 p in DarkTunnelShape.TunnelPoints()) { tunnelPoints++; if (world.SampleDensity(p) > 0f) rockPoints++; }
                Write($"tunnel centre points in rock: {rockPoints} of {tunnelPoints}");
                Write($"mesas {Object.FindObjectsByType<ToneCrate>(FindObjectsSortMode.None).Length}, seeds {Object.FindObjectsByType<NoteSeed>(FindObjectsSortMode.None).Length}, hanging lakes {Object.FindObjectsByType<HangingLake>(FindObjectsSortMode.None).Length}, floating balls {Object.FindObjectsByType<StrangeWater>(FindObjectsSortMode.None).Length}");
                foreach (var lake in Object.FindObjectsByType<HangingLake>(FindObjectsSortMode.None))
                {
                    ShotFrom(cam, lake.transform.position + new Vector3(5f, -1f, -5f), lake.transform.position + Vector3.up * 3f, "11_lake");
                    break;
                }
                var slots9 = Object.FindFirstObjectByType<BodyMounts>();
                if (held != null) carrier.Drop();
                if (slots9 != null) slots9.Select(slots9.ToolSlot);
                foreach (var candidate in Object.FindObjectsByType<NoteSeed>(FindObjectsSortMode.None))
                    if (Physics.Raycast(candidate.transform.position + Vector3.up * 0.3f, Vector3.up, 2.5f, DarkTunnels.TerrainMask) == false) { seedTest = candidate; break; }
                if (seedTest != null && tunnels.Baton != null)
                {
                    Place(body, cam, seedTest.transform.position + new Vector3(0.5f, 1.2f, 0.3f), seedTest.transform.position);
                    tunnels.Baton.DebugConduct(seedTest.Note, seedTest.transform.position, 1.5f);
                }
                Wait(4);
                break;
            }
            case 10:
            {
                Write($"seed taken after playing its note: baton holds {(tunnels.Baton != null ? tunnels.Baton.SeedTotal : -1)}; held {tunnels.Baton != null && tunnels.Baton.Held}, pod open {(seedTest != null ? seedTest.Openness : -1f):0.00}, player {body.position}, seed {(seedTest != null ? seedTest.transform.position.ToString() : "none")}");
                carrier.Release(); // something takes it out of the hand: it must come back by itself
                if (tunnels.Baton != null) tunnels.Baton.DebugNextNote();
                Wait(0.12);
                break;
            }
            case 11:
            {
                Write("baton back in the hand after being taken away: " + (tunnels.Baton != null && tunnels.Baton.Held));
                Shot(cam, "12_note_pop");
                ShotFrom(cam, new Vector3(-28f, -54f, 160f), new Vector3(8f, -30f, 160f), "16_mother_from_below");
                var noteCount = new int[4];
                foreach (var c in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None)) noteCount[(int)CreatureNoteTable.For(c)]++;
                Write($"creatures per note E {noteCount[0]} G {noteCount[1]} B {noteCount[2]} D {noteCount[3]}; fog end {RenderSettings.fogEndDistance:0.0}");
                DarkTunnels.DebugBright = true;
                Wait(2.5);
                break;
            }
            case 12:
            case 13:
            case 14:
            {
                // Way round: start one side of the south-west pillar, lead it to the other side.
                if (step > 12) Write($"round the pillar, {navName}: {Vector3.Distance(navCreature.transform.position, navGoal):0.0} m from the goal (started {navStart:0.0})");
                Place(body, cam, new Vector3(-142f, -62.5f, 150f), new Vector3(-128f, -60f, 150f));
                Vector3 west = new Vector3(-138f, -60f, 150f), east = new Vector3(-118f, -60f, 150f);
                Physics.Raycast(east + Vector3.up, Vector3.down, out RaycastHit eastFloor, 8f, DarkTunnels.TerrainMask);
                Physics.Raycast(west + Vector3.up, Vector3.down, out RaycastHit westFloor, 8f, DarkTunnels.TerrainMask);
                if (step == 12)
                {
                    var g = Object.FindFirstObjectByType<GlimLantern>();
                    g.transform.position = west; g.SetHome(west);
                    navCreature = g; navGoal = east; navName = "glim";
                }
                else if (step == 13)
                {
                    var maw = Object.FindFirstObjectByType<LureMaw>();
                    maw.SetDen(westFloor.point, Vector3.right);
                    navCreature = maw; navGoal = eastFloor.point; navName = "maw";
                }
                else
                {
                    var snail = Object.FindFirstObjectByType<BellSnail>();
                    snail.Attach(westFloor.point, westFloor.normal);
                    navCreature = snail; navGoal = eastFloor.point; navName = "snail";
                }
                navStart = Vector3.Distance(navCreature.transform.position, navGoal);
                tunnels.Baton.DebugConduct(navCreature.Note, navGoal, 9f);
                Wait(10);
                break;
            }
            case 15:
            {
                Write($"round the pillar, {navName}: {Vector3.Distance(navCreature.transform.position, navGoal):0.0} m from the goal (started {navStart:0.0})");
                ShotFrom(cam, new Vector3(-62f, -46f, 150f), new Vector3(-120f, -54f, 165f), "13_overview_east");
                ShotFrom(cam, new Vector3(-118f, -38f, 112f), new Vector3(-115f, -56f, 180f), "14_overview_south");
                ShotFrom(cam, new Vector3(-138f, -36f, 205f), new Vector3(-110f, -56f, 140f), "15_overview_north");
                DarkTunnels.DebugBright = false;
                Write("done");
                Finish("test done");
                break;
            }
        }
        step++;
    }

    // The creature song: phrase by inputs, following by role, the full chord and its door, a seed, a mesa.
    private static CaveGlowCreature songGlim, songMaw, songSnail;
    private static void SongStep(ProceduralWorld world, DarkTunnels tunnels, Rigidbody body, Camera cam)
    {
        CreatureSong song = CreatureSong.Instance;
        Vector3 hub = tunnels.HallWorld + Vector3.up * 0.5f;
        switch (step)
        {
            case 0:
                log.Clear();
                songGlim = Object.FindFirstObjectByType<GlimLantern>();
                songMaw = Object.FindFirstObjectByType<LureMaw>();
                songSnail = Object.FindFirstObjectByType<BellSnail>();
                foreach (var c in new[] { songGlim, songMaw, songSnail })
                    Write($"{c.GetType().Name}: role {c.Role}, phrase {string.Join(" ", song.PhraseOf(c))}");
                foreach (var m in Object.FindObjectsByType<ToneCrate>(FindObjectsSortMode.None))
                    Write($"mesa {m.Note}: role {m.Role}, phrase {string.Join(" ", song.PhraseOf(m))}, needs {m.SeedsNeeded}");
                Write("baton in the scene: " + (Object.FindFirstObjectByType<ConductorBaton>() != null));
                foreach (var t in new[] { typeof(GlimLantern), typeof(BellSnail), typeof(LureMaw), typeof(FuzzCreature), typeof(FireEater), typeof(SkySpider), typeof(BirdCreature), typeof(TentacleCreature), typeof(Squirrel) })
                {
                    SongInput[] motif = CreatureNoteTable.PhraseFor(t);
                    Write($"motif {t.Name}: {(motif == null ? "none" : string.Join(" ", motif))}");
                }
                foreach (SongInput i in System.Enum.GetValues(typeof(SongInput))) Write($"input {i} plays {CreatureNoteTable.EventFor(i)}");
                // Look works from the camera's old place, so aim it by the direction from the new one.
                Vector3 doorView = new Vector3(-68.6f, -74.3f, 169.3f);
                Place(body, cam, doorView, cam.transform.position + (new Vector3(-69.2f, -74.8f, 172.8f) - doorView - Vector3.up * 0.6f));
                Wait(1.5);
                break;
            case 1:
            {
                Vector3 eye = cam.transform.position;
                bool hit = Physics.Linecast(eye, new Vector3(-70f, -75.5f, 176f), out RaycastHit stop, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore);
                Write("way to the skeleton before the chord is stopped by: " + (hit ? stop.collider.name : "nothing"));
                Shot(cam, "33_chord_door_shut");
                Place(body, cam, hub, hub + Vector3.back * 8f);
                songGlim.transform.position = hub + new Vector3(0f, 0.7f, -8f);
                ((GlimLantern)songGlim).SetHome(songGlim.transform.position);
                Wait(1.2);
                break;
            }
            case 2:
                songGlim.transform.position = hub + new Vector3(0f, 0.7f, -8f);
                Look(body, cam, songGlim.AimPoint);
                Wait(0.6);
                break;
            case 3:
            {
                Look(body, cam, songGlim.AimPoint);
                Write($"facing the glim: target {(song.Target != null ? song.Target.GetType().Name : "none")}, mouse taken {CreatureSong.ConsumesMouse}");
                Shot(cam, "30_phrase_above_creature");
                if (song.Target == null) Write("(no target: test presses go nowhere)");
                SongInput[] p = song.PhraseOf(songGlim);
                song.DebugPress(p[0]);
                Write("after one right input, progress " + song.Progress);
                song.DebugPress(p[0] == SongInput.W ? SongInput.S : SongInput.W);
                Write("after a wrong input, progress " + song.Progress);
                foreach (SongInput i in p) song.DebugPress(i);
                Write($"after the whole phrase: glim following {songGlim.Following}");
                if (!songGlim.Following) song.DebugFollow(songGlim);
                Wait(3);
                break;
            }
            case 4:
            {
                Vector3 to = songGlim.transform.position - body.position;
                Write($"melody leads: {to.magnitude:0.0} m away, {Vector3.Dot(to, cam.transform.forward):0.0} m ahead of the view");
                Physics.Raycast(hub + Vector3.right * 4f, Vector3.down, out RaycastHit floor, 6f, DarkTunnels.TerrainMask);
                ((LureMaw)songMaw).SetDen(floor.point, Vector3.forward);
                ((BellSnail)songSnail).Attach(floor.point + Vector3.back * 3f, floor.normal);
                song.DebugFollow(songMaw);
                song.DebugFollow(songSnail);
                Wait(4);
                break;
            }
            case 5:
                Write($"full chord {CreatureSong.FullChord}, followers {song.FollowerCount}; maw {Vector3.Distance(songMaw.transform.position, body.position):0.0} m, snail {Vector3.Distance(songSnail.transform.position, body.position):0.0} m from the player");
                Write($"music layers: glim {AudioManager.GetLayerWeight("BGM_glim"):0.00} bell {AudioManager.GetLayerWeight("BGM_bell"):0.00} maw {AudioManager.GetLayerWeight("BGM_maw"):0.00}");
                Shot(cam, "31_three_following");
                Place(body, cam, new Vector3(-73f, -71.5f, 165f), new Vector3(-69.2f, -74.8f, 172.8f));
                Wait(4.5);
                break;
            case 6:
            {
                var door = Object.FindFirstObjectByType<ChordDoor>();
                Write("chord door open: " + (door != null && door.Open) + ", at y " + (door != null ? door.transform.position.y.ToString("0.0") : "-"));
                Shot(cam, "32_chord_door");
                NoteSeed melodySeed = null;
                foreach (var s in Object.FindObjectsByType<NoteSeed>(FindObjectsSortMode.None))
                    if (s.Note == CaveNote.E && !Physics.Raycast(s.transform.position + Vector3.up * 0.3f, Vector3.up, 2.5f, DarkTunnels.TerrainMask)) { melodySeed = s; break; }
                seedTest = melodySeed;
                if (melodySeed != null)
                {
                    Place(body, cam, melodySeed.transform.position + new Vector3(0.5f, 1.2f, 0.3f), melodySeed.transform.position);
                    songGlim.transform.position = melodySeed.transform.position + Vector3.up * 2f;
                }
                Wait(4);
                break;
            }
            case 7:
            {
                Write("melody seeds held after bringing the glim to a pod: " + song.SeedCount(SongRole.Melody));
                ToneCrate light = null;
                foreach (var m in Object.FindObjectsByType<ToneCrate>(FindObjectsSortMode.None))
                    if (m.SeedsNeeded == 0 && m.transform.position.y < -60f) { light = m; break; }
                conducted = light;
                conductedFrom = light.transform.position;
                Vector3 stand = light.transform.position + Vector3.up * 2f;
                foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    for (float reach = 16f; reach >= 10f; reach -= 2f)
                    {
                        Vector3 spot = light.transform.position + side * reach + Vector3.up * 1.2f;
                        if (Physics.CheckSphere(spot, 0.7f, DarkTunnels.TerrainMask) || !Physics.Raycast(spot, Vector3.down, 4f, DarkTunnels.TerrainMask)) continue;
                        if (Physics.SphereCast(spot + Vector3.down * 0.2f, 0.7f, -side, out _, reach - 5f, DarkTunnels.TerrainMask)) continue;
                        stand = spot; goto found;
                    }
                found:
                Place(body, cam, stand, light.transform.position);
                Write($"mesa starts {Vector3.Distance(stand, conductedFrom):0.0} m from the player");
                song.DebugFollow(light);
                Wait(6);
                break;
            }
            case 8:
                Write($"mesa following: moved {Vector3.Distance(conducted.transform.position, conductedFrom):0.0} m, now {Vector3.Distance(conducted.transform.position, body.position):0.0} m from the player");
                songFuzz = Object.FindFirstObjectByType<FuzzCreature>();
                songTentacle = Object.FindFirstObjectByType<TentacleCreature>();
                foreach (SongMotion m in System.Enum.GetValues(typeof(SongMotion)))
                {
                    var names = new System.Collections.Generic.SortedSet<string>();
                    foreach (var c in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
                        if (CreatureNoteTable.MotionFor(c.GetType()) == m) names.Add(c.GetType().Name);
                    Write($"{m} in this scene: {string.Join(", ", names)}");
                }
                Write($"fuzz found {songFuzz != null}, tentacle found {songTentacle != null}");
                Place(body, cam, hub + Vector3.right * 6f, hub + Vector3.right * 6f + Vector3.back * 9f);
                if (songFuzz == null) { var go = new GameObject("Test fuzz"); songFuzz = go.AddComponent<FuzzCreature>(); }
                if (songTentacle == null) { var go = new GameObject("Test tentacle"); songTentacle = go.AddComponent<TentacleCreature>(); }
                PutOnFloor(songTentacle, hub + Vector3.right * 6f + Vector3.back * 9f, 1f);
                PutOnFloor(songFuzz, hub + Vector3.right * 2f + Vector3.back * 9f, 0.3f);
                Wait(1);
                break;
            case 9:
                Look(body, cam, songTentacle.transform.position);
                Wait(0.5);
                break;
            case 10:
            {
                Write($"tentacle: role {songTentacle.GetType().Name} {CreatureNoteTable.RoleFor(songTentacle.GetType())}, phrase {string.Join(" ", song.PhraseOf(songTentacle))}; facing {(song.Target != null ? song.Target.GetType().Name : "none")}, mouse taken {CreatureSong.ConsumesMouse}");
                Shot(cam, "34_tentacle_phrase");
                {
                    Vector3 aim = songTentacle.AimPosition, eye = cam.transform.position;
                    bool blocked = Physics.Linecast(eye, aim, out RaycastHit why, DarkTunnels.TerrainMask, QueryTriggerInteraction.Ignore);
                    Write($"tentacle check: {Vector3.Distance(eye, aim):0.0} m, {Vector3.Angle(cam.transform.forward, aim - eye):0.0} deg off the view, blocked by {(blocked ? why.collider.name : "nothing")}, dead {songTentacle.IsDead}, carried {songTentacle.IsCarried}, physical {songTentacle.IsInPhysicalWorld}, captured {songTentacle.IsBubbleCaptured}, active {songTentacle.isActiveAndEnabled}, motion {CreatureNoteTable.MotionFor(songTentacle.GetType())}");
                }
                if (song.Target == songTentacle) foreach (SongInput i in song.PhraseOf(songTentacle)) song.DebugPress(i);
                Write("tentacle following after its phrase: " + CreatureSong.IsFollowing(songTentacle));
                if (!CreatureSong.IsFollowing(songTentacle)) song.DebugFollow(songTentacle);
                songFrom = songTentacle.transform.position;
                Look(body, cam, songFuzz.transform.position);
                Wait(0.5);
                break;
            }
            case 11:
            {
                Write($"fuzz: role {CreatureNoteTable.RoleFor(typeof(FuzzCreature))}, phrase {string.Join(" ", song.PhraseOf(songFuzz))}; facing {(song.Target != null ? song.Target.GetType().Name : "none")}");
                if (song.Target == songFuzz) foreach (SongInput i in song.PhraseOf(songFuzz)) song.DebugPress(i);
                Write("fuzz following after its phrase: " + CreatureSong.IsFollowing(songFuzz));
                Write($"one per kind: drums now fuzz {CreatureSong.IsFollowing(songFuzz)}, maw {CreatureSong.IsFollowing(songMaw)}, mesa {CreatureSong.IsFollowing(conducted)}; melody glim {CreatureSong.IsFollowing(songGlim)}; followers {song.FollowerCount}");
                if (!CreatureSong.IsFollowing(songFuzz)) song.DebugFollow(songFuzz);
                Wait(5);
                break;
            }
            case 12:
                Write($"after 5 s: tentacle moved {Vector3.Distance(songTentacle.transform.position, songFrom):0.0} m, {Vector3.Distance(songTentacle.transform.position, body.position):0.0} m from the player; fuzz {Vector3.Distance(songFuzz.transform.position, body.position):0.0} m from the player");
                Shot(cam, "35_all_creatures_following");
                foreach (var c in new Creature[] { songGlim, songMaw, songSnail, songTentacle, songFuzz }) if (c != null) c.gameObject.SetActive(false);
                songLakes = Object.FindObjectsByType<HangingLake>(FindObjectsSortMode.None);
                Write($"hanging lakes {songLakes.Length}, with mirrors {Object.FindObjectsByType<MirrorPool>(FindObjectsSortMode.None).Length}");
                StandUnderLake(body, cam, songLakes[0]);
                Wait(4);
                break;
            case 13:
                Write($"mirror renders {MirrorPool.DebugRenders}, supported {MirrorPool.DebugSupported}, drops falling in the air: {Object.FindObjectsByType<WaterDrips>(FindObjectsSortMode.None).Length} drippers");
                Shot(cam, "36_ceiling_mirror");
                if (songLakes.Length > 0) StandUnderLake(body, cam, songLakes[0]);
                Wait(3);
                break;
            case 14:
                Shot(cam, "37_volume_water");
                if (songLakes.Length > 1) StandUnderLake(body, cam, songLakes[1]);
                Wait(3);
                break;
            case 15:
                Shot(cam, "38_volume_water_2");
                Write("done");
                Finish("test done");
                break;
        }
    }

    private static FuzzCreature songFuzz;
    private static TentacleCreature songTentacle;
    private static Vector3 songFrom;

    private static HangingLake[] songLakes;

    private static void StandUnderLake(Rigidbody body, Camera cam, HangingLake lake)
    {
        Bounds b = lake.GetComponent<MeshRenderer>().bounds;
        Vector3 point = new Vector3(b.center.x, b.min.y, b.center.z);
        Vector3 stand = point + Vector3.down * 3f;
        foreach (float reach in new[] { 0.55f, 0.3f, 0.8f })
            foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 from = point + side * b.extents.x * reach + Vector3.down * 0.6f;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit floor, 25f, DarkTunnels.TerrainMask)) continue;
                Vector3 at = floor.point + Vector3.up * 1.1f;
                if (Physics.Linecast(at + Vector3.up * 0.6f, point + Vector3.down * 0.2f, DarkTunnels.TerrainMask)) continue;
                stand = at;
                goto chosen;
            }
        chosen:
        Place(body, cam, stand, cam.transform.position + (point - (stand + Vector3.up * 0.6f)));
    }

    private static void PutOnFloor(Creature c, Vector3 near, float above)
    {
        Vector3 at = near;
        if (Physics.Raycast(near + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 10f, DarkTunnels.TerrainMask)) at = floor.point + Vector3.up * above;
        c.transform.position = at;
        if (c.TryGetComponent<Rigidbody>(out var r)) { r.position = at; if (!r.isKinematic) r.linearVelocity = Vector3.zero; }
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
