using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repeatable integration checks using real colliders and disposable local physics scenes.</summary>
public static class AsuraCombatValidation
{
    private static int checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException("Asura: " + description); checks++; }
    private static void Set(Object target, string name, object value)
    {
        Type type = target.GetType(); FieldInfo field = null;
        while (type != null && field == null) { field = type.GetField(name, Private); type = type.BaseType; }
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }
    private static void Age(Creature creature, float age) => typeof(Creature).GetMethod("SetRemainingAge", Private | BindingFlags.Public).Invoke(creature, new object[] { age });
    private static void Advance(Creature creature, float delta) => typeof(Creature).GetMethod("AdvanceAge", Private).Invoke(creature, new object[] { delta });
    private static void Resolve(Creature creature, CreaturePolarity polarity) => typeof(Creature).GetMethod("SetResolvedPolarity", Private).Invoke(creature, new object[] { polarity });

    [MenuItem("Tools/Creatures/Asura/Profile Groups (Play Mode)")]
    public static string ProfileGroups()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var report = new System.Text.StringBuilder("Isolated Editor samples; no population limit implied.\nUnits | Search ms/unit | Physics ms/step | Search allocated bytes | Projectile bodies | Retained memory delta MiB\n");
        var nextSearch = typeof(CreatureCombat).GetField("nextSearch", Private);
        object immediate = 0f;
        foreach (int count in new[] { 6, 24, 60 })
        {
            var scene = SceneManager.CreateScene("Asura Profile " + count + " " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            long memoryBefore = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            try
            {
                var staging = new GameObject("Profile staging"); staging.SetActive(false); SceneManager.MoveGameObjectToScene(staging, scene);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = Vector3.down * 0.5f; floor.transform.localScale = new Vector3(60f, 1f, 60f);
                var units = new List<CreatureCombat>(count);
                string[] roles = { "MeleeAsura", "RangedAsura", "GeneralAsura" };
                for (int i = 0; i < count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AsuraPrototypeSetup.Folder + "/" + roles[i % 3] + ".prefab");
                    var root = Object.Instantiate(prefab, new Vector3((i % 10) * 3f - 15f, 0.05f, (i / 10) * 3f - 8f), Quaternion.identity, staging.transform);
                    Set(root.GetComponent<Creature>(), "initialPolarity", (i % 2 == 0) ? CreaturePolarity.Light : CreaturePolarity.Dark);
                    root.transform.SetParent(null, true); units.Add(root.GetComponent<CreatureCombat>());
                }
                void SearchPass()
                {
                    foreach (var unit in units) { unit.Cancel(); nextSearch.SetValue(unit, immediate); unit.Tick(0.01f); }
                }
                for (int warm = 0; warm < 4; warm++) SearchPass();
                var watch = new System.Diagnostics.Stopwatch();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                watch.Start(); for (int pass = 0; pass < 20; pass++) SearchPass(); watch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                double searchMs = watch.Elapsed.TotalMilliseconds / (20 * count);
                var shots = new List<ProjectileCreature>();
                var shotPrefab = AssetDatabase.LoadAssetAtPath<BasicProjectile>(AsuraPrototypeSetup.Folder + "/BasicProjectile.prefab");
                for (int i = 0; i < count; i += 4)
                {
                    var owner = units[i].GetComponent<Creature>();
                    shots.Add(ProjectileCreature.Spawn(shotPrefab, owner, null, owner.AimPosition + Vector3.forward,
                        Vector3.forward * 18f, 12f, 4f, 12f));
                }
                var physics = scene.GetPhysicsScene();
                for (int warm = 0; warm < 10; warm++) physics.Simulate(0.02f);
                watch.Restart();
                for (int step = 0; step < 120; step++)
                {
                    foreach (var unit in units)
                    {
                        var body = unit.GetComponent<Rigidbody>();
                        if (!body.isKinematic) body.AddForce(Vector3.forward * 0.2f, ForceMode.Acceleration);
                    }
                    physics.Simulate(0.02f);
                }
                watch.Stop();
                long retained = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - memoryBefore;
                report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} | {1:F4} | {2:F3} | {3} | {4} | {5:F2}",
                    count, searchMs, watch.Elapsed.TotalMilliseconds / 120, allocated, shots.Count, retained / 1048576.0));
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }
        string result = report.ToString(); Debug.Log(result); return result;
    }

    [MenuItem("Tools/Creatures/Asura/Validate Gameplay (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        CreatureThrower originalThrower = CreatureThrower.Instance;
        var scene = SceneManager.CreateScene("Asura Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var staging = new GameObject("Asura Validation Staging"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        var physics = scene.GetPhysicsScene();
        T Spawn<T>(string prefabName, Vector3 position) where T : Creature
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AsuraPrototypeSetup.Folder + "/" + prefabName + ".prefab");
            var root = Object.Instantiate(prefab, position, Quaternion.identity, staging.transform);
            root.transform.SetParent(null, true);
            return root.GetComponent<T>();
        }
        CreatureLifecycleValidationProbe Probe(Vector3 position, CreaturePolarity polarity = CreaturePolarity.Light)
        {
            var root = new GameObject("Combat witness"); root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = position;
            root.AddComponent<Rigidbody>().useGravity = false; root.AddComponent<BoxCollider>();
            var result = root.AddComponent<CreatureLifecycleValidationProbe>();
            Set(result, "initialPolarity", polarity); root.SetActive(true); return result;
        }
        try
        {
            var witness = Probe(Vector3.zero, CreaturePolarity.Random);
            CreaturePolarity first = witness.Polarity;
            Check(first == CreaturePolarity.Light || first == CreaturePolarity.Dark, "Random only produces a resolved polarity");
            witness.SetSimulationEnabled(false); witness.SetSimulationEnabled(true);
            witness.gameObject.SetActive(false); witness.gameObject.SetActive(true);
            Check(witness.Polarity == first, "pause and re-enable retain identity");
            Check(!witness.ApplyDamage(float.NaN) && !witness.ApplyDamage(float.PositiveInfinity) && !witness.ApplyDamage(-1f), "invalid damage rejected");
            float age = witness.Age, heat = witness.Heat;
            witness.ApplyDamage(25f);
            Check(witness.Health == 75f && witness.Age == age && witness.Heat == heat, "health is individual and independent of ecology");
            var clone = Object.Instantiate(witness, new Vector3(100f, 0f, 0f), Quaternion.identity);
            Check(clone.Health == clone.MaxHealth, "instantiation does not copy depleted health");
            Object.DestroyImmediate(clone.gameObject);
            int damageDeaths = 0;
            witness.Died += dead => { Check(dead.DeathCause == CreatureDeathCause.Damage, "listeners see committed damage cause"); damageDeaths++; };
            witness.ApplyDamage(1000f); witness.ApplyDamage(1000f); witness.Die();
            Check(damageDeaths == 1 && witness.DeathCount == 1 && witness.AgeDeathCount == 0, "lethal damage uses virtual death exactly once");
            Check(witness.CanBeAITargeted, "AI eligibility does not exclude remains");
            Object.DestroyImmediate(witness.gameObject);

            var aged = Probe(Vector3.zero); Age(aged, 0f);
            Check(aged.IsDead && aged.DeathCause == CreatureDeathCause.Age && aged.AgeDeathCount == 1, "age expiry has exclusive age hook");
            Object.DestroyImmediate(aged.gameObject);
            var eater = Probe(Vector3.zero); Set(eater, "dietTags", Creature.FoodTag.Everything); Set(eater, "canEatSameType", true);
            var food = Probe(Vector3.right, CreaturePolarity.None); int meals = 0;
            food.Died += dead => { Check(dead.DeathCause == CreatureDeathCause.Consumed, "consumption records its cause before hooks"); meals++; };
            Check(eater.TryConsumeFood(food) && !eater.TryConsumeFood(food) && meals == 1 && food.ConsumptionCount == 1,
                "None contact consumption remains valid and is claimed once");
            Object.DestroyImmediate(food.gameObject); Object.DestroyImmediate(eater.gameObject);

            var melee = Spawn<MeleeAsura>("MeleeAsura", Vector3.zero);
            var target = Probe(new Vector3(0f, 0.65f, 1.2f), CreaturePolarity.Dark);
            var combat = melee.GetComponent<CreatureCombat>();
            Check(combat.Eligible(target, 20f), "opposite ordinary creatures are eligible regardless of tags");
            Resolve(target, CreaturePolarity.None); Check(!combat.Eligible(target, 20f), "None excluded from enemy selection");
            Resolve(target, CreaturePolarity.Light); Check(!combat.Eligible(target, 20f), "same polarity excluded");
            Resolve(target, CreaturePolarity.Dark); target.GetComponent<Collider>().enabled = false;
            Check(!combat.Eligible(target, 20f), "disabled bodies excluded"); target.GetComponent<Collider>().enabled = true;
            physics.Simulate(0.001f);
            var strike = melee.GetComponent<MeleeAttack>();
            Check(strike.Release(melee, target, null) && target.Health == 80f, "short physical melee strike deals one hit");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0f, 0.65f, 0.65f); wall.transform.localScale = new Vector3(2f, 2f, 0.15f);
            physics.Simulate(0.001f);
            Check(!strike.Release(melee, target, null) && target.Health == 80f, "terrain blocks melee");
            Object.DestroyImmediate(wall); Object.DestroyImmediate(target.gameObject);
            Object.DestroyImmediate(melee.gameObject);

            var emitter = Probe(new Vector3(0f, 0f, -8f));
            var basic = Spawn<BasicProjectile>("BasicProjectile", Vector3.zero);
            Check(basic.Polarity == CreaturePolarity.None && basic.MaxHealth == 5f, "basic projectile has None polarity and authored health");
            basic.Launch(emitter, null, Vector3.forward * 18f, 12f, 4f, 12f);
            var intercept = Probe(new Vector3(0f, 0f, 2f));
            var extra = new GameObject("Compound hitbox"); extra.transform.SetParent(intercept.transform, false);
            extra.AddComponent<BoxCollider>().size = Vector3.one * 0.9f;
            for (int i = 0; i < 15; i++) physics.Simulate(0.02f);
            Check(basic.FlightState == ProjectileFlightState.Resolved && basic.IsDead && intercept.Health == 88f,
                "real collision damages compound hitboxes once and retires the projectile for removal");
            basic.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 18f;
            for (int i = 0; i < 5; i++) physics.Simulate(0.02f);
            Check(intercept.Health == 88f, "resolved impact cannot damage again before deferred removal");
            Object.DestroyImmediate(basic.gameObject); Object.DestroyImmediate(intercept.gameObject);

            var shot = Spawn<BasicProjectile>("BasicProjectile", emitter.transform.position);
            shot.Launch(emitter, null, Vector3.forward * 18f, 12f, 4f, 12f);
            Check(Physics.GetIgnoreCollision(shot.GetComponent<Collider>(), emitter.GetComponent<Collider>()), "launch protects only the source pair");
            for (int i = 0; i < 4; i++) physics.Simulate(0.02f);
            typeof(ProjectileCreature).GetMethod("TickCreature", Private).Invoke(shot, new object[] { 0.1f });
            Check(!Physics.GetIgnoreCollision(shot.GetComponent<Collider>(), emitter.GetComponent<Collider>()), "source collision restored after leaving body");
            Object.DestroyImmediate(shot.gameObject);

            var explosive = Spawn<ExplosiveProjectile>("ExplosiveProjectile", Vector3.zero);
            explosive.Launch(emitter, null, Vector3.forward * 12f, 60f, 4f, 12f);
            int explosions = ExplosionResolver.ResolvedCount;
            Age(explosive, 0f);
            Check(explosive.DeathCause == CreatureDeathCause.Age && ExplosionResolver.ResolvedCount == explosions, "explosive age expiry dissipates");
            Object.DestroyImmediate(explosive.gameObject);
            explosive = Spawn<ExplosiveProjectile>("ExplosiveProjectile", Vector3.zero);
            explosive.Launch(emitter, null, Vector3.forward, 60f, 4f, 12f);
            explosive.BecomeSpent(); explosive.ApplyDamage(1000f);
            Check(ExplosionResolver.ResolvedCount == explosions, "spent explosive remains harmless under lethal damage");
            Object.DestroyImmediate(explosive.gameObject);
            explosive = Spawn<ExplosiveProjectile>("ExplosiveProjectile", Vector3.zero);
            explosive.Launch(emitter, null, Vector3.forward, 60f, 4f, 12f);
            Set(emitter, "dietTags", Creature.FoodTag.Everything);
            Check(emitter.TryConsumeFood(explosive) && explosive.DeathCause == CreatureDeathCause.Consumed &&
                ExplosionResolver.ResolvedCount == explosions, "consumed flying explosive does not detonate");
            Object.DestroyImmediate(explosive.gameObject);
            explosive = Spawn<ExplosiveProjectile>("ExplosiveProjectile", Vector3.zero);
            explosive.Launch(emitter, null, Vector3.forward, 60f, 4f, 12f);
            var chain = Spawn<ExplosiveProjectile>("ExplosiveProjectile", Vector3.right);
            chain.Launch(emitter, null, Vector3.forward, 60f, 4f, 12f);
            physics.Simulate(0.001f);
            explosive.ApplyDamage(1000f);
            Check(explosive.IsDead && chain.IsDead && ExplosionResolver.ResolvedCount == explosions + 2, "damage and queued chain reactions detonate each projectile once");
            Object.DestroyImmediate(explosive.gameObject); Object.DestroyImmediate(chain.gameObject);

            var unaligned = Probe(Vector3.right, CreaturePolarity.None);
            emitter.transform.position = Vector3.left; emitter.GetComponent<Rigidbody>().position = Vector3.left;
            physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 60f, 12f, emitter);
            Check(unaligned.Health < 100f && emitter.Health == 100f, "incidental blast damages None while excluding emitter damage");
            physics.Simulate(0.02f); // AddForce is integrated on the next physics step.
            Check(unaligned.GetComponent<Rigidbody>().linearVelocity.x > 0f, "blast pushes outward");
            Object.DestroyImmediate(unaligned.gameObject); Object.DestroyImmediate(emitter.gameObject);

            var staticVictim = Probe(Vector3.right, CreaturePolarity.None);
            Object.DestroyImmediate(staticVictim.GetComponent<Rigidbody>());
            physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 60f, 0f, null);
            Check(staticVictim.Health < 100f, "static creature colliders also receive incidental blast damage");
            Object.DestroyImmediate(staticVictim.gameObject);
            var shielded = Probe(Vector3.forward * 2f, CreaturePolarity.None);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = Vector3.forward; wall.transform.localScale = new Vector3(3f, 3f, 0.2f);
            physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 60f, 12f, null);
            Check(shielded.Health == 100f, "solid walls obstruct blast damage");
            Object.DestroyImmediate(shielded.gameObject); Object.DestroyImmediate(wall);

            var general = Spawn<GeneralAsura>("GeneralAsura", Vector3.zero);
            general.ApplyDamage(10000f); general.Die();
            Check(general.DroppedCore != null && !general.DroppedCore.IsDead && general.DroppedCore.Age == 90f, "damage death produces one living finite-age core");
            var core = general.DroppedCore; CreaturePolarity corePolarity = core.Polarity;
            Check(core.InteractionPrompt == null && core.ShotsFired == 0, "ground core does not offer or perform automatic firing");
            Check(corePolarity == CreaturePolarity.Light || corePolarity == CreaturePolarity.Dark, "core uses ordinary Random polarity");
            var playerRoot = new GameObject("Blast jump witness"); playerRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(playerRoot, scene);
            playerRoot.transform.position = new Vector3(0f, 10f, 0f);
            var playerBody = playerRoot.AddComponent<Rigidbody>(); playerBody.useGravity = false;
            playerRoot.AddComponent<CapsuleCollider>();
            var player = playerRoot.AddComponent<SmoothFirstPersonController>();
            var cameraObject = new GameObject("Witness camera"); cameraObject.transform.SetParent(playerRoot.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            var anchor = new GameObject("Witness carry point").transform; anchor.SetParent(playerRoot.transform, false);
            var carrier = playerRoot.AddComponent<PlayerCreatureCarrier>();
            Set(carrier, "carryCamera", camera); Set(carrier, "carryPoint", anchor);
            playerRoot.SetActive(true);
            // This fixture has no rendered frame; initialize only the reference normally cached by Start.
            Set(player, "rb", playerBody);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { core });
            Check(core.IsCarried && carrier.CarriedCreature == core && core.CanUseWhileCarried, "living core opts into held-use input priority");
            var thrower = playerRoot.AddComponent<CreatureThrower>();
            Check(!thrower.HoldingThrowable, "held use excludes throwing");
            core.ReceiveInteraction(new CreatureInteraction(playerRoot, core.AimPosition + Vector3.forward * 10f, Vector3.forward));
            core.ReceiveInteraction(new CreatureInteraction(playerRoot, core.AimPosition + Vector3.forward * 10f, Vector3.forward));
            Check(core.ShotsFired == 1, "held core shoots the shared projectile with one-second cooldown");
            Advance(core, 10f); Check(core.Age == 80f, "core lifetime advances independently of ammunition");
            Check(core.IsCarried && core.Polarity == corePolarity, "carrying retains age clock and resolved polarity");
            playerBody.linearVelocity = new Vector3(2f, 1f, 0f);
            physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, playerBody.position + new Vector3(-1f, -1f, 0f), 4f, 60f, 12f, core);
            Check(player.Flung && playerBody.linearVelocity.x > 2f && playerBody.linearVelocity.y > 1f, "blast adds useful upward and sideways player velocity");
            Vector3 flung = playerBody.linearVelocity;
            typeof(SmoothFirstPersonController).GetMethod("SpeedControl", Private).Invoke(player, null);
            Check(playerBody.linearVelocity == flung, "movement speed limiting preserves the blast launch");
            carrier.Release();
            var thrownAsura = Spawn<MeleeAsura>("MeleeAsura", playerBody.position + Vector3.forward);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { thrownAsura });
            Set(thrower, "view", camera); thrower.Throw();
            Check(!thrownAsura.SimulationEnabled && thrownAsura.GetComponent<Rigidbody>().isKinematic, "throw temporarily owns Asura simulation and physics");
            Vector3 deathPosition = thrownAsura.transform.position;
            thrownAsura.ApplyDamage(10000f);
            typeof(CreatureThrower).GetMethod("FixedUpdate", Private).Invoke(thrower, null);
            Check(thrownAsura.IsDead && !thrownAsura.GetComponent<Rigidbody>().isKinematic && thrownAsura.transform.position == deathPosition,
                "damage death retires throw movement before corpse physics");
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { thrownAsura });
            thrower.Throw();
            Check(thrownAsura.IsDead && thrownAsura.GetComponent<Rigidbody>().isKinematic,
                "existing corpse throwing remains available");
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { thrownAsura });
            carrier.Release();
            Check(thrownAsura.IsDead && !thrownAsura.GetComponent<Rigidbody>().isKinematic,
                "reclaiming and dropping a thrown corpse restores passive physics");
            Object.DestroyImmediate(thrownAsura.gameObject);
            var reclaimed = Spawn<BasicProjectile>("BasicProjectile", playerBody.position + Vector3.forward);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { reclaimed });
            thrower.Throw();
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { reclaimed });
            Check(reclaimed.IsCarried && reclaimed.SimulationEnabled && !reclaimed.GetComponent<Rigidbody>().isKinematic,
                "picking up a thrown spent projectile retires throw ownership");
            thrower.Throw();
            var handoffAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/TrappingBubble.prefab");
            var handoffRoot = Object.Instantiate(handoffAsset, reclaimed.transform.position, Quaternion.identity, staging.transform);
            handoffRoot.transform.SetParent(null, true);
            Check(handoffRoot.GetComponent<TrappingBubble>().TryCapture(reclaimed.gameObject), "bubble can take ownership of thrown material");
            reclaimed.GetComponent<BubbleCapture>().Release();
            Check(reclaimed.SimulationEnabled && reclaimed.FlightState == ProjectileFlightState.Spent && !reclaimed.GetComponent<Rigidbody>().isKinematic,
                "bubble release restores underlying simulation after throw handoff");
            Object.DestroyImmediate(reclaimed.gameObject); Object.DestroyImmediate(handoffRoot);
            Age(core, 0f); Check(core.DeathCause == CreatureDeathCause.Age, "core expires through age death");
            Object.DestroyImmediate(core.gameObject); Object.DestroyImmediate(general.gameObject);
            Object.DestroyImmediate(playerRoot);
            general = Spawn<GeneralAsura>("GeneralAsura", Vector3.zero); general.Die();
            Check(general.DroppedCore == null, "direct death does not drop a core"); Object.DestroyImmediate(general.gameObject);
            general = Spawn<GeneralAsura>("GeneralAsura", Vector3.zero); Age(general, 0f);
            Check(general.DroppedCore == null, "age death does not drop a core"); Object.DestroyImmediate(general.gameObject);

            // Both capture mechanisms must turn a live explosive into permanently harmless material.
            var projectile = Spawn<ExplosiveProjectile>("ExplosiveProjectile", new Vector3(0f, 4f, 0f));
            var source = Probe(new Vector3(10f, 4f, 0f));
            projectile.Launch(source, null, Vector3.forward, 60f, 4f, 12f);
            var bubbleAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/TrappingBubble.prefab");
            var bubbleRoot = Object.Instantiate(bubbleAsset, new Vector3(0f, 4f, -2f), Quaternion.identity, staging.transform);
            bubbleRoot.transform.SetParent(null, true); var bubble = bubbleRoot.GetComponent<TrappingBubble>();
            Check(bubble.TryCapture(projectile.gameObject) && projectile.FlightState == ProjectileFlightState.Spent && !projectile.SimulationEnabled,
                "bubble capture spends and pauses flying projectile");
            projectile.GetComponent<BubbleCapture>().Release();
            Check(projectile.FlightState == ProjectileFlightState.Spent && projectile.GetComponent<Rigidbody>().useGravity,
                "capture release restores passive spent physics");
            explosions = ExplosionResolver.ResolvedCount; projectile.ApplyDamage(1000f);
            Check(ExplosionResolver.ResolvedCount == explosions, "captured explosive cannot rearm after release");
            Object.DestroyImmediate(projectile.gameObject); Object.DestroyImmediate(bubbleRoot);
            projectile = Spawn<ExplosiveProjectile>("ExplosiveProjectile", new Vector3(0f, 4f, 0f));
            projectile.Launch(source, null, Vector3.forward, 60f, 4f, 12f);
            var webAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/SkyWeb.prefab");
            var webRoot = Object.Instantiate(webAsset, new Vector3(0f, 4f, 0f), Quaternion.identity, staging.transform);
            webRoot.transform.SetParent(null, true); var web = webRoot.GetComponent<SkyWeb>();
            Check(web.TryCapture(projectile.GetComponent<Rigidbody>(), projectile.transform.position) && projectile.FlightState == ProjectileFlightState.Spent,
                "web capture spends flying projectile");
            projectile.GetComponent<WebCapture>().Release();
            Check(projectile.FlightState == ProjectileFlightState.Spent && projectile.SimulationEnabled, "web release does not rearm flight");
            Object.DestroyImmediate(projectile.gameObject); Object.DestroyImmediate(webRoot); Object.DestroyImmediate(source.gameObject);
            string result = "PASS: " + checks + " Asura combat and lifecycle integration checks.";
            Debug.Log(result); return result;
        }
        finally
        {
            typeof(CreatureThrower).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { originalThrower });
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
