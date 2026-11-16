using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated physics scene: never advances or removes the user's scene objects.</summary>
[InitializeOnLoad]
public static class SkateGeckoValidation
{
    const string Key = "SkateGecko.Check";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static SkateGeckoValidation() { EditorApplication.update += Poll; }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static void Call(object o, string name) => o.GetType().GetMethod(name, Private).Invoke(o, null);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || SkateGeckoSetup.IsBuilding) return;
        if (File.Exists("Temp/SkateGecko.check") && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete("Temp/SkateGecko.check"); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < 1) return;
        SessionState.SetBool(Key, false); Run(); EditorApplication.ExitPlaymode();
    }
    static void Run()
    {
        Scene scene = default; checks = 0;
        try
        {
            scene = SceneManager.CreateScene("Disposable gecko physics checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject Make(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
            var physics = scene.GetPhysicsScene();
            var floor = Make("Ground"); floor.layer = 7; floor.transform.position = new Vector3(0, -.25f, 0);
            floor.AddComponent<BoxCollider>().size = new Vector3(200, .5f, 200);
            var rig = Make("Player"); rig.transform.position = new Vector3(0, 1.02f, 0);
            rig.AddComponent<Rigidbody>(); var capsule = rig.AddComponent<CapsuleCollider>(); capsule.height = 2; capsule.radius = .35f;
            var player = rig.AddComponent<SmoothFirstPersonController>();
            var facing = Make("Orientation"); facing.transform.SetParent(rig.transform, false); Set(player, "orientation", facing.transform); Call(player, "Start"); Set(player, "grounded", true);
            var animal = Make("Gecko"); animal.transform.position = new Vector3(0, .26f, 8); var gecko = animal.AddComponent<SkateGecko>();
            var body = animal.GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.None; Physics.SyncTransforms();
            Check(gecko.Sees(player), "Gecko should see nearby player.");
            var wall = Make("Sight blocker"); wall.transform.position = new Vector3(0, 1, 4); wall.AddComponent<BoxCollider>().size = new Vector3(4, 3, .5f); Physics.SyncTransforms();
            Check(!gecko.Sees(player), "Wall must block gecko sight."); Object.DestroyImmediate(wall); Physics.SyncTransforms();
            Set(gecko, "observer", player);
            for (int i = 0; i < 75; i++) { Call(gecko, "FixedUpdate"); physics.Simulate(.02f); }
            float flee = body.linearVelocity.magnitude;
            Check(flee > 12 && body.position.z > 12, "Gecko should accelerate and flee at skating speed: " + flee);
            Check(!gecko.TryCatch(player), "Cannot catch gecko remotely.");
            animal.transform.position = new Vector3(0, .26f, 1.5f); body.position = animal.transform.position; body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            Check(gecko.TryCatch(player) && gecko.Captured, $"Close interaction catches gecko. player={rig.transform.position} gecko={body.position} sees={gecko.Sees(player)} restricted={player.Restricted} bubble={player.BubbleMovementBlocked}");
            Check(!gecko.CanBeCarried, "Large gecko must not enter ordinary hand carrying.");
            Call(player, "StateHandler");
            Check(gecko.TryMount(player) && gecko.Ridden, "Caught gecko can be ridden.");
            var drive = rig.GetComponent<PlayerSkateShoes>(); Check(drive.Gecko == gecko && drive.CanDrive, $"Gecko uses existing skate movement owner: equipped={drive.Equipped}, state={player.State}, kinematic={player.Body.isKinematic}, restricted={player.Restricted}, flung={player.Flung}, climbing={player.Climbing}, wall={player.WallRunning}, bubble={player.BubbleMovementBlocked}, controller={typeof(PlayerSkateShoes).GetField("controller", Private).GetValue(drive)}.");
            physics.Raycast(new Vector3(0, 2, 0), Vector3.down, out var ground, 4, 1 << 7, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < 75; i++) { drive.Move(Vector3.forward, 10, true, ground, Vector3.zero, 50, .02f); physics.Simulate(.02f); Call(gecko, "FixedUpdate"); }
            float cruise = player.Body.linearVelocity.magnitude;
            Check(cruise > 15, "Riding retains skating cruise speed: " + cruise);
            drive.Move(Vector3.zero, 10, true, ground, Vector3.zero, 50, .02f);
            Check(player.Body.linearVelocity.magnitude > cruise - .1f, "Release input should coast.");
            player.Body.position += Vector3.up * 4; player.Body.linearVelocity = new Vector3(0, 5, 17); Physics.SyncTransforms();
            Check(gecko.UpdateRide(Vector3.forward, false, .02f) && gecko.Airborne && !body.isKinematic, "Airborne gecko must become a separate dynamic body.");
            Set(player, "skateShoes", drive);
            for (int i = 0; i < 10; i++)
            {
                drive.Move(Vector3.zero, 10, false, default, Vector3.zero, 50, .02f);
                Call(player, "ExtraGravity"); physics.Simulate(.02f);
            }
            Check(gecko.Ridden && Vector3.Distance(player.Body.linearVelocity, body.linearVelocity) < .02f,
                "Straight airborne travel keeps rider and board on the same ballistic trajectory.");
            Vector3 before = body.linearVelocity;
            Check(gecko.UpdateRide(Vector3.zero, false, .02f) && gecko.Ridden, "No input in air should not eject rider.");
            Check(!gecko.UpdateRide(Vector3.right, false, .02f) && !gecko.Ridden, "Sudden sideways input in air must separate rider and gecko.");
            Check(!drive.Equipped && drive.Gecko == null && Vector3.Distance(body.linearVelocity, before) < .01f, "Separation disables drive and preserves board momentum.");
            Check(Vector3.Distance(player.Body.linearVelocity, before) < .01f, "Rider keeps launch momentum on separation.");
            physics.Simulate(.1f);
            Check(body.linearVelocity.y < before.y && body.position.z > 1.5f, "Detached gecko flies ballistically under gravity.");
            rig.transform.position = new Vector3(0, 1.02f, 0); player.Body.position = rig.transform.position; player.Body.linearVelocity = Vector3.zero;
            animal.transform.position = new Vector3(0, .26f, 1.5f); body.position = animal.transform.position; body.linearVelocity = Vector3.zero;
            Set(player, "flungUntil", 0f); Set(player, "grounded", true); Call(player, "StateHandler"); Physics.SyncTransforms();
            Check(gecko.TryMount(player), "Gecko can be remounted after separating.");
            gecko.Dismount();
            Check(!gecko.Ridden && !drive.Equipped && !body.isKinematic && player.Body.useGravity, "Manual dismount restores independent physics and player gravity.");
            rig.transform.position = new Vector3(0, 1.02f, 0); player.Body.position = rig.transform.position; Physics.SyncTransforms();
            Check(gecko.TryMount(player), "Manual dismount does not prevent another ride.");
            player.SetRestricted(true); Call(gecko, "FixedUpdate");
            Check(!gecko.Ridden && !drive.Equipped, "Another movement owner safely ends the ride."); player.SetRestricted(false);
            // Start at full escape speed and keep the pursuer behind it for six seconds.
            // These cases used to run over straight edges and diagonally out of corners.
            Object.DestroyImmediate(animal);
            floor.GetComponent<BoxCollider>().size = new Vector3(128, .5f, 128);
            foreach (var outward in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
                new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1) })
            {
                var wild = Make("Boundary regression gecko"); wild.transform.position = outward * 56 + Vector3.up * .26f;
                var escape = wild.AddComponent<SkateGecko>(); var escapeBody = wild.GetComponent<Rigidbody>(); escapeBody.interpolation = RigidbodyInterpolation.None;
                escapeBody.linearVelocity = outward.normalized * 17; Set(escape, "observer", player); Physics.SyncTransforms();
                for (int step = 0; step < 300; step++)
                {
                    rig.transform.position = escapeBody.position - outward.normalized * 8 + Vector3.up;
                    player.Body.position = rig.transform.position; player.Body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
                    Call(escape, "FixedUpdate"); physics.Simulate(.02f);
                    Vector3 p = escapeBody.position;
                    if (Mathf.Abs(p.x) > 62.6f || Mathf.Abs(p.z) > 62.6f || p.y < .1f || escape.EdgeRecoveries != 0)
                        throw new Exception($"Boundary escape: direction={outward}, step={step}, position={p}, recoveries={escape.EdgeRecoveries}");
                }
                Check(true, "Full-speed pursuit remains within map edge/corner without recovery teleport."); Object.DestroyImmediate(wild);
            }
            File.WriteAllText("ArtCaptures/SquirrelForest/GeckoChecks.txt", $"PASS: {checks} isolated physics and interaction checks. Flee={flee:F2} m/s, cruise={cruise:F2} m/s.\nSight occlusion, close catch, mount, coast, separate airborne bodies, sharp-turn separation, momentum and gravity checked.\n");
            File.AppendAllText("ArtCaptures/SquirrelForest/GeckoChecks.txt", "Eight full-speed boundary/corner pursuits: six seconds each, body stays inside the 128 m map; zero recovery teleports.\n");
        }
        catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/GeckoChecks.txt", $"FAIL after {checks}: {e}"); Debug.LogException(e); }
        finally { if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene); }
    }
}
