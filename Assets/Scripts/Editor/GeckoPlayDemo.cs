using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Disposable Play Mode demonstration using the real player, WASD input and crosshair interaction.</summary>
[InitializeOnLoad]
public static class GeckoPlayDemo
{
    const string KeyName = "GeckoPlayDemo.Active";
    const string Output = "ArtCaptures/GeckoPlayDemo";
    static IEnumerator routine;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame;
    static float started;
    static SmoothFirstPersonController player;
    static PlayerCreatureInteractor interactor;
    static Keyboard demoKeyboard;
    static Mouse demoMouse;
    static readonly List<string> log = new List<string>();
    static readonly Vector3 origin = new Vector3(4, 100, -177);
    static GeckoPlayDemo() { EditorApplication.update += Tick; }
    [MenuItem("Tools/Capstone/Gecko/Play Capture Demonstration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play Mode first.");
        SessionState.SetBool(KeyName, true);
        SessionState.SetBool(KeyName + ".WebOnly", false);
        EditorApplication.EnterPlaymode();
    }
    [MenuItem("Tools/Capstone/Gecko/Play Web Rescue Demonstration")]
    public static void RunWeb()
    {
        Run(); SessionState.SetBool(KeyName + ".WebOnly", true);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(KeyName, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || Time.timeSinceLevelLoad < 4 || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (routine == null) { log.Clear(); Directory.CreateDirectory(Output); started = Time.realtimeSinceStartup; routine = Execute(); steps.Clear(); steps.Push(routine); }
            if (Time.realtimeSinceStartup - started > 100) throw new Exception("Demo timeout");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                break;
            }
            if (steps.Count == 0) Finish();
        }
        catch (Exception e) { Note("ERROR " + e); Finish(); }
    }
    static void Finish()
    {
        Keys();
        while (steps.Count > 0) (steps.Pop() as IDisposable)?.Dispose();
        if (demoKeyboard != null) InputSystem.RemoveDevice(demoKeyboard);
        if (demoMouse != null) InputSystem.RemoveDevice(demoMouse);
        demoKeyboard = null; demoMouse = null;
        Note("Demo finished; exiting Play Mode removes temporary actors and restores the authored scene. No scene saved.");
        SessionState.SetBool(KeyName, false); routine = null; EditorApplication.ExitPlaymode();
    }
    static void Note(string text) { log.Add(text); File.WriteAllLines(Output + "/Experience.txt", log); }
    static void Require(bool condition, string description) => Note((condition ? "PASS " : "FAIL ") + description);
    static void Keys(params Key[] keys)
    {
        if (demoKeyboard != null)
        {
            demoKeyboard.MakeCurrent();
            InputSystem.QueueStateEvent(demoKeyboard, new KeyboardState(keys));
        }
        if (demoMouse != null)
        {
            demoMouse.MakeCurrent();
            InputSystem.QueueStateEvent(demoMouse, new MouseState());
        }
    }
    static void Place(Vector3 at)
    {
        Keys();
        foreach (var hand in Object.FindObjectsByType<HandClimber>(FindObjectsSortMode.None))
            if (hand.Controls(player)) hand.StopForBubbleCapture();
        player.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
        player.Body.position = at; player.transform.position = at; player.Body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    static void Aim(Vector3 at)
    {
        Vector3 d = at - player.PlayerCamera.transform.position;
        player.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
    }
    static void Click(Vector3 at)
    {
        Aim(at); Physics.SyncTransforms();
        var target = interactor.FindCreature();
        Note("Crosshair click: " + (target != null ? target.name : "no creature"));
        interactor.TryInteract();
    }
    static SkateGecko Spawn(Vector3 at)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<SkateGecko>(SkateGeckoSetup.PrefabPath);
        var g = Object.Instantiate(prefab, at, Quaternion.identity); g.name = "TEMP demo gecko"; return g;
    }
    static Vector3 Target(SkateGecko g) => g.GetComponent<Collider>().bounds.center;
    static IEnumerator RideRoute(SkateGecko gecko, string route)
    {
        float end = Time.time + 3;
        while (!gecko.Ridden && Time.time < end)
        {
            if (route == "WEB" && Vector3.Distance(gecko.GetComponent<Collider>().ClosestPoint(player.Body.position),player.Body.position)>2f)
                Place(new Vector3(Target(gecko).x,origin.y+1f,Target(gecko).z-1.5f));
            Aim(Target(gecko)); interactor.TryInteract();
            if (gecko.Ridden) Note($"{route} mount accepted immediately: pos={gecko.transform.position}, player={player.transform.position}, drive={player.GetComponent<PlayerSkateShoes>().CanDrive}, state={player.State}.");
            if (!gecko.Ridden && (route != "WEB" || (player.Grounded && !player.Climbing && Mathf.Abs(player.transform.position.y-gecko.transform.position.y)<1.8f))) Keys(Key.W); else Keys();
            yield return null;
        }
        Keys(); Note($"{route} MOUNT: ridden={gecko.Ridden}, tame={gecko.Captured}, grounded={player.Grounded}.");
        Require(gecko.Ridden, route + " crosshair mount");
        if (!gecko.Ridden)
        {
            var pin = gecko.GetComponent<WebCapture>();
            Note($"FAIL {route} could not mount: sim={gecko.SimulationEnabled}, webbed={pin!=null&&pin.IsCaptured}, sees={gecko.Sees(player)}, distance={Vector3.Distance(gecko.GetComponent<Collider>().ClosestPoint(player.transform.position),player.transform.position):F2}, player={player.transform.position}, gecko={gecko.transform.position}, climbing={player.Climbing}, restricted={player.Restricted}, blocked={player.BubbleMovementBlocked}.");
            yield break;
        }
        end = Time.time + 1.5f;
        while (Time.time < end) { Keys(Key.W); yield return null; }
        Keys(); Note($"{route} GLIDE: ridden={gecko.Ridden}, speed={player.CurrentVelocity:F2}m/s.");
        Require(gecko.Ridden && player.CurrentVelocity > 1, route + " sustained forward riding");
        Shot(route + "_Ride", Target(gecko), true);
        if (route == "WEB") Note($"WEB ride ownership: freeOfWeb={gecko.GetComponent<WebCapture>()!=null&&!gecko.GetComponent<WebCapture>().IsCaptured}, autonomous={gecko.SimulationEnabled}.");
        Vector3 brakingDirection = Vector3.ProjectOnPlane(player.Body.linearVelocity,Vector3.up).normalized;
        end = Time.time + 1.2f;
        while (Time.time < end && Vector3.Dot(player.Body.linearVelocity,brakingDirection) > .2f) { Keys(Key.S); yield return null; }
        Note($"{route} brake input: S={Keyboard.current.sKey.isPressed}, W={Keyboard.current.wKey.isPressed}, controllerVertical={typeof(SmoothFirstPersonController).GetField("verticalInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(player)}, grounded={player.Grounded}, airborne={gecko.Airborne}, drive={player.GetComponent<PlayerSkateShoes>().CanDrive}.");
        Keys(); Note($"{route} BRAKE: ridden={gecko.Ridden}, speed={player.Body.linearVelocity.magnitude:F2}m/s.");
        Require(gecko.Ridden && player.Body.linearVelocity.magnitude < 1, route + " ground braking");
        gecko.Dismount();
        end = Time.time + .4f;
        while (Time.time < end) yield return null;
        Note($"{route} DISMOUNT: independent={!gecko.Ridden&&!gecko.GetComponent<Rigidbody>().isKinematic}, tame={gecko.Captured}.");
        Require(!gecko.Ridden && !gecko.GetComponent<Rigidbody>().isKinematic && gecko.Captured, route + " dismount restores independent board");
        end = Time.time + 2;
        while (!gecko.Ridden && Time.time < end) { Aim(Target(gecko)); interactor.TryInteract(); yield return null; }
        Note($"{route} REMOUNT: ridden={gecko.Ridden}.");
        Require(gecko.Ridden, route + " crosshair remount");
        if (!gecko.Ridden) { Note("FAIL " + route + " could not remount"); yield break; }
        end = Time.time + .6f;
        while (Time.time < end) { Keys(Key.W); yield return null; }
        end = Time.time + .25f;
        while (Time.time < end) { Keys(Key.W, Key.Space); yield return null; }
        Note($"{route} JUMP: ridden={gecko.Ridden}, airborne={gecko.Airborne}.");
        Require(gecko.Ridden && gecko.Airborne, route + " straight jump stays mounted");
        end = Time.time + 3;
        while (gecko.Ridden && gecko.Airborne && Time.time < end) { Keys(Key.W); yield return null; }
        Require(gecko.Ridden && !gecko.Airborne, route + " straight landing resumes supported riding");
        end = Time.time + .25f;
        while (Time.time < end) { Keys(Key.W,Key.Space); yield return null; }
        Require(gecko.Ridden && gecko.Airborne, route + " can jump again after landing");
        end = Time.time + .15f;
        while (Time.time < end) { Keys(Key.D); yield return null; }
        Note($"{route} turn input: D={Keyboard.current.dKey.isPressed}, controllerHorizontal={typeof(SmoothFirstPersonController).GetField("horizontalInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(player)}, grounded={player.Grounded}, airborne={gecko.Airborne}.");
        Keys(); Note($"{route} AIR TURN: separated={!gecko.Ridden}, playerFlung={player.Flung}.");
        Require(!gecko.Ridden && player.Flung, route + " abrupt air turn separates rider and board");
    }
    static void Shot(string name, Vector3 focus, bool overview = false)
    {
        Camera cam = player.PlayerCamera;
        GameObject temporary = null;
        if (overview)
        {
            temporary = new GameObject("TEMP demonstration observation camera");
            cam = temporary.AddComponent<Camera>(); cam.CopyFrom(player.PlayerCamera);
            cam.transform.position = focus + new Vector3(5, 3, -6); cam.transform.LookAt(focus);
            cam.fieldOfView = 55;
        }
        var old = cam.targetTexture;
        var rt = RenderTexture.GetTemporary(1280, 720, 24);
        var active = RenderTexture.active;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(texture); if (temporary != null) Object.Destroy(temporary);
        }
    }
    static IEnumerator Execute()
    {
        demoKeyboard = InputSystem.AddDevice<Keyboard>("Gecko demonstration keyboard");
        demoMouse = InputSystem.AddDevice<Mouse>("Gecko demonstration mouse");
        Keys();
        player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        if (player == null) throw new Exception("No live player");
        interactor = player.GetComponentInChildren<PlayerCreatureInteractor>();
        if (interactor == null) interactor = player.transform.root.GetComponentInChildren<PlayerCreatureInteractor>();
        if (interactor == null) throw new Exception("No player interactor");
        Note("Assisted live Play Mode demonstration: real player controller, queued W/Shift keyboard state, SetLook assisted aim, TryInteract crosshair click entry point. No direct gecko TryCatch or bubble TryCapture calls. Temporary raised platform separates the demonstration from other creatures; normal sizes/speeds/capture rules retained.");
        var flowerSource = Object.FindFirstObjectByType<BubbleFlower>();
        var spiderSource = Object.FindFirstObjectByType<SkySpider>();
        if (flowerSource == null || spiderSource == null) throw new Exception("World source flower/spider missing");
        var flower = Object.Instantiate(flowerSource, origin + new Vector3(15, .3f, 0), Quaternion.identity);
        flower.AmbientEmissionAllowed = false; flower.gameObject.SetActive(true);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "TEMP demonstration floor";
        floor.layer = 7; floor.transform.position = origin + Vector3.down * .25f; floor.transform.localScale = new Vector3(160, .5f, 160);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color = new Color(.12f, .16f, .13f); floor.GetComponent<Renderer>().sharedMaterial = mat;
        Place(origin + Vector3.up * 1.2f);
        var carrier = player.GetComponent<PlayerCreatureCarrier>(); if (carrier != null) carrier.Release();
        float end = Time.time + 1;
        while (Time.time < end) yield return null;
        SkateGecko gecko;
        if (!SessionState.GetBool(KeyName + ".WebOnly", false))
        {
        gecko = Spawn(origin + new Vector3(0, .27f, 7));
        float first = Vector3.Distance(player.transform.position, gecko.transform.position), maxSpeed = 0;
        end = Time.time + 4;
        while (Time.time < end)
        {
            Aim(Target(gecko)); Keys(Key.W, Key.LeftShift); interactor.TryInteract();
            maxSpeed = Mathf.Max(maxSpeed, gecko.GetComponent<Rigidbody>().linearVelocity.magnitude);
            yield return null;
        }
        Keys();
        Note($"HAND long chase: initial centre distance {first:F2}m, final {Vector3.Distance(player.transform.position,gecko.transform.position):F2}m, max gecko speed {maxSpeed:F2}m/s, caught={gecko.Captured}, player={player.CurrentVelocity:F2}m/s.");
        Shot("01_HandChase", Target(gecko));
        Object.Destroy(gecko.gameObject);
        Place(origin + Vector3.up * 1.2f);
        gecko = Spawn(origin + new Vector3(0, .27f, 1.0f));
        yield return null; Aim(Target(gecko)); yield return null;
        Note($"HAND pre-click: sees={gecko.Sees(player)}, distance={Vector3.Distance(gecko.GetComponent<Collider>().ClosestPoint(player.transform.position),player.transform.position):F2}, sim={gecko.SimulationEnabled}, restricted={player.Restricted}, bubbleBlocked={player.BubbleMovementBlocked}, climbing={player.Climbing}, wallrunning={player.WallRunning}, boneBusy={BoneRide.Busy}, cutscene={BoneAwakeningCutscene.Playing}, sourcePlayer={interactor.PlayerController==player}.");
        Click(Target(gecko));
        Note($"HAND close interception: caught={gecko.Captured}, grounded={player.Grounded}.");
        Shot("02_HandCatch", Target(gecko), true);
        yield return RideRoute(gecko, "HAND");
        gecko.Dismount(); Object.Destroy(gecko.gameObject);
        }

        Place(origin + new Vector3(15, 1.2f, -8));
        var spider = Object.Instantiate(spiderSource, origin + new Vector3(15, 3, 0), Quaternion.identity);
        spider.gameObject.SetActive(true); spider.enabled = true; spider.SetSimulationEnabled(true);
        end = Time.time + 5;
        while (spider.CurrentWeb == null && Time.time < end) yield return null;
        if (spider.CurrentWeb == null) throw new Exception("No live web");
        var web = spider.CurrentWeb; spider.enabled = false; web.AdvanceGrowth(60);
        spider.GetComponent<Rigidbody>().position = origin + new Vector3(35, 3, 0);
        spider.transform.position = origin + new Vector3(35, 3, 0);
        Note("WEB builder parked away from test contact to isolate silk rescue from predator contact/feeding.");
        var strand = web.Geometry.GetStrand(20);
        Vector3 contact = web.transform.position + (strand.Start + strand.End) * .5f;
        gecko = Spawn(contact + Vector3.up * 2);
        gecko.GetComponent<Rigidbody>().linearVelocity = Vector3.down * 10;
        end = Time.time + 3;
        while ((gecko.GetComponent<WebCapture>() == null || !gecko.GetComponent<WebCapture>().IsCaptured) && Time.time < end) yield return null;
        var capture = gecko.GetComponent<WebCapture>();
        Note($"WEB incoming actual collision: trapped={capture!=null&&capture.IsCaptured}, autonomous={gecko.SimulationEnabled}.");
        Click(Target(gecko));
        Note($"WEB distant click leaves captive unchanged: trapped={capture!=null&&capture.IsCaptured}, tame={gecko.Captured}.");
        Vector3 pinned = gecko.transform.position;
        Shot("04_WebCapture", Target(gecko), true);
        Place(Target(gecko) + Vector3.back * 2 + Vector3.up * .7f);
        end = Time.time + 2;
        while (Time.time < end) yield return null;
        Note($"WEB two-second drift={Vector3.Distance(pinned,gecko.transform.position):F4}m.");
        yield return null; Aim(Target(gecko)); yield return null; Click(Target(gecko));
        Note($"WEB player click while pinned: caught={gecko.Captured}; autonomous={gecko.SimulationEnabled}; freed={capture!=null&&!capture.IsCaptured}; remainingWebCaptives={web.CaptureCount}.");
        Shot("05_WebApproach", Target(gecko));
        // Stage the next approach beside the released animal, rather than walking
        // the assisted straight-line driver up the climbable web above its head.
        end = Time.time + 3f;
        while (gecko.transform.position.y > origin.y + .5f && Time.time < end) yield return null;
        Place(new Vector3(Target(gecko).x, origin.y + 1f, Target(gecko).z - 1.5f));
        player.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
        Note("WEB staged player beside rescued animal for mount; animal pose/velocity and original web preserved.");
        yield return null;
        // Keep the real web alive: rescue and riding must work without removing it.
        yield return RideRoute(gecko, "WEB");
        var rescueField = typeof(WebCapture).GetField("rescuePairs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        end = Time.time + 2;
        while (((ICollection)rescueField.GetValue(capture)).Count > 0 && Time.time < end) yield return null;
        Require(((ICollection)rescueField.GetValue(capture)).Count == 0,"WEB rescue collision grace expires without leaving ignored pairs");
        Note($"WEB after separation: webbedAgain={capture!=null&&capture.IsCaptured}; original silk collision override ended={web.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger).All(c=>!Physics.GetIgnoreCollision(gecko.GetComponent<Collider>(),c))}. A detached board may legitimately be caught again.");
        Object.Destroy(gecko.gameObject); Object.Destroy(web.gameObject); Object.Destroy(spider.gameObject);
        if (SessionState.GetBool(KeyName + ".WebOnly", false)) yield break;

        Place(origin + new Vector3(-15, 1.2f, 0));
        flower.transform.position = player.transform.position + Vector3.forward;
        bool pickup = carrier.TryCarryFromInteraction(flower);
        end = Time.time + .7f;
        while (Time.time < end) yield return null;
        gecko = Spawn(origin + new Vector3(-15, .27f, 3));
        yield return null; Aim(Target(gecko)); yield return null;
        Note($"BUBBLE picked up normal flower: requested={pickup}, held={carrier.CarriedCreature==flower}.");
        Click(Target(gecko));
        end = Time.time + 3;
        while (!gecko.IsBubbleCaptured && Time.time < end)
        {
            Aim(Target(gecko) + gecko.GetComponent<Rigidbody>().linearVelocity * .15f);
            Keys(Key.W, Key.LeftShift);
            if (flower.LastShot == null || Time.time % .65f < Time.deltaTime) interactor.TryInteract();
            yield return null;
        }
        Keys();
        var bubble = gecko.IsBubbleCaptured ? gecko.GetComponent<BubbleCapture>().OwningBubble : flower.LastShot;
        Note($"BUBBLE real held-flower aimed shot: emitted={bubble!=null}, trapped={gecko.IsBubbleCaptured}, handTamed={gecko.Captured}.");
        Shot("06_BubbleShot", Target(gecko), true);
        carrier.Release();
        // Put the flower aside instead of dropping it directly onto the occupied shell.
        flower.transform.position = player.transform.position + Vector3.back * 5;
        flower.GetComponent<Rigidbody>().position = flower.transform.position;
        if (bubble != null && !bubble.IsPopped && gecko.IsBubbleCaptured)
        {
            Place(bubble.transform.position + Vector3.back * (bubble.Radius + 1.5f));
            yield return null;
            if (bubble == null || bubble.IsPopped) { Note("BUBBLE popped on approach before the explicit click."); yield break; }
            Aim(bubble.transform.position); yield return null;
            if (bubble == null || bubble.IsPopped) { Note("BUBBLE popped on approach before the explicit click."); yield break; }
            Click(bubble.transform.position);
            Note($"BUBBLE crosshair pop: popped={bubble.IsPopped}, animalReleased={!gecko.IsBubbleCaptured}.");
            end = Time.time + 1;
            while (Time.time < end)
            {
                Aim(Target(gecko)); Keys(Key.W, Key.LeftShift); interactor.TryInteract(); yield return null;
            }
            Keys(); Note($"BUBBLE follow-up catch within one second: handTamed={gecko.Captured}, airborne={gecko.transform.position.y-origin.y:F2}m above floor.");
            Shot("07_BubbleReleased", Target(gecko), true);
            yield return RideRoute(gecko, "BUBBLE");
        }
        Object.Destroy(gecko.gameObject);
        yield return null;
    }
}
