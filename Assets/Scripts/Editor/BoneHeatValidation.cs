using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Run with -executeMethod BoneHeatValidation.Run in an isolated batch project.</summary>
public static class BoneHeatValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunAll()
    {
        BoneChildCatchValidation.Run();
        Run();
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var staging = new GameObject("Bone heat validation").transform;
        int checks = 0;
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException("Bone heat: " + text);
            checks++;
        }
        FireBubble Bubble(Vector3 position)
        {
            var go = new GameObject("Fuel bubble");
            go.transform.SetParent(staging);
            go.transform.position = position;
            return go.AddComponent<FireBubble>();
        }

        try
        {
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var childObject = new GameObject("Child");
            childObject.transform.SetParent(staging);
            childObject.transform.position = origin;
            var child = childObject.AddComponent<BoneChild>();
            FireBubble fuel = Bubble(origin);
            Check(fuel.Heat == 0f && child.Catch(fuel), "a zero-Heat fire bubble still binds");
            Check(fuel.Heat == 30f && fuel.BoneFuelFraction == 1f, "binding gives a finite 30 Heat reserve");
            Call(child, "UpdateHeat");
            Call(child, "UpdateWarmth", 2f);
            Check(child.Warmth == 1f && child.FlightStrength == 1f, "fuel warms the child to full lift");
            float before = fuel.Heat;
            Call(child, "SpendFlightHeat", 10f, 0f);
            float cruiseCost = before - fuel.Heat;
            Check(Mathf.Abs(cruiseCost - 11.25f) < 0.01f, "level flight burns fifty percent more heat at the tuned rate");
            before = fuel.Heat;
            Call(child, "SpendFlightHeat", 10f, 10f);
            Check(before - fuel.Heat > cruiseCost, "actual ascent costs more than level flight");
            before = fuel.Heat;
            Call(child, "SpendFlightHeat", 0f, 0f);
            Check(fuel.Heat == before, "no time or ascent means no fuel spend");
            Call(child, "SpendFlightHeat", 10000f, 0f);
            Check(fuel.IsDead && !fuel.gameObject.activeSelf && child.Cradled == null, "exhaustion removes the bubble and releases the ribs");
            Set(child, "heat", 0f);
            Call(child, "UpdateWarmth", 3f);
            Check(child.Warmth > 0f && child.Warmth < 1f && child.FlightStrength > 0f && child.FlightStrength < 1f,
                "colour and lift fade rather than switching off immediately");
            Call(child, "UpdateWarmth", 20f);
            Check(child.Warmth == 0f && child.FlightStrength == 0f, "a fully cooled child has no lift");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(staging);
            floor.layer = 7;
            floor.transform.position = origin - Vector3.up * 0.5f;
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();
            Set(child, "head", origin + Vector3.up * 10f);
            Set(child, "headDir", Vector3.up);
            Set(child, "rest", origin);
            Call(child, "Fly", 0.1f);
            float firstHeight = ((Vector3)Get(child, "head")).y;
            Check(firstHeight < origin.y + 10f && firstHeight > origin.y + 8f, "an exhausted child descends without teleporting or continuing to climb");
            for (int i = 0; i < 100; i++) Call(child, "Fly", 0.1f);
            float finalHeight = ((Vector3)Get(child, "head")).y;
            Check(finalHeight < firstHeight && finalHeight >= origin.y + 0.45f, "the cooled child settles above the ground");
            Transform leftRib = new GameObject("Cooled rib L").transform;
            Transform rightRib = new GameObject("Cooled rib R").transform;
            leftRib.SetParent(child.transform);
            rightRib.SetParent(child.transform);
            ((List<Transform[]>)Get(child, "ribs")).Add(new[] { leftRib, rightRib });
            var ribBeats = (List<float>)Get(child, "ribBeat");
            ribBeats.Add(0f);
            Call(child, "BeatRibs", 1f, true);
            Check(ribBeats[0] < -50f, "cooled ribs fold instead of continuing full wingbeats while descending");
            Check(child.Catch(Bubble(origin)), "the empty child can accept fresh fuel");

            var motherObject = new GameObject("Mother");
            motherObject.transform.SetParent(staging);
            motherObject.transform.position = origin + Vector3.right * 30f;
            var mother = motherObject.AddComponent<BoneKiln>();
            Set(mother, "restingHeatPerSecond", 0f); // Keep flight-cost assertions independent of resting loss.
            Transform root = new GameObject("Skeleton").transform;
            root.SetParent(motherObject.transform, false);
            Set(mother, "root", root);
            Set(mother, "path", new List<Vector3> { Vector3.zero, Vector3.back, Vector3.back * 2f });
            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.transform.SetParent(root, false);
            Renderer renderer = surface.GetComponent<Renderer>();
            Collider solid = surface.GetComponent<Collider>();
            var contact = surface.AddComponent<BoneFireContact>();
            contact.Setup(mother, renderer);
            ((List<BoneFireContact>)Get(mother, "fireContacts")).Add(contact);
            ((List<Renderer>)Get(mother, "boneRenderers")).Add(renderer);
            var childCollider = new GameObject("Child collider remains usable").AddComponent<SphereCollider>();
            childCollider.transform.SetParent(root, false);
            childCollider.transform.localPosition = Vector3.right * 10f;

            FireBubble first = Bubble(root.position + Vector3.forward);
            Check(mother.Catch(first, solid, root.position, Vector3.forward), "mother accepts zero-Heat bubbles on her body");
            Check(first.transform.parent == surface.transform && first.IsBoundToBone, "the bubble is attached to the moving bone segment");
            Check(!mother.Catch(first, solid, root.position, Vector3.forward), "a bound bubble cannot be counted twice");
            Call(mother, "StepHeat", 10f);
            Call(mother, "UpdateWake", 10f);
            Check(Mathf.Abs(mother.Warmth - 1f / 15f) < 0.01f && !mother.Awake, "one of fifteen bubbles only warms part of the mother");
            float lower = (float)Call(mother, "SurfaceWarmth", 0f);
            float upper = (float)Call(mother, "SurfaceWarmth", 1f);
            Check(lower > upper && lower > 0f && upper == 0f, "warm colour starts at the spine attachment before the rib tips");
            Set(mother, "restingHeatPerSecond", 0.15f);
            float storedHeat = first.Heat;
            float initialWarmth = mother.Warmth;
            Call(mother, "StepHeat", 20f);
            Check(first.Heat < storedHeat && mother.Warmth < initialWarmth && !mother.Awake,
                "incomplete accumulation loses both fuel and warmth while still asleep");
            float remainingHeat = first.Heat;
            Call(mother, "StepHeat", 0f);
            Check(first.Heat == remainingHeat, "zero elapsed time cannot drain resting fuel");
            Call(mother, "StepHeat", 1000f);
            Check(first.IsDead && mother.AttachedFireCount == 0 && mother.Warmth == 0f,
                "unattended partial accumulation eventually burns out completely");
            Set(mother, "restingHeatPerSecond", 0f);
            first = Bubble(root.position + Vector3.forward);
            Check(mother.Catch(first, solid, root.position, Vector3.forward), "the cooled mother accepts fresh accumulation");
            for (int i = 1; i < 15; i++)
            {
                Check(mother.Catch(Bubble(root.position + Vector3.forward * (i + 1)), solid, root.position, Vector3.forward),
                    "additional bubble " + i + " sticks to the mother");
                if (i == 13)
                {
                    Call(mother, "StepHeat", 10f);
                    Call(mother, "UpdateWake", 10f);
                    Check(!mother.Awake, "fourteen bubbles cannot activate the mother even when almost fully warm");
                }
            }
            Call(mother, "StepHeat", 10f);
            Check(mother.RequiredFireBubbles == 15 && mother.AttachedFireCount == 15 && mother.Warmth >= 0.999f, "fifteen full bubbles warm the whole body");
            Check((float)Call(mother, "SurfaceWarmth", 1f) == 1f, "warmth reaches the upper skeleton");
            Call(mother, "UpdateWake", 2f);
            Check(!mother.Awake, "full warmth must persist through the wake delay");
            Call(mother, "UpdateWake", 1f);
            Check(mother.Awake && mother.Flying, "a fully warmed mother wakes into flight");
            Check(!solid.enabled && childCollider.enabled, "mother flight releases physical bones without disabling the children");

            var thrower = new GameObject("Thrower").AddComponent<CreatureThrower>();
            thrower.transform.SetParent(staging);
            FireBubble next = Bubble(root.position - Vector3.forward * 4f);
            Physics.SyncTransforms();
            object[] castArgs = { root.position - Vector3.forward * 4f, root.position + Vector3.forward * 4f, next, null };
            bool hit = (bool)typeof(CreatureThrower).GetMethod("FindFlightHit", Private).Invoke(thrower, castArgs);
            var surfaceHit = (RaycastHit)castArgs[3];
            Check(hit && surfaceHit.collider.isTrigger && surfaceHit.collider.GetComponent<BoneFireContact>() == contact,
                "throws still find the moving mother's fuel contact after waking");
            Check(mother.Catch(next, surfaceHit.collider, surfaceHit.point, surfaceHit.normal), "the mother can be refuelled in flight");

            Call(mother, "SpendFlightHeat", 5f, 0f);
            Check(Mathf.Abs(first.Heat - 12f) < 0.01f, "mother flight spends 18 heat over five seconds, oldest bubble first");
            Call(mother, "SpendFlightHeat", 10f, 30f);
            Check(first.IsDead, "climbing consumes extra heat and moves on to the next bubble");
            Call(mother, "SpendFlightHeat", 10000f, 0f);
            Call(mother, "StepHeat", 1f);
            Check(mother.AttachedFireCount == 0 && mother.Warmth > 0f && mother.Warmth < 1f,
                "all fuel disappears while the mother cools gradually");
            Call(mother, "StepHeat", 30f);
            Check(mother.Warmth == 0f && mother.FlightStrength == 0f, "the cooled mother loses flight capability");

            // A curved neck starts below the mouth. The leader must be the outdoor tail, not the skull in the cave.
            var exitObject = new GameObject("Reverse exit mother");
            exitObject.transform.SetParent(staging);
            var exitMother = exitObject.AddComponent<BoneKiln>();
            Transform exitRoot = new GameObject("Exit skeleton").transform;
            exitRoot.SetParent(exitObject.transform);
            exitRoot.position = origin + Vector3.right * 70f;
            Set(exitMother, "root", exitRoot);
            Set(exitMother, "mouth", exitRoot.position);
            Set(exitMother, "warmth", 1f);
            var exitPath = new List<Vector3> { new Vector3(0f, -8f, 0f), new Vector3(0f, -3f, 1f),
                new Vector3(0f, 1f, 3f), new Vector3(0f, 2f, 8f) };
            Set(exitMother, "path", exitPath);
            var worldObject = new GameObject("Exit terrain layout");
            worldObject.transform.SetParent(staging);
            worldObject.transform.position = exitRoot.position;
            var world = worldObject.AddComponent<ProceduralWorld>();
            Set(world, "sinkholeCount", 0);
            Set(world, "surfaceAmplitude", 0f);
            Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));
            Set(exitMother, "world", world);
            Set(mother, "world", world);
            var hoverFuel = Bubble(root.position + Vector3.up);
            Check(mother.Catch(hoverFuel, solid, root.position, Vector3.up), "cooled mother accepts fuel for lift checks");
            Set(mother, "flying", false);
            world.ColumnHeights(root.position, out float hoverGround, out _, out _);
            Set(mother, "head", new Vector3(root.position.x, hoverGround + 10f, root.position.z));
            Call(mother, "StepAirborneHeat", 2f, hoverGround + 10f);
            Check(Mathf.Abs(hoverFuel.Heat - 22.8f) < 0.01f, "level hovering spends heat even without climbing or a flight-mode flag");
            Set(mother, "head", new Vector3(root.position.x, hoverGround + 9f, root.position.z));
            Call(mother, "StepAirborneHeat", 2f, hoverGround + 10f);
            Check(Mathf.Abs(hoverFuel.Heat - 15.6f) < 0.01f, "descending glide still pays the continuous lift cost");
            Set(mother, "head", new Vector3(root.position.x, hoverGround + 3f, root.position.z));
            Call(mother, "StepAirborneHeat", 2f, hoverGround + 3f);
            Check(Mathf.Abs(hoverFuel.Heat - 15.6f) < 0.01f, "ground crawling does not pay an airborne cost");
            exitMother.Wake();
            Vector3 tail = exitRoot.TransformPoint(exitPath[exitPath.Count - 1]);
            Check(Vector3.Distance((Vector3)Get(exitMother, "head"), tail) < 0.001f,
                "activation starts with the outdoor tail as leader");
            float length = (float)Get(exitMother, "pathLength");
            Vector3 skullBefore = (Vector3)Call(exitMother, "PointBehind", length);
            Check(Vector3.Distance(skullBefore, exitRoot.TransformPoint(exitPath[0])) < 0.001f,
                "tail-first initialization preserves the skull's original cave position");
            Call(exitMother, "BackOut", 0.25f);
            Check(Vector3.Distance((Vector3)Get(exitMother, "head"), tail) < 0.001f,
                "the cinematic first gives the ribs time to fold instead of jerking the body immediately");
            Call(exitMother, "BackOut", 3f);
            Vector3 skullAfter = (Vector3)Call(exitMother, "PointBehind", length);
            Vector3 leader = (Vector3)Get(exitMother, "head");
            Check(leader.z > tail.z && skullAfter.y > skullBefore.y && skullAfter.z >= skullBefore.z,
                "the first movement backs outside and draws the skull up its existing neck, never deeper into the cave");
            Set(exitMother, "backOutDistance", length + 8f);
            Vector3 expectedSkull = (Vector3)Call(exitMother, "PointBehind", length);
            Call(exitMother, "FinishBackingOut");
            Check(!(bool)Get(exitMother, "emerging") && Vector3.Distance((Vector3)Get(exitMother, "head"), expectedSkull) < 0.001f,
                "handoff to head-first roaming preserves the skull anchor without teleporting to the tail");
            Debug.Log($"Bone heat validation passed: {checks} assertions.");
        }
        finally { Object.DestroyImmediate(staging.gameObject); }
    }
}
