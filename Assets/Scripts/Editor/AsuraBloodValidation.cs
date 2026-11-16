using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real receivers, scene-local discovery, colliders, death hooks and interaction rays.</summary>
public static class AsuraBloodValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string name, object value)
    {
        var type = target.GetType(); FieldInfo field = null;
        while (type != null && field == null) { field = type.GetField(name, Private); type = type.BaseType; }
        if (field == null) throw new MissingFieldException(name);
        field.SetValue(target, value);
    }
    private static void Tick(Creature target, float dt) => target.GetType().GetMethod("TickCreature", Private).Invoke(target, new object[] { dt });
    [MenuItem("Tools/Creatures/Asura/Validate Blood (Play Mode)")]
    public static int Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        int checks = 0;
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException("Asura blood: " + text); checks++; }
        CreatureThrower previousThrower = CreatureThrower.Instance;
        var scene = SceneManager.CreateScene("Blood checks " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        var staging = new GameObject("Staging"); staging.SetActive(false); SceneManager.MoveGameObjectToScene(staging, scene);
        T Spawn<T>(string name, Vector3 position) where T : Creature
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AsuraPrototypeSetup.Folder + "/" + name + ".prefab");
            var root = Object.Instantiate(asset, position, Quaternion.identity, staging.transform);
            root.SetActive(true); root.transform.SetParent(null, true); return root.GetComponent<T>();
        }
        AsuraBlood Blood(Vector3 position, CreaturePolarity polarity)
        {
            var drop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AsuraBlood>(AsuraBloodSetup.BloodPath), position, Quaternion.identity, staging.transform);
            drop.transform.SetParent(null, true); drop.SetDonor(polarity); return drop;
        }
        void Resolve(Creature creature, CreaturePolarity value) => typeof(Creature).GetMethod("SetResolvedPolarity", Private).Invoke(creature, new object[] { value });
        void Age(Creature creature) => typeof(Creature).GetMethod("SetRemainingAge", Private | BindingFlags.Public).Invoke(creature, new object[] { 0f });
        var playerRoot = new GameObject("Coat player"); playerRoot.SetActive(false); SceneManager.MoveGameObjectToScene(playerRoot, scene);
        var body = playerRoot.AddComponent<Rigidbody>(); body.useGravity = false;
        var capsule = playerRoot.AddComponent<CapsuleCollider>(); capsule.height = 2f; capsule.radius = 0.3f;
        var stats = playerRoot.AddComponent<PlayerCreatureStats>();
        var controller = playerRoot.AddComponent<SmoothFirstPersonController>();
        var cameraObject = new GameObject("Coat test camera"); cameraObject.transform.SetParent(playerRoot.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        var anchor = new GameObject("Carry point").transform; anchor.SetParent(playerRoot.transform, false);
        anchor.localPosition = new Vector3(0.6f, 0f, 0.6f);
        var carrier = playerRoot.AddComponent<PlayerCreatureCarrier>();
        Set(carrier, "carryCamera", camera); Set(carrier, "carryPoint", anchor);
        var interactor = playerRoot.AddComponent<PlayerCreatureInteractor>();
        Set(interactor, "carrier", carrier); Set(interactor, "interactionCamera", camera);
        playerRoot.SetActive(true); Set(controller, "rb", body);
        void Move(Vector3 position) { body.position = position; playerRoot.transform.position = position; Physics.SyncTransforms(); }
        try
        {
            Check(stats.CoatHealth == 0f && stats.Polarity == CreaturePolarity.None && !stats.CanBeAITargeted, "existing players start bare");
            int events = 0;
            stats.CoatChanged += (health, polarity) =>
            {
                Check(health == stats.CoatHealth && polarity == stats.Polarity && ((health == 0f) == (polarity == CreaturePolarity.None)), "events see the committed pair");
                events++;
            };
            stats.SetLuminosity(37f);
            Check(!stats.ApplyDamage(20f) && !stats.TryApplyBlood(CreaturePolarity.None) && !stats.TryApplyBlood(CreaturePolarity.Random) && events == 0, "bare damage and invalid donor are inert");
            Check(stats.TryApplyBlood(CreaturePolarity.Light) && stats.CoatHealth == 50f, "first blood is half full");
            Check(stats.TryApplyBlood(CreaturePolarity.Light) && stats.CoatHealth == 100f, "second matching blood fills");
            stats.ApplyDamage(20f);
            Check(stats.CoatHealth == 80f && stats.TryApplyBlood(CreaturePolarity.Light) && stats.CoatHealth == 100f, "80 caps at 100");
            int before = events;
            Check(!stats.TryApplyBlood(CreaturePolarity.Light) && events == before, "full matching coat rejects consumption and notification");
            Check(stats.TryApplyBlood(CreaturePolarity.Dark) && stats.CoatHealth == 50f && stats.Polarity == CreaturePolarity.Dark, "opposite blood replaces full coat");
            Check(stats.ApplyDamage(1000f) && stats.CoatHealth == 0f && stats.Polarity == CreaturePolarity.None, "depletion commits zero and None together");
            Check(stats.Luminosity == 37f && controller.enabled, "coat never changes light or controller");
            stats.TryApplyBlood(CreaturePolarity.Dark);
            var melee = Spawn<MeleeAsura>("MeleeAsura", Vector3.zero); Resolve(melee, CreaturePolarity.Light);
            Move(new Vector3(0f, 0.5f, 0.85f)); physics.Simulate(0.001f);
            var combat = melee.GetComponent<CreatureCombat>(); var strike = melee.GetComponent<MeleeAttack>();
            Check(combat.Eligible(stats, 20f) && strike.InReach(melee, stats), "opposite coat has a physical melee target");
            var motor = melee.GetComponent<AsuraMotor>(); motor.SetIntent(Vector3.forward * 3f, Vector3.forward);
            typeof(AsuraMotor).GetMethod("FixedUpdate", Private).Invoke(motor, null);
            Check(!(bool)typeof(AsuraMotor).GetField("steering", Private).GetValue(motor), "motor recognizes player collision as a target body instead of bypassing it as terrain");
            Check(strike.Release(melee, stats, null) && stats.CoatHealth == 30f, "authored melee damage reaches coat");
            Set(combat, "target", stats); Set(combat, "<State>k__BackingField", CreatureCombatState.Windup); Set(combat, "windupRemaining", 0f);
            stats.TryApplyBlood(CreaturePolarity.Light); combat.Tick(1f);
            Check(combat.Target == null && combat.Releases == 0 && !combat.Eligible(stats, 20f), "side switch cancels committed Light attack");
            Resolve(melee, CreaturePolarity.Dark); Check(combat.Eligible(stats, 20f), "Dark attacker acquires Light coat symmetrically");
            Set(combat, "target", stats); Set(combat, "<State>k__BackingField", CreatureCombatState.Windup); Set(combat, "windupRemaining", 0f);
            stats.TryApplyBlood(CreaturePolarity.Dark); combat.Tick(1f);
            Check(combat.Target == null && combat.Releases == 0, "side switch cancels committed Dark attack");
            stats.TryApplyBlood(CreaturePolarity.Light);
            Set(combat, "target", stats); Set(combat, "<State>k__BackingField", CreatureCombatState.Windup);
            stats.ApplyDamage(1000f); combat.Tick(1f);
            Check(combat.Target == null && combat.Releases == 0 && controller.enabled, "neutralization cancels windup without disabling input");
            var targets = new List<ICombatTarget>(); var indices = new List<int>(); var creatures = new List<Creature>();
            CreatureSpatialQuery.NearbyCombat(scene, stats.AimPosition, 20f, targets, indices);
            Check(targets.Contains(stats), "active player shares the scene snapshot");
            CreatureSpatialQuery.Nearby(scene, stats.AimPosition, 20f, creatures, indices);
            Check(creatures.Contains(melee), "legacy creature-only discovery remains intact");
            var foreignScene = SceneManager.CreateScene("Other blood scene " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            CreatureSpatialQuery.NearbyCombat(foreignScene, stats.AimPosition, 20f, targets, indices);
            Check(!targets.Contains(stats), "players from another scene are excluded"); SceneManager.UnloadSceneAsync(foreignScene);
            Object.DestroyImmediate(melee.gameObject);

            Move(new Vector3(0f, 0.6f, 6f)); stats.TryApplyBlood(CreaturePolarity.Dark);
            var chooser = Spawn<RangedAsura>("RangedAsura", Vector3.zero); Resolve(chooser, CreaturePolarity.Light);
            var ordinaryRoot = new GameObject("Closer ordinary target"); ordinaryRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(ordinaryRoot, scene); ordinaryRoot.transform.position = new Vector3(2f, 0.6f, 2f);
            ordinaryRoot.AddComponent<Rigidbody>().useGravity = false; ordinaryRoot.AddComponent<BoxCollider>();
            var ordinary = ordinaryRoot.AddComponent<CreatureLifecycleValidationProbe>(); ordinaryRoot.SetActive(true); Resolve(ordinary, CreaturePolarity.Dark);
            physics.Simulate(0.001f);
            var choosingCombat = chooser.GetComponent<CreatureCombat>(); Set(choosingCombat, "nextSearch", 0f); choosingCombat.Tick(0.01f);
            Check(choosingCombat.Target == (ICombatTarget)ordinary, "closer ordinary target wins over a coated player without special aggro priority");
            Object.DestroyImmediate(ordinaryRoot); choosingCombat.Cancel(); Set(choosingCombat, "nextSearch", 0f); choosingCombat.Tick(0.01f);
            Check(choosingCombat.Target == stats, "same discovery path acquires the remaining coated player");
            Object.DestroyImmediate(chooser.gameObject); stats.ApplyDamage(1000f);

            Move(Vector3.forward * 2f);
            stats.TryApplyBlood(CreaturePolarity.Light);
            var child = new GameObject("Second hitbox"); child.transform.SetParent(playerRoot.transform, false);
            child.AddComponent<BoxCollider>().size = new Vector3(0.5f, 1f, 0.5f);
            Check(CombatTarget.Owner(child.GetComponent<Collider>()) == stats, "child hitbox resolves to one owning player");
            var emitter = Spawn<RangedAsura>("RangedAsura", Vector3.back * 8f); Resolve(emitter, CreaturePolarity.Light);
            var shot = Spawn<BasicProjectile>("BasicProjectile", Vector3.zero);
            shot.Launch(emitter, null, Vector3.forward * 18f, 12f, 4f, 12f);
            for (int i = 0; i < 15; i++) physics.Simulate(0.02f);
            Check(shot.IsDead && stats.CoatHealth == 38f, "friendly in-flight shot damages compound player once");
            Object.DestroyImmediate(shot.gameObject);
            Move(Vector3.right); body.linearVelocity = Vector3.zero; physics.Simulate(0.001f);
            stats.TryApplyBlood(CreaturePolarity.Light); stats.TryApplyBlood(CreaturePolarity.Light);
            int damageEvents = events;
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 20f, 8f, emitter);
            Check(events == damageEvents + 1 && stats.CoatHealth < 100f && stats.CoatHealth > 80f, "allied blast applies one falloff hit across colliders");
            physics.Simulate(0.02f); float compoundPush = body.linearVelocity.x;
            Object.DestroyImmediate(child); Move(Vector3.right); body.linearVelocity = Vector3.zero; physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 0f, 8f, emitter); physics.Simulate(0.02f);
            Check(Mathf.Abs(body.linearVelocity.x - compoundPush) < 0.02f, "compound hitbox does not multiply impulse");
            stats.ApplyDamage(1000f); Move(Vector3.right); body.linearVelocity = Vector3.zero; physics.Simulate(0.001f);
            ExplosionResolver.Request(scene, new Vector3(0f, -1f, 0f), 4f, 60f, 12f, emitter);
            Check(stats.CoatHealth == 0f && body.linearVelocity.y > 0f && controller.BlastMotion, "bare player still rocket jumps");
            Object.DestroyImmediate(emitter.gameObject);

            foreach (string role in new[] { "MeleeAsura", "RangedAsura", "GeneralAsura", "RollingAsura" })
            {
                var donor = Spawn<Creature>(role, Vector3.right * 12f); Resolve(donor, CreaturePolarity.Dark);
                int dropCount = Object.FindObjectsByType<AsuraBlood>(FindObjectsSortMode.None).Length;
                donor.ApplyDamage(10000f); donor.Die(); donor.ApplyDamage(10000f);
                var drop = donor.GetComponent<AsuraBloodDrop>().DroppedBlood;
                Check(drop != null && drop.DonorPolarity == CreaturePolarity.Dark && drop.Polarity == CreaturePolarity.None && !drop.CanBeAITargeted, role + " leaves one neutral pickup with donor data");
                var first = drop; donor.Die(); Check(donor.GetComponent<AsuraBloodDrop>().DroppedBlood == first, role + " cannot double-drop");
                Check(Object.FindObjectsByType<AsuraBlood>(FindObjectsSortMode.None).Length == dropCount + 1, role + " creates exactly one blood object");
                if (donor is GeneralAsura general) { Check(general.DroppedCore != null && Vector3.Distance(general.DroppedCore.transform.position, drop.transform.position) > 0.8f, "General retains a separately selectable Blast Core"); Object.DestroyImmediate(general.DroppedCore.gameObject); }
                Object.DestroyImmediate(drop.gameObject); Object.DestroyImmediate(donor.gameObject);
                donor = Spawn<Creature>(role, Vector3.right * 12f); Age(donor);
                Check(donor.GetComponent<AsuraBloodDrop>().DroppedBlood == null, role + " age death skips blood"); Object.DestroyImmediate(donor.gameObject);
                donor = Spawn<Creature>(role, Vector3.right * 12f); donor.Die();
                Check(donor.GetComponent<AsuraBloodDrop>().DroppedBlood == null, role + " direct lifecycle death skips blood"); Object.DestroyImmediate(donor.gameObject);
                donor = Spawn<Creature>(role, Vector3.right * 12f);
                typeof(Creature).GetMethod("CompleteConsumption", Private).Invoke(donor, new object[] { null });
                Check(donor.DeathCause == CreatureDeathCause.Consumed && donor.GetComponent<AsuraBloodDrop>().DroppedBlood == null, role + " consumption skips blood");
                Object.DestroyImmediate(donor.gameObject);
            }
            Move(new Vector3(0f, 0.5f, 0.85f)); stats.TryApplyBlood(CreaturePolarity.Dark);
            var rolling = Spawn<RollingAsura>("RollingAsura", Vector3.zero); Resolve(rolling, CreaturePolarity.Light);
            Set(rolling, "nextSearch", 0f); physics.Simulate(0.001f); Tick(rolling, 0.01f);
            Check(rolling.Target == stats && rolling.Armed, "rolling acquires and arms against coated player");
            stats.TryApplyBlood(CreaturePolarity.Light); Tick(rolling, 1f);
            Check(!rolling.Armed && !rolling.IsDead, "rolling cancels its fuse after friendly switch");
            stats.TryApplyBlood(CreaturePolarity.Dark); Set(rolling, "nextSearch", 0f); Tick(rolling, 0.01f); Tick(rolling, 1f);
            Check(rolling.Detonated && rolling.IsDead && rolling.GetComponent<AsuraBloodDrop>().DroppedBlood != null, "rolling direct self-detonation leaves blood");
            var rollingDrop = rolling.GetComponent<AsuraBloodDrop>().DroppedBlood; rolling.Die();
            Check(rolling.GetComponent<AsuraBloodDrop>().DroppedBlood == rollingDrop, "self-detonation reward is claimed once");
            Object.DestroyImmediate(rollingDrop.gameObject); Object.DestroyImmediate(rolling.gameObject);

            Move(Vector3.zero); body.linearVelocity = Vector3.zero; camera.transform.rotation = Quaternion.identity;
            stats.ApplyDamage(1000f);
            var blood = Blood(Vector3.forward * 2f, CreaturePolarity.Light); Physics.SyncTransforms();
            Check(interactor.FindCreature() == blood && stats.CoatHealth == 0f, "aiming or standing near blood never auto-coats");
            bool reentrantUse = false;
            Action<float, CreaturePolarity> retry = (_, __) => reentrantUse = blood.TryCoat(interactor);
            stats.CoatChanged += retry;
            interactor.TryInteract();
            stats.CoatChanged -= retry;
            Check(!reentrantUse && stats.CoatHealth == 50f, "coat listeners cannot reenter and apply the same blood twice");
            Check(blood.Used && stats.CoatHealth == 50f && !blood.TryCoat(interactor), "deliberate normal interaction consumes exactly once");
            Object.DestroyImmediate(blood.gameObject);
            var core = Spawn<BlastCore>("BlastCore", Vector3.forward);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { core });
            blood = Blood(Vector3.forward * 2f, CreaturePolarity.Dark); Physics.SyncTransforms();
            Check(interactor.ContextualBloodPrompt != null && interactor.ContextualBloodPrompt.Contains(interactor.BloodCoatBinding), "held ability prompt shows its real contextual binding");
            Check(interactor.TryContextualBloodUse() && blood.Used && stats.Polarity == CreaturePolarity.Dark && core.ShotsFired == 0 && core.IsCarried, "contextual coat does not fire or drop the held core");
            Object.DestroyImmediate(blood.gameObject);
            stats.ApplyDamage(1000f);
            Check(core.IsCarried && controller.enabled, "losing the last point preserves the held weapon and input");
            interactor.TryInteract(); Check(core.ShotsFired == 1, "intentional left-click route still fires");
            stats.TryApplyBlood(CreaturePolarity.Dark); stats.TryApplyBlood(CreaturePolarity.Dark);
            blood = Blood(Vector3.forward * 2f, CreaturePolarity.Dark); Physics.SyncTransforms();
            Check(!interactor.TryContextualBloodUse() && !blood.Used && blood.gameObject.activeSelf, "full matching blood stays selectable on the ground");
            Move(Vector3.back * 8f); Physics.SyncTransforms();
            Check(!blood.TryCoat(interactor) && !blood.Used, "blood cannot be used beyond normal reach");
            Object.DestroyImmediate(blood.gameObject); carrier.Release(); Object.DestroyImmediate(core.gameObject);
            ICombatTarget stale = stats;
            Object.DestroyImmediate(playerRoot);
            Check(!CombatTarget.Exists(stale), "stored interfaces honor destroyed Unity objects");
            CreatureSpatialQuery.NearbyCombat(scene, Vector3.zero, 20f, targets, indices);
            Check(!targets.Contains(stale), "destroyed player never leaks through cached discovery");
            Debug.Log("PASS: " + checks + " Asura blood checks. Contextual binding: F; normal use: LMB.");
            return checks;
        }
        finally
        {
            typeof(CreatureThrower).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { previousThrower });
            if (playerRoot != null) Object.DestroyImmediate(playerRoot);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
