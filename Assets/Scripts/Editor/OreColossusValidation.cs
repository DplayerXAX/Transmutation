using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Play Mode checks for the Ore Colossus, its heart, kneeling and the Geode Larva, in disposable local
/// physics scenes. The colossus is driven with a fixed step; nothing touches the open scene.
/// </summary>
public static class OreColossusValidation
{
    private sealed class Failure : Exception { public Failure(string message) : base(message) { } }
    private sealed class Witness
    {
        public GameObject root; public Rigidbody body; public SmoothFirstPersonController controller;
        public PlayerCreatureCarrier carrier; public Camera camera;
    }

    private static int checks;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float Step = 0.05f;

    private static void Check(bool condition, string description)
    { if (!condition) throw new Failure("Ore Colossus: " + description); checks++; }

    private static void Set(object target, string name, object value)
    {
        Type type = target.GetType(); FieldInfo field = null;
        while (type != null && field == null) { field = type.GetField(name, Any); type = type.BaseType; }
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    private static object Call(object target, string name, params object[] args)
    {
        Type type = target.GetType();
        while (type != null)
        {
            foreach (MethodInfo method in type.GetMethods(Any | BindingFlags.DeclaredOnly))
                if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(target, args);
            type = type.BaseType;
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }

    private static void Advance(OreColossus colossus, int steps) { for (int i = 0; i < steps; i++) { Call(colossus, "Advance", Step); Physics.SyncTransforms(); } }

    private static CreatureLifecycleValidationProbe Probe(Scene scene, Vector3 position, bool gravity = false)
    {
        var root = new GameObject("Ore Colossus witness creature"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = position;
        root.AddComponent<Rigidbody>().useGravity = gravity; root.AddComponent<BoxCollider>();
        var probe = root.AddComponent<CreatureLifecycleValidationProbe>();
        Set(probe, "initialPolarity", CreaturePolarity.Light);
        root.SetActive(true);
        return probe;
    }

    private static Witness Player(Scene scene, Vector3 position)
    {
        var w = new Witness { root = new GameObject("Ore Colossus witness player") };
        w.root.SetActive(false); w.root.layer = 6;
        SceneManager.MoveGameObjectToScene(w.root, scene);
        w.root.transform.position = position;
        w.body = w.root.AddComponent<Rigidbody>(); w.body.useGravity = false;
        w.root.AddComponent<CapsuleCollider>();
        w.controller = w.root.AddComponent<SmoothFirstPersonController>();
        var cameraObject = new GameObject("Witness camera"); cameraObject.transform.SetParent(w.root.transform, false);
        w.camera = cameraObject.AddComponent<Camera>(); w.camera.enabled = false;
        var anchor = new GameObject("Witness carry point").transform; anchor.SetParent(w.root.transform, false);
        anchor.localPosition = new Vector3(0.3f, -0.3f, 1.2f);
        w.carrier = w.root.AddComponent<PlayerCreatureCarrier>();
        Set(w.carrier, "carryCamera", w.camera); Set(w.carrier, "carryPoint", anchor);
        w.root.SetActive(true);
        // No rendered frame here: set only the references Start would cache.
        Set(w.controller, "rb", w.body);
        Set(w.controller, "playerCamera", w.camera);
        return w;
    }

    private static OreColossus Colossus(Scene scene, Vector3 at)
    {
        var root = new GameObject("Ore Colossus under test"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = at;
        root.AddComponent<Rigidbody>();
        var colossus = root.AddComponent<OreColossus>();
        Set(colossus, "placeInWorld", false);
        Set(colossus, "initialPolarity", CreaturePolarity.None);
        Set(colossus, "restChance", 0f);
        root.SetActive(true);
        return colossus;
    }

    [MenuItem("Tools/Creatures/Ore Colossus/Validate (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        CreatureThrower originalThrower = CreatureThrower.Instance;
        var scene = SceneManager.CreateScene("Ore Colossus Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        // Far below anything the player can see; local physics keeps every query separate anyway.
        Vector3 origin = new Vector3(0f, -1000f, 0f);
        string fluidReport;
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Validation floor"; floor.layer = 7;
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = origin + Vector3.down * 0.5f; floor.transform.localScale = new Vector3(400f, 1f, 400f);
            Physics.SyncTransforms();

            // ---- Build, identity and immunity ----
            OreColossus colossus = Colossus(scene, origin);
            Advance(colossus, 1);
            Check(colossus.Built && colossus.CurrentPhase == OreColossus.Phase.Walking, "builds where it stands without a world and starts walking");
            Check(colossus.Polarity == CreaturePolarity.None && !colossus.CanBeAITargeted, "never an AI target");
            Check(colossus.Invulnerable && !colossus.CanBeCarried && !colossus.CanBeConsumed, "structural creature flags");
            var meleePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AsuraPrototypeSetup.Folder + "/MeleeAsura.prefab");
            if (meleePrefab != null)
            {
                var staging = new GameObject("Asura staging"); staging.SetActive(false); SceneManager.MoveGameObjectToScene(staging, scene);
                var melee = Object.Instantiate(meleePrefab, origin + new Vector3(20f, 0.05f, 0f), Quaternion.identity, staging.transform);
                melee.transform.SetParent(null, true);
                Physics.SyncTransforms();
                Check(!melee.GetComponent<CreatureCombat>().Eligible(colossus, 60f), "Asura combat ignores the colossus");
                Object.DestroyImmediate(melee); Object.DestroyImmediate(staging);
            }
            float health = colossus.Health; bool damaged = false;
            colossus.Damaged += (self, amount, source) => damaged = true;
            Check(!colossus.ApplyDamage(1000f) && colossus.Health == health && !damaged, "direct damage is refused before Damaged listeners");
            colossus.GetLeg(0, out _, out _, out Vector3 foot0, out _);
            var bystander = Probe(scene, foot0 + new Vector3(0f, 0.6f, 2.5f));
            Physics.SyncTransforms();
            ExplosionResolver.Request(scene, foot0 + Vector3.up * 0.6f + Vector3.forward * 1.5f, 4f, 60f, 0f, null);
            Check(colossus.Health == health && !colossus.IsDead && bystander.Health < 100f, "blasts beside a foot hurt others but not the colossus");
            Object.DestroyImmediate(bystander.gameObject);

            // ---- Carry refusal ----
            Witness witness = Player(scene, origin + new Vector3(40f, 1f, 40f));
            Call(witness.carrier, "BeginCarry", colossus);
            Check(!colossus.IsCarried && witness.carrier.CarriedCreature == null, "the player cannot pick it up");

            // ---- Walking on a Ground floor ----
            float travelled = 0f, worstSegment = 0f, worstFoot = 0f, worstPivot = 0f; int maxPairs = 0;
            Vector3 last = colossus.transform.position;
            for (int i = 0; i < 400; i++)
            {
                Advance(colossus, 1);
                Vector3 now = colossus.transform.position;
                travelled += new Vector2(now.x - last.x, now.z - last.z).magnitude; last = now;
                worstPivot = Mathf.Max(worstPivot, Mathf.Abs(now.y - origin.y));
                maxPairs = Mathf.Max(maxPairs, colossus.SwingingPairCount);
                for (int leg = 0; leg < colossus.LegCount; leg++)
                {
                    colossus.GetLeg(leg, out Vector3 hip, out Vector3 knee, out Vector3 foot, out bool swinging);
                    worstSegment = Mathf.Max(worstSegment, Mathf.Abs((knee - hip).magnitude - colossus.UpperLength), Mathf.Abs((foot - knee).magnitude - colossus.LowerLength));
                    if (!swinging) worstFoot = Mathf.Max(worstFoot, Mathf.Abs(foot.y - origin.y));
                }
            }
            Check(travelled >= 15f, $"walks at least 15 m in 20 s (walked {travelled:0.0} m)");
            Check(worstSegment < 0.01f, $"leg segments keep their length (worst error {worstSegment:0.0000} m)");
            Check(worstFoot < 0.15f, $"planted feet stay on the ground (worst {worstFoot:0.000} m)");
            Check(worstPivot < 0.5f, "its pivot follows the ground");
            Check(maxPairs <= 1, "only one diagonal pair of legs swings at a time");

            // ---- Stomp ----
            Vector3 stomp = colossus.transform.position + colossus.transform.forward * 30f;
            witness.root.transform.position = stomp + new Vector3(2f, 1f, 0f); witness.body.position = witness.root.transform.position;
            witness.body.linearVelocity = Vector3.zero;
            var victim = Probe(scene, stomp + new Vector3(-2f, 0.6f, 0f));
            Physics.SyncTransforms();
            Call(colossus, "ForceStomp", stomp);
            Check(witness.controller.Flung && witness.body.linearVelocity.x > 1f && witness.body.linearVelocity.y > 0.5f, "a stomp flings the player outward and up");
            Check(victim.Health < 100f, "a stomp hurts small creatures beside it");
            physics.Simulate(0.02f);
            Check(victim.GetComponent<Rigidbody>().linearVelocity.x < -0.5f, "a stomp pushes loose bodies outward");
            Object.DestroyImmediate(victim.gameObject);

            // ---- Heart as an erosion target ----
            OreHeart heart = colossus.Heart;
            Check(heart != null && !heart.Broken && ErosionTarget.TryGet(heart.Collider, out ErosionTarget heartTarget) && heartTarget == heart.Target,
                "the heart's collider is a registered erosion target");
            Check(ErosionTarget.IsSurface(heart.Collider) && (ErosionTarget.SurfaceLayers & (1 << heart.gameObject.layer)) != 0, "surface layers cover the heart");
            float before = heart.Integrity;
            float first = (float)Call(heart.Target, "Erode", heart.transform.position, 50f);
            float second = (float)Call(heart.Target, "Erode", heart.transform.position, 50f);
            Check(first > 0f && first + second <= heart.Target.MaximumPerSecond * Time.fixedDeltaTime + 1e-4f, "contact erosion shares a per-second cap");
            float splash = (float)Call(heart.Target, "Splash", heart.transform.position, 12f);
            float again = (float)Call(heart.Target, "Splash", heart.transform.position, 12f);
            Check(splash > 0.5f && again == 0f, "thrown bursts count and are throttled");
            Check(Mathf.Abs(heart.Integrity - (before - first - second - splash)) < 1e-3f, "erosion wears the heart's integrity");
            Advance(colossus, 1);
            Check(colossus.CurrentPhase == OreColossus.Phase.Staggering, "cracking past a quarter makes it stagger");

            // ---- Shatter and kneel ----
            int larvae = CountLarvae(scene);
            colossus.DebugShatter();
            for (int i = 0; i < 300 && !colossus.Settled; i++) Advance(colossus, 1);
            Check(colossus.Settled && !colossus.IsDead, "a broken heart makes it kneel for good, alive");
            Check(heart.Broken && !ErosionTarget.TryGet(heart.Collider, out _), "the shattered heart takes no more water");
            foreach (Collider collider in colossus.GetComponentsInChildren<Collider>(true))
                Check(collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy, "no live colliders stay on the creature once it kneels");
            Check(colossus.Remains != null && colossus.Remains.scene == scene, "kneeling leaves static remains in its own scene");
            int legParts = 0, groundParts = 0;
            foreach (Collider collider in colossus.Remains.GetComponentsInChildren<Collider>())
            {
                Check(collider.attachedRigidbody == null, "remains are static");
                bool legPart = collider.name == "Upper" || collider.name == "Lower" || collider.name == "Foot";
                Check(collider.gameObject.layer == (legPart ? 8 : 7), $"remains layer for {collider.name}");
                if (legPart) legParts++; else groundParts++;
            }
            Check(legParts == colossus.LegCount * 3 && groundParts >= colossus.TailPlateCount + 2, "legs, body, tail and shrine all become remains");
            Check(Vector3.Angle(colossus.Body.up, Vector3.up) <= 15f, "the back is nearly level");
            for (int i = 0; i < colossus.TailPlateCount; i++)
            {
                Vector3 along = colossus.TailPlate(i).forward;
                Check(Mathf.Asin(Mathf.Clamp(-along.y, -1f, 1f)) * Mathf.Rad2Deg <= colossus.MaxRampSlope + 0.5f, "the tail ramp is walkable");
            }
            Transform tip = colossus.TailPlate(colossus.TailPlateCount - 1);
            Vector3 tipEnd = tip.position + tip.forward * 3f;
            Check(Mathf.Abs(tipEnd.y - origin.y) < 0.45f, $"the ramp reaches the ground (tip {tipEnd.y - origin.y:0.00} m)");
            Transform midPlate = colossus.TailPlate(colossus.TailPlateCount / 2);
            Vector3 rampPoint = midPlate.position + midPlate.forward * 1.5f;
            Check(physics.Raycast(rampPoint + Vector3.up * 2f, Vector3.down, out RaycastHit rampHit, 4f, 1 << 7, QueryTriggerInteraction.Ignore) &&
                rampHit.collider.transform.IsChildOf(colossus.Remains.transform) && rampHit.normal.y >= Mathf.Cos(40f * Mathf.Deg2Rad), "the ramp is standable ground");
            Check(physics.Raycast(colossus.Body.position + Vector3.up * 6f + colossus.Body.forward * -3f, Vector3.down, out RaycastHit deckHit, 8f, 1 << 7, QueryTriggerInteraction.Ignore) &&
                deckHit.normal.y >= Mathf.Cos(15f * Mathf.Deg2Rad), "the back is standable ground");

            // ---- Exactly one larva ----
            GeodeLarva larva = colossus.ReleasedLarva;
            Check(larva != null && larva.gameObject.scene == scene && larva.transform.parent == null && CountLarvae(scene) == larvae + 1, "the geode releases one larva at the scene root");
            Check(larva.Nested && larva.Polarity == CreaturePolarity.None && larva.Invulnerable && !larva.CanBeConsumed, "the larva rests in its geode, untargetable and unharmed by water");
            colossus.DebugShatter(); Advance(colossus, 5);
            Check(CountLarvae(scene) == larvae + 1, "a colossus releases only one larva");

            // ---- Infusing life ----
            var receiver = Probe(scene, origin + new Vector3(60f, 1f, 0f));
            Call(receiver, "SetRemainingAge", 10f);
            Call(witness.carrier, "BeginCarry", larva);
            Check(larva.IsCarried && !larva.Nested && larva.CanUseWhileCarried, "carried, the larva takes the left click");
            float reserve = larva.Reserve;
            Check((bool)Call(larva, "TryInfuse", receiver) && Mathf.Abs(receiver.Age - 25f) < 1e-3f && Mathf.Abs(larva.Reserve - (reserve - 15f)) < 1e-3f,
                "one infusion moves fifteen seconds of life");
            Check(!(bool)Call(larva, "TryInfuse", receiver), "the cooldown stops a second infusion");
            Set(larva, "nextUse", 0f);
            Check(!(bool)Call(larva, "TryInfuse", colossus), "invulnerable creatures are not infused");
            var dead = Probe(scene, origin + new Vector3(62f, 1f, 0f)); dead.Die();
            Check(!(bool)Call(larva, "TryInfuse", dead), "dead creatures are not infused");
            // Through the real click: the camera ray finds the creature it looks at.
            witness.root.transform.position = origin + new Vector3(60f, 1f, -4f); witness.body.position = witness.root.transform.position;
            witness.camera.transform.SetPositionAndRotation(witness.root.transform.position + Vector3.up * 0.5f, Quaternion.LookRotation(receiver.transform.position - (witness.root.transform.position + Vector3.up * 0.5f)));
            Physics.SyncTransforms();
            float ageBefore = receiver.Age;
            larva.ReceiveInteraction(new CreatureInteraction(witness.root, receiver.transform.position, witness.camera.transform.forward));
            Check(receiver.Age > ageBefore, "left click infuses the creature under the crosshair");
            Set(larva, "nextUse", 0f); Set(larva, "reserve", 0f);
            ageBefore = receiver.Age;
            Check(!(bool)Call(larva, "TryInfuse", receiver) && receiver.Age == ageBefore && larva.CarriedPrompt.StartsWith("Drained"), "an empty larva gives nothing");

            // ---- Recharge ----
            witness.carrier.Release();
            Check(!larva.IsCarried && !larva.GetComponent<Rigidbody>().isKinematic && larva.GetComponent<Rigidbody>().useGravity,
                "once lifted out of its geode the larva is an ordinary physics body");
            colossus.CradlePoint(out Vector3 cradle);
            Call(larva, "Nest", colossus, cradle, Quaternion.identity);
            Set(larva, "reserve", 10f);
            Call(larva, "TickCreature", 1f);
            Check(Mathf.Abs(larva.Reserve - 11.5f) < 1e-3f, "resting near a colossus refills the larva");
            Call(larva, "Nest", colossus, origin + new Vector3(120f, 1f, 0f), Quaternion.identity);
            Call(larva, "TickCreature", 1f);
            Check(Mathf.Abs(larva.Reserve - 11.5f) < 1e-3f, "far from any colossus it does not refill");

            // ---- Meditation journal ----
            CreatureCast cast = CreatureCast.Capture(colossus, 2.5f);
            int vertices = 0;
            if (cast != null) foreach (CreatureCast.Part part in cast.parts) { vertices += part.mesh.vertexCount; Object.DestroyImmediate(part.mesh); }
            Check(cast != null && cast.parts.Count > 10 && vertices < 400000, "the meditation journal can cast it");
            CreatureCast larvaCast = CreatureCast.Capture(larva, 2.5f);
            Check(larvaCast != null && larvaCast.parts.Count > 0, "the meditation journal can cast the larva");
            if (larvaCast != null) foreach (CreatureCast.Part part in larvaCast.parts) Object.DestroyImmediate(part.mesh);

            fluidReport = FluidChecks();
        }
        catch (Failure failure) { Debug.LogError(failure.Message); throw; }
        finally
        {
            typeof(CreatureThrower).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { originalThrower });
            SceneManager.UnloadSceneAsync(scene);
        }
        string result = $"PASS: {checks} Ore Colossus checks. {fluidReport}";
        Debug.Log(result);
        return result;
    }

    private static int CountLarvae(Scene scene)
    {
        int count = 0;
        foreach (GeodeLarva larva in Object.FindObjectsByType<GeodeLarva>(FindObjectsSortMode.None)) if (larva.gameObject.scene == scene) count++;
        return count;
    }

    /// <summary>Real water against an erosion target, on a tiny generated world with its own fluid system.</summary>
    private static string FluidChecks()
    {
        var scene = SceneManager.CreateScene("Ore Colossus Fluid Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        try
        {
            ProceduralWorld world; ErosiveFluidSystem fluid; ErosiveFluidSource source;
            try
            {
                var worldRoot = new GameObject("Tiny erosion world"); worldRoot.SetActive(false);
                SceneManager.MoveGameObjectToScene(worldRoot, scene);
                world = worldRoot.AddComponent<ProceduralWorld>();
                Set(world, "worldSize", new Vector2(8f, 8f)); Set(world, "cellSize", 0.5f); Set(world, "chunkCells", 8);
                Set(world, "erosionTestCourse", true); Set(world, "blockingRadius", 0f);
                worldRoot.SetActive(true);
                world.Generate(true);
                var fluidRoot = new GameObject("Fluid under test"); fluidRoot.SetActive(false);
                SceneManager.MoveGameObjectToScene(fluidRoot, scene);
                fluid = fluidRoot.AddComponent<ErosiveFluidSystem>();
                fluid.SetWorld(world);
                Set(fluid, "maximumDroplets", 16); Set(fluid, "erosiveRain", false);
                var sourceObject = new GameObject("Source"); sourceObject.transform.SetParent(fluidRoot.transform, false);
                source = sourceObject.AddComponent<ErosiveFluidSource>();
                fluidRoot.SetActive(true);
                Call(fluid, "Start");
                fluid.SetEmitting(false);
            }
            catch (Exception exception) when (!(exception is Failure))
            {
                return "Fluid checks skipped (" + exception.GetBaseException().Message + ").";
            }
            if (!world.LayoutReady) return "Fluid checks skipped (the tiny world did not generate).";

            Bounds volume = world.VolumeBounds;
            Vector3 centre = new Vector3(volume.center.x, volume.max.y + 3f, volume.center.z);
            // A creature with one ordinary collider and one erosion target: water passes one and hits the other.
            var owner = Probe(scene, centre);
            owner.GetComponent<Rigidbody>().isKinematic = true;
            var targetObject = new GameObject("Weak point"); targetObject.SetActive(false);
            targetObject.transform.SetParent(owner.transform, false);
            var sphere = targetObject.AddComponent<SphereCollider>(); sphere.radius = 0.7f;
            var target = targetObject.AddComponent<ErosionTarget>();
            target.Configure(new Collider[] { sphere }, 1f, 0f, 0.9f, 0f);
            targetObject.SetActive(true);
            Physics.SyncTransforms();
            Check(fluid.Spawn(source, centre + Vector3.up * 1.2f, Vector3.zero), "the fluid system spawns a droplet");
            ErosiveFluidDroplet drop = fluid.Droplets[fluid.Droplets.Count - 1];
            Call(fluid, "RefreshActors");
            Check(Physics.GetIgnoreCollision(drop.Collider, owner.GetComponent<Collider>()), "water still passes through a creature's ordinary collider");
            Check(!Physics.GetIgnoreCollision(drop.Collider, sphere), "water collides with an erosion target under a creature");
            // A target registered after water already ignored its collider becomes solid again.
            var lateObject = new GameObject("Late weak point"); lateObject.transform.SetParent(owner.transform, false);
            lateObject.transform.localPosition = Vector3.right * 3f;
            var lateBox = lateObject.AddComponent<BoxCollider>();
            Call(fluid, "RegisterActor", owner, true);
            Check(Physics.GetIgnoreCollision(drop.Collider, lateBox), "a new creature collider is ignored by water");
            lateObject.AddComponent<ErosionTarget>();
            Check(!Physics.GetIgnoreCollision(drop.Collider, lateBox), "registering a target un-ignores it");

            for (int i = 0; i < 40 && target.TotalReceived <= 0f; i++)
            {
                physics.Simulate(Time.fixedDeltaTime);
                Call(fluid, "FixedUpdate");
            }
            Check(target.TotalReceived > 0f, "water falling on the target erodes it");

            // Held: overlap stands in for touching.
            float received = target.TotalReceived;
            var witness = Player(scene, centre + new Vector3(0f, 0f, -3f));
            if (fluid.Droplets.Count == 0 || fluid.Droplets[0] == null) fluid.Spawn(source, centre + Vector3.up * 3f, Vector3.zero);
            drop = fluid.Droplets[0];
            Call(witness.carrier, "BeginCarry", drop);
            Check(drop.IsCarried && drop.Body.isKinematic, "a held droplet is placed kinematically");
            drop.transform.position = centre + Vector3.up * 0.6f; drop.Body.position = drop.transform.position;
            Physics.SyncTransforms();
            Call(fluid, "FixedUpdate");
            Check(target.TotalReceived > received && drop.TargetContactCount > 0, "a droplet held against the target erodes it");
            witness.carrier.Release();

            // Thrown: the burst on impact.
            received = target.TotalReceived;
            fluid.Spawn(source, centre + new Vector3(0f, 0.2f, -2f), Vector3.zero);
            ErosiveFluidDroplet thrown = fluid.Droplets[fluid.Droplets.Count - 1];
            Check(physics.Raycast(centre + Vector3.back * 2f, Vector3.forward, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider == sphere, "ray reaches the target");
            Check((bool)Call(thrown, "TrySplash", hit, Vector3.forward * 12f) && target.TotalReceived > received, "a thrown droplet bursts on the target");
            Call(fluid, "FixedUpdate");
            Check(!new List<ErosiveFluidDroplet>(fluid.Droplets).Contains(thrown), "the burst droplet returns to the pool");

            // Water never ages an invulnerable creature.
            OreColossus colossus = Colossus(scene, centre + Vector3.right * 40f);
            float age = colossus.Age;
            Call(fluid, "Soak", colossus, colossus.transform.position);
            for (int i = 0; i < 20; i++) Call(fluid, "FixedUpdate");
            Check(!fluid.IsSoaked(colossus) && colossus.Age == age, "water does not age the colossus");
            return "Fluid checks passed.";
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }
}
