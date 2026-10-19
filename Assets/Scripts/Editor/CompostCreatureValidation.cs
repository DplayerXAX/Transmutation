using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit compost checks using disposable existing-state creatures, without changing their death contract.</summary>
public static class CompostCreatureValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Compost validation: " + message);
        checks++;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Advance(CompostCreature compost, float seconds) => Call(compost, "AdvanceProcessing", seconds);
    private static void Charges(CompostCreature compost, long value) => Set(compost, "storedCharges", value);
    private static bool Claim(CompostCreature compost, Creature corpse) =>
        (bool)Call(compost, "TryBeginAbsorption", corpse, corpse.GetComponent<Collider>());

    public static string ValidateAssets()
    {
        checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompostCreatureSetup.PrefabPath);
        Check(prefab != null, "prefab exists");
        var compost = prefab.GetComponent<CompostCreature>();
        Check(compost != null && !compost.CanBeCarried, "compost creature is unportable");
        var ride = prefab.GetComponent<CompostRideSurface>();
        Check(ride != null && ride.Back == compost.Back, "player support references the actual back collider");
        Vector3 backSize = Vector3.Scale(ride.Back.size, ride.Back.transform.lossyScale);
        Check(Mathf.Approximately(backSize.x, 1f) && Mathf.Approximately(backSize.z, 1.8f), "flat back has player-scale dimensions");
        var fields = new SerializedObject(compost);
        Check(fields.FindProperty("gooPrefab").objectReferenceValue != null, "existing Goo Creature prefab assigned");
        Check(fields.FindProperty("legs").arraySize == 4, "four independent walking legs assigned");
        Check(fields.FindProperty("fullnessThreshold").intValue == 50 && Mathf.Approximately(fields.FindProperty("absorptionSeconds").floatValue, 5f) &&
            Mathf.Approximately(fields.FindProperty("productionSeconds").floatValue, 7f), "default fullness and timing match design");
        Check(prefab.GetComponent<Rigidbody>().isKinematic, "large creature uses controlled platform physics");
        return "PASS: " + checks + " compost asset checks.";
    }

    [MenuItem("Tools/Creatures/Compost/Validate Gameplay (Play Mode)")]
    public static void Validate() => Debug.Log(Run());

    [MenuItem("Tools/Creatures/Compost/Validate Live Riding (Play Mode)")]
    public static async void ValidateLiveRiding()
    {
        try { Debug.Log(await RunRideCheck()); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    public static async Task<string> RunRideCheck()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before live riding validation.");
        Scene scene = SceneManager.CreateScene("Compost Live Riding " + Guid.NewGuid());
        var stage = new GameObject("Live Riding Staging"); SceneManager.MoveGameObjectToScene(stage, scene); stage.SetActive(false);
        try
        {
            Vector3 origin = new Vector3(2000f, 0f, 2000f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(stage.transform, false); floor.layer = 7;
            floor.transform.position = origin - Vector3.up * 0.25f; floor.transform.localScale = new Vector3(40f, 0.5f, 40f);
            var turtle = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CompostCreatureSetup.PrefabPath), stage.transform);
            turtle.transform.position = origin;
            var compost = turtle.GetComponent<CompostCreature>();
            var rider = new GameObject("Live Rider"); rider.transform.SetParent(stage.transform, false);
            var movement = rider.AddComponent<SmoothFirstPersonController>(); rider.AddComponent<CapsuleCollider>().height = 2f;
            Set(movement, "orientation", rider.transform); Set(movement, "whatIsGround", (LayerMask)(1 << 7));
            Vector3 initialLocal = new Vector3(0f, 3.01f, -0.35f);
            rider.transform.position = origin + initialLocal;
            stage.SetActive(true);
            Set(compost, "destination", origin + Vector3.forward * 8f); Set(compost, "wanderTimer", 100f);
            Physics.SyncTransforms();
            float start = Time.time, deadline = Time.realtimeSinceStartup + 25f;
            bool turned = false;
            float maximumDrift = 0f;
            while (Time.time - start < 8f)
            {
                if (!Application.isPlaying || Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Live riding test stopped or timed out.");
                if (!turned && Time.time - start > 2f)
                {
                    Set(compost, "destination", origin + Vector3.right * 8f); Set(compost, "wanderTimer", 100f); turned = true;
                }
                if (Time.time - start > 0.2f)
                {
                    Vector3 local = turtle.transform.InverseTransformPoint(rider.transform.position);
                    maximumDrift = Mathf.Max(maximumDrift, Vector2.Distance(new Vector2(local.x, local.z), new Vector2(initialLocal.x, initialLocal.z)));
                    if (local.y < 2.85f || maximumDrift > 0.25f)
                        throw new InvalidOperationException("Live rider lost the walking back: local " + local + ", drift " + maximumDrift);
                }
                await Task.Yield();
            }
            if (Vector3.Distance(turtle.transform.position, origin) < 1f || Quaternion.Angle(turtle.transform.rotation, Quaternion.identity) < 20f)
                throw new InvalidOperationException("Live riding test did not include translation and turning.");
            var rb = rider.GetComponent<Rigidbody>();
            Call(movement, "Jump");
            float jumpStart = Time.time;
            while (Time.time - jumpStart < 0.2f) await Task.Yield();
            if (rider.transform.position.y < 3.15f || typeof(SmoothFirstPersonController).GetField("rideSurface", Private).GetValue(movement) != null)
                throw new InvalidOperationException("Live rider did not jump free of the back.");
            Set(movement, "ownJumpRising", false); Set(movement, "exitingSlope", false);
            rb.position = turtle.transform.TransformPoint(new Vector3(0f, 3.02f, 0f)); rb.linearVelocity = Vector3.zero;
            compost.Die();
            float deathStart = Time.time;
            while (Time.time - deathStart < 0.6f) await Task.Yield();
            if (Mathf.Abs(turtle.transform.InverseTransformPoint(rider.transform.position).y - 3f) > 0.1f || !movement.Grounded || rider.transform.parent != stage.transform)
                throw new InvalidOperationException("Dead back did not retain its unparented rider.");
            return "PASS: live riding translation, turning, jump, and dead-back support; maximum local drift " + maximumDrift.ToString("F3") + " m.";
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(scene);
            }
        }
    }

    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before compost gameplay validation.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("Compost Validation " + Guid.NewGuid());
        var stage = new GameObject("Compost Validation Staging");
        SceneManager.MoveGameObjectToScene(stage, scene);
        stage.SetActive(false);
        Vector3 origin = new Vector3(4000f, 0f, 4000f);
        GameObject floor = null;
        CompostCreature compost = null;
        try
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.name = "Validation Ground"; floor.layer = 7;
            floor.transform.position = origin - Vector3.up * 0.25f;
            floor.transform.localScale = new Vector3(30f, 0.5f, 30f);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CompostCreatureSetup.PrefabPath);
            var root = Object.Instantiate(asset, stage.transform);
            root.transform.position = origin;
            compost = root.GetComponent<CompostCreature>();
            Set(compost, "walkSpeed", 0f);
            var goo = (GooCreature)typeof(CompostCreature).GetField("gooPrefab", Private).GetValue(compost);
            Set(compost, "gooPrefab", null);
            stage.SetActive(true);
            Physics.SyncTransforms();

            CreatureLifecycleValidationProbe Corpse(float nutrition = 120f, float x = 0f, float z = 0f, bool dead = true)
            {
                var target = new GameObject("Compost Validation Body");
                target.transform.SetParent(stage.transform, false);
                target.transform.position = origin + new Vector3(x, 2.151f, z);
                target.AddComponent<Rigidbody>().useGravity = true;
                target.AddComponent<BoxCollider>().size = new Vector3(0.25f, 0.3f, 0.3f);
                var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(model.GetComponent<Collider>());
                model.transform.SetParent(target.transform, false);
                model.transform.localScale = new Vector3(0.25f, 0.3f, 0.3f);
                var probe = target.AddComponent<CreatureLifecycleValidationProbe>();
                using (var fields = new SerializedObject(probe))
                {
                    fields.FindProperty("nutrition").floatValue = nutrition;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                if (dead) probe.Die();
                Physics.SyncTransforms();
                return probe;
            }
            void Clear()
            {
                foreach (var probe in stage.GetComponentsInChildren<CreatureLifecycleValidationProbe>(true)) Object.DestroyImmediate(probe.gameObject);
                Set(compost, "gooPrefab", null); Set(compost, "producing", false); Set(compost, "productionTime", 0f);
                Charges(compost, 0);
                Advance(compost, 0.0001f);
                Physics.SyncTransforms();
            }

            Check(!compost.CanBeCarried && compost.StoredCharges == 0, "unportable creature starts without compost");
            Check(CompostCreature.ChargesForNutrition(0f) == 1 && CompostCreature.ChargesForNutrition(39f) == 1 &&
                CompostCreature.ChargesForNutrition(120f) == 6 && CompostCreature.ChargesForNutrition(1100f) == 55, "universal Nutrition conversion with integer minimum");
            var alive = Corpse(dead: false);
            Check(!Claim(compost, alive), "living creature is rejected");
            alive.SetSimulationEnabled(false);
            Check(!alive.IsDead && !Claim(compost, alive), "paused living creature is rejected");
            Clear();
            var side = Corpse(x: 0.7f);
            Check(!Claim(compost, side), "side contact does not qualify");
            Clear();
            var lower = Corpse();
            var upper = Corpse(); upper.transform.position += Vector3.up * 0.3f; Physics.SyncTransforms();
            Check(!Claim(compost, upper), "stacked body must reach the actual back");
            Check(Claim(compost, lower), "directly supported dead body qualifies");
            Check(!Claim(compost, lower), "one body cannot be claimed twice");
            Advance(compost, 2.5f);
            var absorption = lower.GetComponent<CompostAbsorption>();
            Check(Mathf.Approximately(absorption.Progress, 0.5f) && compost.StoredCharges == 0, "half sinking gives no early credit");
            Check(lower.GetComponent<Rigidbody>().isKinematic && Physics.GetIgnoreCollision(lower.GetComponent<Collider>(), compost.Back), "sinking owns physics and ignores only the platform collision");
            Check(!Physics.GetIgnoreCollision(lower.GetComponent<Collider>(), upper.GetComponent<Collider>()), "stacked bodies retain mutual collision");
            Advance(compost, 2.5f);
            Check(!lower.gameObject.activeSelf && compost.StoredCharges == 6, "five active seconds consumes one corpse for six charges");
            Check(lower.DeathCount == 1 && lower.ConsumptionCount == 0, "compost does not repeat death or invoke food effects");
            Call(compost, "FixedUpdate"); Physics.SyncTransforms();
            upper.transform.position = origin + new Vector3(0f, 2.151f, 0f); Physics.SyncTransforms();
            Check(Claim(compost, upper), "waiting body is accepted after reaching the back");
            Clear();

            var a = Corpse(300f, z: -0.4f);
            var b = Corpse(300f, z: 0.4f);
            Charges(compost, 45);
            Check(Claim(compost, a) && Claim(compost, b), "two supported bodies absorb concurrently");
            Advance(compost, 5f);
            Check(compost.StoredCharges == 60 && !a.gameObject.activeSelf && b.gameObject.activeSelf, "45 + 15 reaches 60; second completion waits");
            Check(Mathf.Approximately(b.GetComponent<CompostAbsorption>().Progress, 0.8f), "waiting body remains visibly partly sunk");
            Charges(compost, 50); Advance(compost, 8f);
            Check(Mathf.Approximately(b.GetComponent<CompostAbsorption>().Progress, 0.8f), "exactly 50 still blocks final sinking");
            Charges(compost, 49); Advance(compost, 0f);
            Check(Mathf.Approximately(b.GetComponent<CompostAbsorption>().Progress, 0.8f), "zero gameplay delta preserves progress");
            Advance(compost, 1f);
            Check(compost.StoredCharges == 64 && !b.gameObject.activeSelf, "below 50 resumes remaining time and credits the whole body");
            Clear();
            a = Corpse(1100f); Check(Claim(compost, a), "oversized meal is accepted"); Advance(compost, 5f);
            Check(compost.StoredCharges == 55, "0 + 55 is permitted without a hard cap");
            Clear();
            a = Corpse(); Check(Claim(compost, a), "snapshot fixture accepted");
            typeof(Creature).GetField("nutrition", Private).SetValue(a, 2000f);
            Advance(compost, 5f);
            Check(compost.StoredCharges == 6, "Nutrition is snapshotted on entry");
            Clear();
            a = Corpse(); Claim(compost, a); Advance(compost, 4.5f); Charges(compost, 50); Advance(compost, 1f);
            Check(Mathf.Approximately(a.GetComponent<CompostAbsorption>().Progress, 0.9f), "fullness never rewinds an already deeper body");
            Clear();

            a = Corpse(); Claim(compost, a); Advance(compost, 2f); Call(compost, "FixedUpdate"); Physics.SyncTransforms();
            var player = new GameObject("Compost Pickup Player"); player.transform.SetParent(stage.transform, false); player.SetActive(false);
            player.transform.position = origin + Vector3.right * 4f;
            var controller = player.AddComponent<SmoothFirstPersonController>();
            var cameraObject = new GameObject("Pickup Camera", typeof(Camera)); cameraObject.transform.SetParent(player.transform, false);
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            Set(carrier, "carryCamera", cameraObject.GetComponent<Camera>());
            player.SetActive(true); Call(controller, "Start"); controller.enabled = false;
            Call(carrier, "BeginCarry", a);
            Check(carrier.CarriedCreature == a && a.GetComponent<CompostAbsorption>().Owner == null, "existing pickup cancels sinking before carry ownership");
            Check(a.transform.position.y >= 2.14f, "retrieved body is restored above the back");
            Advance(compost, 5f);
            Check(compost.StoredCharges == 0, "retrieval gives no compost credit");
            carrier.Release();
            Check(!a.GetComponent<Rigidbody>().isKinematic && a.GetComponent<Rigidbody>().useGravity, "drop restores original corpse physics");
            Object.DestroyImmediate(player);
            Clear();

            a = Corpse(); Claim(compost, a);
            var eater = Corpse(dead: false, x: 2f);
            using (var fields = new SerializedObject(eater))
            {
                fields.FindProperty("dietTags").intValue = -1;
                fields.FindProperty("canEatSameType").boolValue = true;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            Check(eater.TryConsumeFood(a), "existing feeding can remove a claimed body");
            Advance(compost, 5f);
            Check(compost.StoredCharges == 0 && compost.AbsorbingBodyCount == 0, "competing consumption cancels without duplicate reward");
            Clear();
            a = Corpse(); Claim(compost, a); Object.DestroyImmediate(a.gameObject); Advance(compost, 5f);
            Check(compost.StoredCharges == 0 && compost.AbsorbingBodyCount == 0, "external removal releases stale claim without credit");

            var bonesAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BirdBones.prefab");
            if (bonesAsset != null)
            {
                var bones = Object.Instantiate(bonesAsset, stage.transform).GetComponent<BirdBones>();
                bones.transform.position = origin + Vector3.up * 2.2f; Physics.SyncTransforms();
                bones.transform.position += Vector3.up * (compost.BackTopWS - bones.GetComponent<Collider>().bounds.min.y + 0.001f);
                Physics.SyncTransforms();
                Check(bones.IsDead && Claim(compost, bones), "Bird Bones compost through the shared IsDead state");
                Object.DestroyImmediate(bones.gameObject);
            }

            Set(compost, "gooPrefab", goo); Charges(compost, 10); Advance(compost, 0.01f);
            Check(compost.IsProducing && compost.StoredCharges == 0 && compost.ProductionProgress == 0f, "production reserves ten charges on start");
            int GooCount() => stage.scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<GooCreature>().Length);
            int initialGoo = GooCount(); Advance(compost, 6.9f);
            Check(GooCount() == initialGoo && compost.IsProducing, "production does not finish early");
            Advance(compost, 0.1f);
            Check(GooCount() == initialGoo + 1 && !compost.IsProducing, "seven seconds releases one living goo behind the creature");
            foreach (var r in scene.GetRootGameObjects()) if (r.GetComponent<GooCreature>() != null) Object.DestroyImmediate(r);
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.SetParent(stage.transform, false); obstruction.transform.position = origin + new Vector3(0f, 0.4f, -1.55f);
            Physics.SyncTransforms();
            Charges(compost, 10); Advance(compost, 0.01f); Advance(compost, 7f);
            Check(compost.IsProducing && compost.ProductionProgress == 1f && compost.StoredCharges == 0, "blocked output retains the completed batch without another charge");
            Object.DestroyImmediate(obstruction); Physics.SyncTransforms(); Advance(compost, 0.01f);
            Check(!compost.IsProducing && GooCount() == initialGoo + 1, "clearing output obstruction releases the retained goo");
            foreach (var r in scene.GetRootGameObjects()) if (r.GetComponent<GooCreature>() != null) Object.DestroyImmediate(r);
            Clear();

            a = Corpse(); Claim(compost, a); Charges(compost, 60); Set(compost, "gooPrefab", goo); Set(compost, "producing", true);
            Advance(compost, 4f);
            compost.SetSimulationEnabled(false); Advance(compost, 100f);
            Check(compost.StoredCharges == 60 && Mathf.Approximately(compost.ProductionProgress, 4f / 7f) &&
                Mathf.Approximately(a.GetComponent<CompostAbsorption>().Progress, 0.8f), "simulation pause freezes production and absorption");
            compost.SetSimulationEnabled(true);
            Advance(compost, 3f);
            Check(compost.StoredCharges == 50 && compost.IsProducing, "next production reservation reduces 60 to 50");
            Advance(compost, 1f);
            Check(Mathf.Approximately(a.GetComponent<CompostAbsorption>().Progress, 0.8f), "50 after production spending still pauses the corpse");
            foreach (var r in scene.GetRootGameObjects()) if (r.GetComponent<GooCreature>() != null) Object.DestroyImmediate(r);
            Advance(compost, 6f);
            Check(compost.StoredCharges == 40, "following reservation reduces 50 to 40");
            Advance(compost, 1f);
            Check(!a.gameObject.activeSelf && compost.StoredCharges == 46, "spending below threshold resumes absorption");
            Clear();

            bool Walks(Vector3 candidate, out float ground)
            {
                object[] values = { candidate, Vector3.forward, Vector3.right, 0f };
                bool result = (bool)Call(compost, "CanWalk", values); ground = (float)values[3]; return result;
            }
            Check(Walks(origin + Vector3.forward * 0.01f, out _), "flat ground is traversable");
            Check(!Walks(origin + Vector3.forward * 14.6f, out _), "front-foot probes reject a cliff");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(stage.transform, false);
            wall.transform.position = origin + new Vector3(0f, 1.5f, 1.3f); wall.transform.localScale = new Vector3(2f, 3f, 0.2f);
            Physics.SyncTransforms();
            Check(!Walks(origin + Vector3.forward * 0.01f, out _), "forward obstacle prevents walking through a wall");
            Object.DestroyImmediate(wall); Physics.SyncTransforms();
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube); slope.transform.SetParent(stage.transform, false); slope.layer = 7;
            slope.transform.position = origin + new Vector3(8f, 3f, 0f); slope.transform.localScale = new Vector3(3f, 0.1f, 3f);
            slope.transform.rotation = Quaternion.Euler(0f, 0f, 50f); Physics.SyncTransforms();
            Check(!Walks(origin + new Vector3(8f, 3f, 0f), out _), "steep ground is rejected");
            Object.DestroyImmediate(slope); Physics.SyncTransforms();

            var rider = new GameObject("Compost Rider"); rider.transform.SetParent(stage.transform, false); rider.SetActive(false);
            var movement = rider.AddComponent<SmoothFirstPersonController>(); rider.AddComponent<CapsuleCollider>().height = 2f;
            Set(movement, "orientation", rider.transform); Set(movement, "whatIsGround", (LayerMask)(1 << 7));
            rider.transform.position = origin + new Vector3(0f, 3.01f, 0f); rider.SetActive(true);
            Call(movement, "Start"); movement.enabled = false;
            var playerBody = rider.GetComponent<Rigidbody>();
            Call(compost.RideSurface, "PlanMotion", origin + Vector3.forward * 0.012f, Quaternion.identity, 0.02f);
            Physics.SyncTransforms(); Call(movement, "GroundCheck"); Call(movement, "SteerVelocity");
            Check(Mathf.Abs(playerBody.linearVelocity.z - 0.6f) < 0.03f, "standing movement includes platform translation");
            playerBody.position = origin + new Vector3(0f, 2.997f, 0f); Physics.SyncTransforms(); Call(movement, "GroundCheck");
            Check(movement.Grounded && typeof(SmoothFirstPersonController).GetField("rideSurface", Private).GetValue(movement) != null,
                "settled contact inside the thin ground cast retains compost support");
            Set(movement, "moveDirection", Vector3.right); Set(movement, "moveSpeed", 4f); Call(movement, "SteerVelocity");
            Check(playerBody.linearVelocity.x > 0f && playerBody.linearVelocity.z > 0.55f, "player walks relative to the moving back");
            Call(compost.RideSurface, "PlanMotion", origin, Quaternion.Euler(0f, 3f, 0f), 0.02f);
            Vector3 point = origin + Vector3.forward * 0.5f;
            Check(compost.RideSurface.VelocityAt(point).x > 1f, "turning contributes point-specific platform velocity");
            Call(movement, "Jump"); Call(movement, "UpdateRideSupport");
            Check(typeof(SmoothFirstPersonController).GetField("rideSurface", Private).GetValue(movement) == null, "jump detaches from platform support");
            Check(rider.transform.parent == stage.transform, "riding never parents the player to the creature");
            Object.DestroyImmediate(rider);

            a = Corpse(); Claim(compost, a); Advance(compost, 2f); Call(compost, "FixedUpdate");
            Charges(compost, 34); Set(compost, "producing", true); Set(compost, "productionTime", 3f);
            int deaths = 0; compost.Died += _ => deaths++;
            compost.Die(); compost.Die();
            Check(compost.IsDead && !compost.CanBeCarried && deaths == 1, "compost leaves an unportable corpse and dies once");
            Check(compost.StoredCharges == 0 && !compost.IsProducing && compost.AbsorbingBodyCount == 0, "own death clears charges, output, and absorption");
            Check(a.GetComponent<CompostAbsorption>().Owner == null && !a.GetComponent<Rigidbody>().isKinematic &&
                a.GetComponent<Rigidbody>().useGravity && a.transform.position.y >= 2.14f, "own death restores unfinished bodies above the solid back");
            Check(compost.Back.enabled && compost.GetComponent<Rigidbody>().isKinematic, "dead back remains a stationary collidable platform");
            Advance(compost, 100f);
            Check(compost.StoredCharges == 0 && !compost.IsProducing, "dead creature cannot continue processing");
            return "PASS: " + checks + " compost gameplay checks.";
        }
        finally
        {
            foreach (GameObject r in scene.GetRootGameObjects()) Object.DestroyImmediate(r);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
