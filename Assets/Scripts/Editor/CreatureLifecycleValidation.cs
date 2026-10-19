using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Additional corpse and Inspector checks invoked by BirdLifeValidation.</summary>
internal static class CreatureLifecycleValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Creature lifecycle: " + message);
        checks++;
    }

    internal static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("Creature Lifecycle " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var staging = new GameObject("Lifecycle Staging"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);

        T Create<T>() where T : Component
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.transform.SetParent(staging.transform, false);
            root.AddComponent<Rigidbody>();
            var component = root.AddComponent<T>();
            root.transform.SetParent(null, true);
            return component;
        }
        Creature Prefab(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/" + name + ".prefab");
            Check(asset != null, name + " prefab exists");
            var root = Object.Instantiate(asset, staging.transform);
            root.transform.SetParent(null, true);
            return root.GetComponent<Creature>();
        }
        try
        {
            var probe = Create<CreatureLifecycleValidationProbe>();
            int deaths = 0;
            probe.Died += dead => { Check(dead.IsDead && !dead.SimulationEnabled && dead.Age == 0f, "death event observes committed state"); deaths++; dead.Die(); };
            probe.BeginPet(Vector3.zero, Vector3.up);
            probe.Die(); probe.Die(); probe.SetSimulationEnabled(true);
            Check(deaths == 1 && probe.DeathCount == 1 && probe.IsDead && !probe.SimulationEnabled, "death and reentrant events happen once and cannot revive");
            typeof(Creature).GetMethod("AdvanceAge", Private).Invoke(probe, new object[] { 100f });
            Check(probe.AgeDeathCount == 0, "direct death never becomes age death even though death sets Age to zero");
            Check(!probe.IsPetted && probe.gameObject.activeInHierarchy && probe.GetComponent<Collider>().enabled, "death releases petting and retains active solid body");
            var rb = probe.GetComponent<Rigidbody>();
            Check(!rb.isKinematic && rb.useGravity, "mobile corpse has passive gravity physics");
            rb.position = Vector3.up * 10f;
            scene.GetPhysicsScene().Simulate(0.1f);
            Check(rb.position.y < 10f && rb.linearVelocity.y < 0f, "retained corpse falls under real physics");

            // Reload the serialized component state onto an inactive instance: no death event is replayed.
            var reloadRoot = GameObject.CreatePrimitive(PrimitiveType.Cube); reloadRoot.transform.SetParent(staging.transform, false);
            var reloadBody = reloadRoot.AddComponent<Rigidbody>(); reloadBody.isKinematic = true; reloadBody.useGravity = false;
            var reloaded = reloadRoot.AddComponent<CreatureLifecycleValidationProbe>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(probe), reloaded);
            int reloadDeaths = 0; reloaded.Died += _ => reloadDeaths++;
            reloadRoot.transform.SetParent(null, true);
            reloaded.SetSimulationEnabled(true);
            Check(reloaded.IsDead && !reloaded.SimulationEnabled && reloaded.Age == 0f && reloadDeaths == 0,
                "serialized death survives reinitialization without another event");
            Check(!reloadBody.isKinematic && reloadBody.useGravity, "dead initialization restores corpse physics");
            reloaded.enabled = false; reloaded.enabled = true;
            Check(reloaded.IsDead && !reloaded.SimulationEnabled, "component re-enable cannot revive");

            var first = Create<CreatureLifecycleValidationProbe>();
            var second = Create<CreatureLifecycleValidationProbe>(); second.SetSimulationEnabled(false);
            var editor = (CreatureEditor)Editor.CreateEditor(new Object[] { first, second });
            try
            {
                VisualElement panel = editor.CreateInspectorGUI();
                Check(panel.Q<Toggle>("is-dead") != null && !panel.Q<Toggle>("is-dead").enabledSelf,
                    "inherited Inspector exposes read-only death");
                Check(panel.Q<Toggle>("simulation-running").showMixedValue, "Inspector represents mixed simulation states");
                Check(panel.Q<Button>("kill-creatures").enabledSelf, "Kill is available for living scene creatures");
                CreatureEditor.Kill(new Creature[] { first, second, probe });
                Check(first.IsDead && second.IsDead && probe.DeathCount == 1, "multi-selection Kill uses normal once-only death");
            }
            finally { Object.DestroyImmediate(editor); }

            foreach (string name in new[] { "BirdCreature", "SkySpider", "GooCreature", "Bubble", "BubbleFlower", "BounceShroom", "CorruptSeed", "CompostCreature", "SkyWeb", "TrappingBubble", "FireSource", "Fire Flower" })
            {
                Creature creature = Prefab(name);
                int id = creature.GetInstanceID();
                var renderers = creature.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                var solids = creature.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                int count = 0; creature.Died += _ => count++;
                creature.Die(); creature.Die(); creature.SetSimulationEnabled(true);
                Check(creature.GetInstanceID() == id && creature.gameObject.activeInHierarchy && creature.IsDead && !creature.SimulationEnabled && count == 1,
                    name + " retains the same active dead object exactly once");
                Check(renderers.All(r => r != null && r.enabled) && solids.All(c => c != null && c.enabled), name + " retains visible meshes and solid colliders");
                var body = creature.GetComponent<Rigidbody>();
                Check(!creature.CanUseWhileCarried && string.IsNullOrEmpty(creature.InteractionPrompt), name + " offers no dead creature abilities");
                if (creature is CompostCreature || creature is SkyWeb)
                    Check(!creature.CanBeCarried && (body == null || body.isKinematic), name + " remains a stationary unportable structure");
                else if (body != null)
                    Check(!body.isKinematic && body.useGravity, name + " has passive corpse physics");
                if (creature is BirdCreature bird) Check(bird.FlockManager == null && !bird.IsFlying, "dead bird leaves flock and stops flight");
                if (creature is SkySpider spider) Check(spider.CurrentWeb == null, "dead spider releases its builder role");
                if (creature is TrappingBubble bubble) { bubble.Pop(); Check(!bubble.IsPopped, "dead bubble cannot pop away after death"); }
                Object.DestroyImmediate(creature.gameObject);
            }

            var fuzz = Create<FuzzCreature>();
            // Avoid taking ownership of creatures in the user's other loaded scenes.
            var chain = (System.Collections.ICollection)typeof(FuzzCreature).GetField("Chain", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (chain.Count == 0 && FuzzCreature.RopeLength == 0)
            {
                fuzz.Anchor();
                Check(fuzz.InRope, "Fuzz starts anchored for cleanup test");
            }
            fuzz.Die();
            Check(!fuzz.InRope && !fuzz.IsFollowing && !fuzz.Caught && !fuzz.GetComponent<Rigidbody>().isKinematic,
                "dead Fuzz leaves chains and ropes and falls");
            fuzz.Anchor(); Check(!fuzz.InRope, "dead Fuzz cannot re-anchor");

            var tentacle = Create<TentacleCreature>();
            tentacle.PinArm(Vector3.forward); tentacle.Suspend(true); tentacle.Die();
            Check(tentacle.PinnedCount == 0 && !tentacle.Suspended && !tentacle.Holding && !tentacle.IsGripping,
                "dead tentacle releases pins, suspension, prey, and grips");
            Check(tentacle.PinArm(Vector3.forward) == -1 && !tentacle.GetComponent<Rigidbody>().isKinematic,
                "dead tentacle cannot use arms and falls");

            var occupied = (TrappingBubble)Prefab("TrappingBubble");
            var occupant = Create<CreatureLifecycleValidationProbe>();
            Check(occupied.TryCapture(occupant.gameObject), "live bubble captures occupant");
            occupied.Die();
            Check(!occupied.IsOccupied && !occupant.IsBubbleCaptured && occupant.SimulationEnabled && !occupied.IsPopped && occupied.gameObject.activeInHierarchy,
                "bubble death releases occupant without deleting its own body");

            var captor = (TrappingBubble)Prefab("TrappingBubble");
            var captive = Create<CreatureLifecycleValidationProbe>();
            Check(captor.TryCapture(captive.gameObject), "bubble captures creature before death");
            captive.Die();
            Check(!captive.IsBubbleCaptured && !captive.SimulationEnabled && captive.GetComponent<Collider>().enabled && captive.GetComponent<Rigidbody>().useGravity,
                "captive death releases bubble ownership and restores corpse collisions and gravity");

            var web = (SkyWeb)Prefab("SkyWeb");
            var webPrey = Create<CreatureLifecycleValidationProbe>();
            Check(web.TryCapture(webPrey.GetComponent<Rigidbody>(), webPrey.transform.position), "live web captures prey");
            web.Die();
            Check(web.CaptureCount == 0 && !webPrey.GetComponent<WebCapture>().IsCaptured && webPrey.SimulationEnabled,
                "web death releases owned captures without killing occupants");

            var eater = Create<FireEater>(); eater.Die();
            var meal = (FireBubble)Prefab("Bubble");
            var stateField = typeof(FireBubble).GetField("currentState", Private);
            stateField.SetValue(meal, Enum.Parse(stateField.FieldType, "Inanimate"));
            typeof(FireEater).GetMethod("TryEat", Private).Invoke(eater, new object[] { meal.GetComponent<Collider>() });
            Check((float)typeof(FireEater).GetField("digestionTimer", Private).GetValue(eater) == 0f,
                "dead Fire Eater cannot feed through contact callbacks");

            string result = "PASS: " + checks + " shared creature lifecycle checks.";
            Debug.Log(result);
            return result;
        }
        finally
        {
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
