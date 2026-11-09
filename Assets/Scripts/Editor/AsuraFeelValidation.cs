using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AsuraFeelValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string text) { if (!value) throw new Exception("Contact feel: " + text); checks++; }
    private static void Set(object target, string field, object value)
    {
        Type type = target.GetType(); FieldInfo info = null;
        while (type != null && info == null) { info = type.GetField(field, Private); type = type.BaseType; }
        info.SetValue(target, value);
    }
    private static void Tick(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    [MenuItem("Tools/Creatures/Asura/Validate Contact Feel (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play Mode first.");
        checks = 0;
        var scene = SceneManager.CreateScene("Contact checks " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        var staging = new GameObject("Staging"); staging.SetActive(false); SceneManager.MoveGameObjectToScene(staging, scene);
        T Spawn<T>(string name, Vector3 position) where T : Creature
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AsuraPrototypeSetup.Folder + "/" + name + ".prefab"), position, Quaternion.identity, staging.transform);
            root.SetActive(true); root.transform.SetParent(null, true); return root.GetComponent<T>();
        }
        try
        {
            var a = Spawn<MeleeAsura>("MeleeAsura", Vector3.zero);
            var b = Spawn<MeleeAsura>("MeleeAsura", Vector3.forward * 0.98f);
            Set(b, "polarity", CreaturePolarity.Dark);
            var strike = a.GetComponent<MeleeAttack>();
            Physics.SyncTransforms();
            Check(strike.InReach(a, b), "surface contact permits differently scaled creatures to hit");
            int events = 0; b.Damaged += (_, __, ___) => events++;
            Check(strike.Release(a, b, null) && b.Health == 80f && events == 1, "contact release delivers one damage event");
            physics.Simulate(0.001f);
            Check(b.GetComponent<Rigidbody>().linearVelocity.z > 2f, "contact delivers visible backward velocity");
            Check(b.TryGetComponent<CreatureHurtResponse>(out var hurt) && hurt.Strength > 0f, "health damage starts shared hurt");
            var renderer = b.transform.Find("Visual/Body").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", Color.cyan); renderer.SetPropertyBlock(properties);
            Tick(hurt, "LateUpdate"); renderer.GetPropertyBlock(properties, 0);
            Check(properties.GetColor("_BaseColor").r > 0.5f, "hurt overlays the existing individual material properties");
            Set(hurt, "remaining", 0f); Tick(hurt, "Update"); renderer.GetPropertyBlock(properties, 0);
            Check(properties.isEmpty, "hurt restores an empty material slot instead of freezing procedural color updates");
            b.transform.position = Vector3.forward * 4f; Physics.SyncTransforms();
            Check(!strike.Release(a, b, null) && events == 1, "out-of-contact release cannot hurt or push");
            Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject);

            var small = Spawn<MeleeAsura>("MeleeAsura", new Vector3(-10f, 0f, 0f));
            var medium = Spawn<RangedAsura>("RangedAsura", Vector3.zero);
            var large = Spawn<GeneralAsura>("GeneralAsura", new Vector3(10f, 0f, 0f));
            foreach (var c in new Creature[] { small, medium, large })
            { c.GetComponent<Rigidbody>().useGravity = false; CreatureImpulse.Apply(c.GetComponent<Rigidbody>(), Vector3.forward * 4f); }
            physics.Simulate(0.001f);
            Check(small.GetComponent<Rigidbody>().linearVelocity.z > medium.GetComponent<Rigidbody>().linearVelocity.z && medium.GetComponent<Rigidbody>().linearVelocity.z > large.GetComponent<Rigidbody>().linearVelocity.z, "size categories resist the same impulse differently");
            Check(!small.ApplyDamage(0f) && small.GetComponent<CreatureHurtResponse>() == null, "rejected damage produces no hurt reaction");
            Object.DestroyImmediate(small.gameObject); Object.DestroyImmediate(medium.gameObject); Object.DestroyImmediate(large.gameObject);

            var target = Spawn<MeleeAsura>("MeleeAsura", Vector3.right);
            target.GetComponent<Rigidbody>().useGravity = false;
            var extra = new GameObject("Compound"); extra.transform.SetParent(target.transform, false); extra.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            Physics.SyncTransforms();
            ExplosionResolver.Request(scene, Vector3.zero, 4f, 0f, 4f, null);
            physics.Simulate(0.001f);
            float speed = target.GetComponent<Rigidbody>().linearVelocity.magnitude;
            Check(speed > 1f && speed <= 4.01f, "compound body receives one blast impulse");
            var late = Spawn<MeleeAsura>("MeleeAsura", Vector3.zero); late.GetComponent<Rigidbody>().useGravity = false;
            physics.Simulate(0.001f);
            Check(late.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude < 0.001f, "entering an old explosion has no residual force");
            Object.DestroyImmediate(target.gameObject); Object.DestroyImmediate(late.gameObject);

            var emitter = Spawn<BlastCore>("BlastCore", Vector3.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = Vector3.forward; wall.transform.localScale = new Vector3(3f, 3f, 0.2f); Physics.SyncTransforms();
            Check(emitter.GetComponent<ProjectileAttack>().Fire(emitter, Vector3.forward * 2f, Vector3.forward) == null, "muzzle beyond a wall cannot spawn a shot through it");
            Object.DestroyImmediate(wall); Object.DestroyImmediate(emitter.gameObject);

            var bomber = Spawn<RollingAsura>("RollingAsura", Vector3.zero);
            int before = ExplosionResolver.ResolvedCount;
            bomber.ApplyDamage(999f); Check(bomber.IsDead && ExplosionResolver.ResolvedCount == before, "bomber damage death is inert");
            Object.DestroyImmediate(bomber.gameObject);
            bomber = Spawn<RollingAsura>("RollingAsura", Vector3.zero);
            Set(bomber, "<Armed>k__BackingField", true); Set(bomber, "fuse", 0.01f);
            Tick(bomber, "TickCreature", 0.02f); Tick(bomber, "TickCreature", 0.02f);
            Check(bomber.IsDead && ExplosionResolver.ResolvedCount == before + 1, "fuse resolves exactly one blast");
            Object.DestroyImmediate(bomber.gameObject);
            bomber = Spawn<RollingAsura>("RollingAsura", Vector3.zero);
            Set(bomber, "<Armed>k__BackingField", true); Tick(bomber, "OnCaptureStarted");
            Tick(bomber, "TickCreature", 2f);
            Check(bomber.Spent && !bomber.Armed && !bomber.IsDead && ExplosionResolver.ResolvedCount == before + 1, "capture permanently disarms the rolling bomber");
            Object.DestroyImmediate(bomber.gameObject);
            return "PASS: " + checks + " contact, hurt, impulse, launcher and bomber checks.";
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }
}
