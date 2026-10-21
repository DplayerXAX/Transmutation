using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Run in an isolated batch project: flight sweeps, nearby ride prompts, and three-bubble reserve.</summary>
public static class BoneTravelValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Transform staging = new GameObject("Bone travel validation").transform;
        int checks = 0;
        Mesh groundMesh = null;
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException("Bone travel: " + text);
            checks++;
        }
        GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name); go.transform.SetParent(staging); go.transform.position = at; return go;
        }
        try
        {
            Vector3 origin = new Vector3(2000f, 200f, 2000f);
            Transform owner = Make("Flying body", origin).transform;
            Type collision = typeof(BoneKiln).Assembly.GetType("BoneFlightCollision");
            Vector3 Move(Vector3 from, Vector3 to, float radius) => (Vector3)collision.GetMethod("Move").Invoke(null, new object[] { from, to, radius, owner, true });
            var wall = Make("Thin model wall on Default layer", origin + Vector3.forward * 10f).AddComponent<BoxCollider>();
            wall.size = new Vector3(30f, 30f, 0.2f);
            groundMesh = new Mesh();
            groundMesh.vertices = new[] { new Vector3(-50f, 0f, -50f), new Vector3(50f, 0f, -50f),
                new Vector3(-50f, 0f, 50f), new Vector3(50f, 0f, 50f) };
            groundMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            var terrain = Make("Terrain mesh", origin);
            terrain.layer = 7; terrain.AddComponent<MeshCollider>().sharedMesh = groundMesh;
            Physics.SyncTransforms();
            Vector3 from = origin + Vector3.up * 5f;
            Vector3 stopped = Move(from, from + Vector3.forward * 30f, 1.2f);
            Check(stopped.z < origin.z + 8.8f && stopped.z > origin.z + 8f, "a long flight step stops before a thin model, including Default-layer models");
            Vector3 slid = Move(from, from + Vector3.forward * 30f + Vector3.right * 5f, 1.2f);
            Check(slid.x > from.x + 2f && slid.z < origin.z + 8.8f, "blocked flight slides along the wall");
            Vector3 landed = Move(from, from + Vector3.down * 20f, 1.2f);
            Check(landed.y >= origin.y + 1.19f, "flight cannot sink through a real terrain mesh");
            Vector3 recovered = Move(wall.bounds.center, wall.bounds.center, 0.6f);
            Check(Vector3.Distance(recovered, wall.ClosestPoint(recovered)) >= 0.59f,
                $"a newly appearing model overlap is resolved (at {recovered}, wall clearance {Vector3.Distance(recovered, wall.ClosestPoint(recovered))})");
            var ownBone = Make("Own skeleton collider", from + Vector3.forward * 3f).AddComponent<BoxCollider>();
            ownBone.transform.SetParent(owner, true);
            Physics.SyncTransforms();
            Check(Vector3.Distance(Move(from, from + Vector3.forward * 6f, 0.5f), from + Vector3.forward * 6f) < 0.01f,
                "the skeleton's own bones do not block its flight");
            var mother = owner.gameObject.AddComponent<BoneKiln>();
            Set(mother, "root", owner);
            Vector3 skull = (Vector3)Call(mother, "MoveSkull", origin + Vector3.up * 10f, origin + Vector3.up * 10f + Vector3.forward * 25f, Vector3.forward);
            Check(skull.z <= origin.z + 2.5f, "the elongated skull's front is protected, rather than only its pivot");
            Type bodyStationType = typeof(BoneKiln).GetNestedType("Station", BindingFlags.NonPublic);
            object bodyStation = Activator.CreateInstance(bodyStationType, true);
            Transform bodyPiece = Make("Animated vertebra", from).transform;
            bodyPiece.SetParent(owner, true);
            bodyStationType.GetField("transform").SetValue(bodyStation, bodyPiece);
            bodyStationType.GetField("flightRadius").SetValue(bodyStation, 0.8f);
            ((IList)Get(mother, "stations")).Add(bodyStation);
            Set(mother, "head", from + Vector3.forward * 25f); Set(mother, "headDir", Vector3.forward); Set(mother, "awakeBlend", 1f);
            ((List<Vector3>)Get(mother, "trail")).Add(from);
            Call(mother, "PlaceBodyStep", 1f);
            Check(bodyPiece.position.z < origin.z + 9.2f && bodyPiece.position.z > origin.z + 8.8f,
                "animated body placement also stops a following vertebra at a model, even if its target lies beyond it");
            Check(!(bool)collision.GetMethod("ClearSegment").Invoke(null, new object[] { from, from + Vector3.forward * 20f, 0.25f, owner }),
                "rib clearance also notices a model outside the terrain layer");
            Object.DestroyImmediate(wall.gameObject);

            var child = Make("Reserve child", origin + Vector3.forward * 60f).AddComponent<BoneChild>();
            child.size = 1f;
            var beads = (List<Transform>)Get(child, "beads");
            for (int i = 0; i < 5; i++)
            {
                Transform bead = Make("Reserve anchor", child.transform.position + Vector3.forward * i).transform;
                bead.SetParent(child.transform, true); beads.Add(bead);
            }
            FireBubble Bubble() => Make("Reserve bubble", child.transform.position + Vector3.up).AddComponent<FireBubble>();
            FireBubble first = Bubble(), second = Bubble(), third = Bubble(), fourth = Bubble();
            Check(child.Catch(first) && child.Catch(second) && child.Catch(third), "three distinct bubbles bind to three rib pairs");
            Check(child.StoredBubbleCount == 3 && Mathf.Abs(child.FuelHeat - 90f) < 0.01f, "all reserve heat is available");
            Check(!child.Catch(fourth) && !fourth.IsBoundToBone && !child.Catch(first), "full capacity and double binding do not lose or duplicate bubbles");
            fourth.transform.position = child.transform.position + Vector3.right * 10f;
            Physics.SyncTransforms();
            Call(child, "UpdateHeat"); Call(child, "UpdateWarmth", 2f);
            Call(child, "SpendFlightHeat", 42f / child.flightHeatPerSecond, 0f);
            Check(first.IsDead && child.StoredBubbleCount == 2 && child.Cradled == second,
                "depleting the oldest bubble advances to the next reserve");
            Check(Mathf.Abs(second.Heat - 18f) < 0.01f && third.Heat == 30f,
                "remaining flight cost carries into the next bubble without burning the entire reserve simultaneously");
            Call(child, "UpdateHeat"); Call(child, "UpdateWarmth", 2f);
            Check(child.Warmth == 1f, "reserve fuel preserves lift when the first bubble disappears");
            float before = child.FuelHeat;
            Call(child, "SpendFlightHeat", 0f, 10f);
            Check(Mathf.Abs(before - child.FuelHeat - 4.5f) < 0.01f, "ten metres of climbing uses 4.5 additional Heat");
            var stored = (IList)Get(child, "storedFuel");
            foreach (object fuel in stored) fuel.GetType().GetField("start").SetValue(fuel, Time.time - 3f);
            Call(child, "LateUpdate");
            Check(Vector3.Distance(second.transform.position, third.transform.position) > 1f,
                "stored bubbles use separate moving rib anchors rather than overlapping at one point");
            Check(child.Catch(fourth) && child.StoredBubbleCount == 3, "a freed rib pair can catch another reserve bubble");
            Call(child, "SpendFlightHeat", 10000f, 0f);
            Check(child.StoredBubbleCount == 0 && child.FuelHeat == 0f, "all spent bubbles disappear and free all rib pairs");

            Vector3 playerAt = origin + Vector3.right * 70f + Vector3.up * 10f;
            var player = Make("Rider", playerAt);
            player.AddComponent<SmoothFirstPersonController>();
            var camera = Make("Rider camera", playerAt).AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetParent(player.transform, true);
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            Call(carrier, "Awake");
            var carriedTentacle = Make("Rider's tentacle", playerAt + Vector3.right * 3f).AddComponent<TentacleCreature>();
            Call(carrier, "BeginCarry", carriedTentacle);
            Check(carrier.CarriesTentacle, "ride-prompt fixture actually has a carried tentacle");
            var riderBody = player.GetComponent<Rigidbody>(); riderBody.position = playerAt;
            var rideMother = Make("Ride mother", playerAt + Vector3.forward * 3f).AddComponent<BoneKiln>();
            Transform rideRoot = Make("Ride skeleton", rideMother.transform.position).transform;
            Set(rideMother, "root", rideRoot);
            Transform spine = Make("Nearby back vertebra", rideMother.transform.position).transform;
            spine.SetParent(rideRoot, true);
            Transform distantHead = Make("Distant skull", playerAt + Vector3.forward * 30f).transform;
            distantHead.SetParent(rideRoot, true); Set(rideMother, "skull", distantHead);
            Type stationType = typeof(BoneKiln).GetNestedType("Station", BindingFlags.NonPublic);
            object station = Activator.CreateInstance(stationType, true);
            stationType.GetField("transform").SetValue(station, spine);
            ((IList)Get(rideMother, "stations")).Add(station);
            Physics.SyncTransforms();
            Check(rideMother.TryGetRideSeat(out Transform seat) && seat == spine,
                "a nearby back vertebra is rideable even when the skull is far away and the mother is asleep");
            var hud = Make("Ride prompt", playerAt).AddComponent<InteractionPromptHUD>();
            Set(hud, "carrier", carrier); Set(hud, "rideMother", rideMother);
            object[] prompt = { null, null };
            typeof(InteractionPromptHUD).GetMethod("ChoosePrompt", Private).Invoke(hud, prompt);
            Check(((string)prompt[1]).Contains("[C] Ride bone mother"), "approaching the ride seat displays C before held-object hints");
            var occluder = Make("Occluding terrain/model", playerAt + Vector3.forward * 1.5f).AddComponent<BoxCollider>();
            occluder.size = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();
            Check(!rideMother.TryGetRideSeat(out _), "a nearby seat behind a wall cannot be mounted");
            Object.DestroyImmediate(occluder.gameObject);
            BoneRide.GetOn(rideMother, rideRoot, spine, Array.Empty<Transform>());
            Check(BoneRide.Mount == (object)rideMother && riderBody.isKinematic, "mounting transfers the player to the mother");
            prompt = new object[] { null, null };
            typeof(InteractionPromptHUD).GetMethod("ChoosePrompt", Private).Invoke(hud, prompt);
            Check(((string)prompt[1]).Contains("[C] Dismount"), "riding displays C to dismount");
            BoneRide.GetOff();
            Check(BoneRide.Mount == null && !riderBody.isKinematic, "dismount restores player movement");
            Debug.Log($"Bone travel validation passed: {checks} assertions.");
        }
        finally
        {
            BoneRide.GetOff(); Object.DestroyImmediate(staging.gameObject);
            if (groundMesh != null) Object.DestroyImmediate(groundMesh);
        }
    }
}
