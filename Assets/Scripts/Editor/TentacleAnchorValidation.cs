using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Real Play Mode regression for pickup ownership, terrain sweeps and teaching rigs.</summary>
[InitializeOnLoad]
public static class TentacleAnchorValidation
{
    private const string Pending = "Capstone.TentacleAnchorValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static TentacleAnchorValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Validate();
        };
        if (EditorApplication.isPlaying && SessionState.GetBool(Pending, false)) EditorApplication.delayCall += Validate;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Validate()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int checks = 0;
        SimulationMode oldMode = Physics.simulationMode;
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        object Static(string name, params object[] args) => typeof(TentacleTether).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Tentacle anchors: " + message);
            checks++;
        }
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.transform.position = origin + Vector3.up;
            var player = new GameObject("Player"); player.transform.position = origin;
            var playerBody = player.AddComponent<Rigidbody>(); playerBody.useGravity = false;
            playerBody.constraints = RigidbodyConstraints.FreezeRotation;
            var playerCollider = player.AddComponent<SphereCollider>(); playerCollider.radius = 0.4f;
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            carrier.enabled = false;
            var tether = player.AddComponent<TentacleTether>(); tether.enabled = false;
            var templateObject = new GameObject("Template"); templateObject.transform.position = origin + Vector3.left * 40f;
            templateObject.AddComponent<SphereCollider>().radius = 0.45f;
            var template = templateObject.AddComponent<TentacleCreature>();
            Transform site = TentaclePracticeSite.Build(origin + Vector3.forward * 30f, Quaternion.identity, 0, template, null, 75);
            var demo = site.GetComponentInChildren<TentacleCreature>();
            Check(tether.CordCount == 1 && demo.Suspended && demo.PinnedCount == 2, "teaching creature hangs from two real supports");
            Check(site.GetComponentsInChildren<MeshCollider>().Length == 6, "all six practice rocks have mesh collision");
            Check(site.Find("Landing ledge").localPosition.z == 19f, "launch has a separated forward landing target");
            var cords = (IList)Get(tether, "cords");
            object cord = cords[0]; Type cordType = cord.GetType();
            object Field(string name) => cordType.GetField(name).GetValue(cord);
            Vector3 middle = demo.transform.position;
            Check((bool)Static("PocketClear", cord, middle), "preplaced pocket and both arm corridors are clear");
            Check(Vector3.Distance((Vector3)Field("a"), (Vector3)Field("b")) > 9f, "supports are far enough apart to draw a slingshot");
            demo.Dragged = true; demo.BeginPet(middle, Vector3.up);
            demo.ContinuePet(middle - site.forward * 4f, Vector3.up, 0.1f);
            Call(tether, "Spring", cord, 0.4f);
            Check(Vector3.Distance((Vector3)Field("pocket"), middle - site.forward * 4f) < 0.5f, "body can be drawn backwards through the open corridor");
            Check(Physics.GetIgnoreCollision(playerCollider, (Collider)Field("solid")), "drawn arms do not push the pulling player");
            int flung = tether.Flung;
            Array groundArms = (Array)Get(demo, "tentacles");
            foreach (object arm in groundArms) arm.GetType().GetField("planted").SetValue(arm, true);
            Call(carrier, "BeginCarry", demo);
            Check(carrier.CarriedCreature == demo && demo.IsCarried, "normal pickup can take the teaching creature");
            Check(tether.CordCount == 0 && demo.PinnedCount == 0 && !demo.Suspended, "pickup removes bridge, anchor pins and suspension immediately");
            Check(!demo.IsPetted && !demo.Dragged && tether.Flung == flung, "pickup cancels dragging without launching anything");
            Check(!demo.GetComponent<Rigidbody>().isKinematic && !demo.GetComponent<Rigidbody>().useGravity, "carry retains ownership of its dynamic physics");
            Check(Physics.GetIgnoreCollision(demo.GetComponent<SphereCollider>(), playerCollider), "carry keeps body collision ignored after rig removal");
            Check(((List<int>)Get(tether, "pinnedArms")).Count == 0 && !tether.Reeling, "pickup clears all player pull state");
            bool planted = false;
            foreach (object arm in groundArms) planted |= (bool)arm.GetType().GetField("planted").GetValue(arm);
            Check(!planted, "pickup also clears walking holds before climb assistance can act on the player");
            playerBody.linearVelocity = Vector3.zero;
            Call(tether, "FixedUpdate"); Physics.Simulate(0.02f);
            Check(playerBody.linearVelocity.sqrMagnitude < 0.0001f, "pickup leaves no continuing spring force");

            // Reorder arm IDs deliberately: forces must use each ID's own anchor/rest length.
            tether.FireAt(origin + Vector3.left * 4f);
            tether.FireAt(origin + Vector3.right * 10f + Vector3.forward * 3f);
            var arms = (List<int>)Get(tether, "pinnedArms"); arms.Reverse();
            var lengths = (Dictionary<int, float>)Get(tether, "restLengths");
            playerBody.position = origin + Vector3.forward * 3f; playerBody.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Vector3 expected = Vector3.zero;
            foreach (int arm in arms)
            {
                demo.ArmPin(arm, out Vector3 pin); Vector3 delta = pin - playerBody.position;
                expected += delta.normalized * Mathf.Min(Mathf.Max(0f, delta.magnitude - lengths[arm]) * 5f, 40f);
            }
            Call(tether, "PullBack"); Physics.Simulate(0.02f);
            Check(Vector3.Distance(playerBody.linearVelocity, Vector3.ClampMagnitude(expected, 40f) * 0.02f) < 0.01f,
                "shot ordering never mismatches an arm with another arm's rest length");
            tether.FireAt(origin + Vector3.back * 8f);
            Check(arms.Count == 2 && lengths.Count == 2, "third shot removes the replaced arm's stored rest length");
            Set(tether, "reeling", true); Set(tether, "reeledArm", arms[0]);
            tether.LetGoAll();
            Check(!tether.Reeling && (int)Get(tether, "reeledArm") == -1 && lengths.Count == 0, "manual release cancels reel state as well as pins");
            Call(tether, "ReelIn", 0.02f);
            Check(!tether.Reeling, "empty reel remains safe on the next physics tick");
            Call(carrier, "ReleaseCarriedCreature");
            Check(demo.GetComponent<Rigidbody>().isKinematic && !demo.GetComponent<Rigidbody>().useGravity,
                "release restores normal swept wandering instead of dynamic collision forces");
            Call(tether, "Update");
            demo.transform.position = origin + Vector3.forward * 12f + Vector3.up * 2f;
            demo.GetComponent<Rigidbody>().position = demo.transform.position;
            Call(carrier, "BeginCarry", demo);
            tether.FireAt(demo.transform.position + Vector3.left * 4f + Vector3.up * 0.4f);
            tether.FireAt(demo.transform.position + Vector3.right * 4f + Vector3.up * 0.4f);
            Call(carrier, "ReleaseCarriedCreature");
            Call(tether, "Update");
            Check(tether.CordCount == 1 && demo.Suspended && demo.PinnedCount == 2,
                "shooting two anchors then dropping creates the player's own usable rig");
            Call(carrier, "BeginCarry", demo);
            Call(carrier, "ReleaseCarriedCreature");
            Call(tether, "Update");
            Check(tether.CordCount == 0 && demo.PinnedCount == 0, "picking up and dropping again does not resurrect old anchors");

            Transform second = TentaclePracticeSite.Build(origin + Vector3.right * 60f, Quaternion.Euler(0f, 35f, 0f), 1, template, null, 76);
            Check(tether.CordCount == 1 && second.GetComponentInChildren<TentacleCreature>().PinnedCount == 2, "second wider, rotated teaching rig also attaches");
            cord = cords[0]; cordType = cord.GetType();
            Vector3 start = (Vector3)Field("pocket");
            Vector3 forward = second.forward;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Thin rebound obstacle"; wall.transform.SetPositionAndRotation(start + forward * 2f, second.rotation);
            wall.transform.localScale = new Vector3(20f, 8f, 0.12f);
            Physics.SyncTransforms();
            Vector3 safe = (Vector3)Static("SafePocket", cord, start, start + forward * 8f);
            Check(Vector3.Dot(safe - start, forward) < 1.5f, "body sweep stops before a thin wall even when the target lies beyond it");
            Check((bool)Static("PocketClear", cord, safe), "stopped body still leaves both arms outside terrain");
            UnityEngine.Object.DestroyImmediate(wall);
            Vector3 aPin = (Vector3)Field("a");
            var armRock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            armRock.name = "Arm-only obstacle"; armRock.transform.position = (start + aPin) * 0.5f + forward * 1.5f;
            armRock.transform.rotation = second.rotation; armRock.transform.localScale = new Vector3(0.7f, 5f, 0.8f);
            Physics.SyncTransforms();
            Check(!(bool)Static("PocketClear", cord, start + forward * 3f), "arm corridor rejects a clear body destination when a stretched arm crosses rock");
            safe = (Vector3)Static("SafePocket", cord, start, start + forward * 5f);
            Check((bool)Static("PocketClear", cord, safe) && Vector3.Dot(safe - start, forward) < 3f,
                "drawing and rebound respect the entire two-arm triangle");
            UnityEngine.Object.DestroyImmediate(armRock);
            var creature = (TentacleCreature)Field("creature");
            creature.Dragged = true; creature.BeginPet(start, Vector3.up);
            creature.ContinuePet(start - forward * 4f, Vector3.up, 0.1f);
            Call(tether, "Spring", cord, 0.5f);
            creature.Dragged = false; creature.EndPet();
            Call(tether, "Spring", cord, 0.1f);
            Check(tether.Flung == flung + 1, "ordinary mouse release still launches a drawn slingshot");

            Vector3 groundOrigin = origin + Vector3.forward * 300f;
            var worldObject = new GameObject("Practice terrain layout"); worldObject.SetActive(false);
            worldObject.transform.position = groundOrigin;
            var world = worldObject.AddComponent<ProceduralWorld>();
            Set(world, "sinkholeCount", 0); Set(world, "surfaceAmplitude", 0f); Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));
            Check(!TentaclePracticeSite.TryBuild(world, groundOrigin, Vector3.forward, 10f, 0, template, 80),
                "automatic teaching placement waits for actual terrain");
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-130f, 0f, -130f), new Vector3(130f, 0f, -130f),
                new Vector3(-130f, 0f, 130f), new Vector3(130f, 0f, 130f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            var terrain = new GameObject("Loaded practice ground"); terrain.layer = world.TerrainLayer;
            terrain.transform.position = groundOrigin; terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
            terrain.AddComponent<ProceduralWorldChunk>().Initialize(world, Vector3Int.zero);
            Physics.SyncTransforms();
            Check(TentaclePracticeSite.TryBuild(world, groundOrigin, Vector3.forward, 10f, 0, template, 80),
                "loaded terrain generates the first practice site near the kiln");
            Check(TentaclePracticeSite.TryBuild(world, groundOrigin, Vector3.forward, 10f, 1, template, 80),
                "opposite side generates the second separated practice site");
            Check(tether.CordCount == 3, "automatic sites register both visible teaching creatures with the player's tether");

            // An off-centre wall drop must not home towards the anchors' midpoint, nor arm a slingshot by itself.
            player.AddComponent<SmoothFirstPersonController>();
            Set(carrier, "playerColliders", player.GetComponentsInChildren<Collider>(true));
            Vector3 towerOrigin = origin + Vector3.right * 150f;
            var tower = GameObject.CreatePrimitive(PrimitiveType.Cube); tower.transform.position = towerOrigin + Vector3.up * 10f;
            tower.transform.localScale = new Vector3(4f, 20f, 4f);
            Vector3 dropPoint = towerOrigin + Vector3.back * 6f + Vector3.up * 3f;
            playerBody.position = dropPoint; playerBody.linearVelocity = Vector3.zero;
            camera.transform.position = dropPoint + Vector3.up;
            var wallOctopus = new GameObject("Wall drop octopus"); wallOctopus.transform.position = dropPoint;
            var wallBody = wallOctopus.AddComponent<Rigidbody>();
            var wallCollider = wallOctopus.AddComponent<SphereCollider>(); wallCollider.radius = 0.45f;
            var wallCreature = wallOctopus.AddComponent<TentacleCreature>();
            Set(wallCreature, "wanderTarget", origin + Vector3.left * 30f);
            Call(carrier, "BeginCarry", wallCreature);
            tether.FireAt(towerOrigin + new Vector3(-1f, 8f, -2.06f), tower.transform);
            tether.FireAt(towerOrigin + new Vector3(1f, 9f, -2.06f), tower.transform);
            Physics.SyncTransforms();
            int rigCount = tether.CordCount, launches = tether.Flung;
            Call(carrier, "ReleaseCarriedCreature");
            Check(wallCreature.Suspended && tether.CordCount == rigCount + 1,
                "RMB release creates the wall rig in the same call, before autonomous movement resumes");
            object wallCord = cords[cords.Count - 1]; Type wallCordType = wallCord.GetType();
            object WallField(string name) => wallCordType.GetField(name).GetValue(wallCord);
            Check(Vector3.Distance((Vector3)WallField("rest"), dropPoint) < 0.01f,
                "an off-centre tower attachment rests where it was put down");
            Check(Vector3.Distance((Vector3)Get(wallCreature, "wanderTarget"), dropPoint) < 0.01f,
                "drop forgets the old distant wandering target");
            Check(Physics.GetIgnoreCollision(wallCollider, playerCollider)
                && Physics.GetIgnoreCollision(playerCollider, (Collider)WallField("solid")),
                "drop preserves clearance from both the growing body and newly created bridge strips");
            for (int i = 0; i < 50; i++) { Call(tether, "FixedUpdate"); Call(tether, "LateUpdate"); Physics.Simulate(0.02f); }
            Check(Vector3.Distance(wallCreature.transform.position, dropPoint) < 0.05f && tether.Flung == launches,
                "wall drop stays put over many physics steps without recentering or launching");
            Check(playerBody.linearVelocity.sqrMagnitude < 0.001f, "an overlapping drop cannot kick the player away from the tower");
            wallCordType.GetField("passUntil").SetValue(wallCord, Time.time - 1f);
            Call(tether, "Spring", wallCord, 0.02f);
            Check(Physics.GetIgnoreCollision(playerCollider, (Collider)WallField("solid")),
                "collision grace cannot expire while the player's body still intersects an arm strip");
            playerBody.position = dropPoint + Vector3.back * 5f; Physics.SyncTransforms(); Call(tether, "Spring", wallCord, 0.02f);
            Check(!Physics.GetIgnoreCollision(playerCollider, (Collider)WallField("solid")),
                "bridge collision returns after the player moves clear");
            wallCreature.Dragged = true; wallCreature.BeginPet(dropPoint, Vector3.up);
            wallCreature.ContinuePet(dropPoint + Vector3.back * 0.15f, Vector3.up, 0.1f);
            Call(tether, "Spring", wallCord, 0.3f); wallCreature.Dragged = false; wallCreature.EndPet(); Call(tether, "Spring", wallCord, 0.02f);
            Check(tether.Flung == launches, "a tiny touch does not convert the large anchor-to-body offset into an accidental shot");
            wallCreature.Dragged = true; wallCreature.BeginPet(wallCreature.transform.position, Vector3.up);
            wallCreature.ContinuePet(dropPoint + Vector3.back * 3f, Vector3.up, 0.1f);
            Call(tether, "Spring", wallCord, 0.4f); wallCreature.Dragged = false; wallCreature.EndPet(); Call(tether, "Spring", wallCord, 0.02f);
            Check(tether.Flung == launches + 1, "a deliberate draw still launches the wall slingshot");

            // Dropping without a rig also keeps collision ignored until the grown body has separated.
            playerBody.linearVelocity = Vector3.zero; playerBody.position = dropPoint;
            wallBody.position = dropPoint; wallCreature.transform.position = dropPoint;
            Call(carrier, "BeginCarry", wallCreature);
            wallBody.linearVelocity = new Vector3(30f, 20f, -40f); wallBody.angularVelocity = Vector3.one * 10f;
            Call(carrier, "ReleaseCarriedCreature");
            Check(wallBody.isKinematic && wallBody.linearVelocity.sqrMagnitude < 0.001f && wallBody.angularVelocity.sqrMagnitude < 0.001f,
                "drop clears carry spring velocity before returning to kinematic crawling");
            var deferred = (IList)Get(carrier, "droppedCollisions");
            for (int i = 0; i < deferred.Count; i++)
            {
                object pair = deferred[i]; pair.GetType().GetField("restoreAt").SetValue(pair, Time.time - 1f); deferred[i] = pair;
            }
            Physics.SyncTransforms(); Call(carrier, "RestoreDroppedCollisions");
            Check(Physics.GetIgnoreCollision(wallCollider, playerCollider), "free drop waits for body clearance even after the initial grace time");
            playerBody.position += Vector3.back * 6f; Physics.SyncTransforms(); Call(carrier, "RestoreDroppedCollisions");
            Check(!Physics.GetIgnoreCollision(wallCollider, playerCollider), "free drop restores collision once the body no longer overlaps the player");
            Debug.Log($"Tentacle anchor Play Mode validation passed: {checks} assertions.");
            Physics.simulationMode = oldMode;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Physics.simulationMode = oldMode;
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}

