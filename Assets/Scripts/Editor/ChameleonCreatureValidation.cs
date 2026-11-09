using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Focused integration checks against the real player rig and shared creature systems.</summary>
public static class ChameleonCreatureValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Chameleon validation: " + message);
        checks++;
    }
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, Private).Invoke(target, arguments);

    [MenuItem("Tools/Creatures/Chameleon/Validate Assets")]
    public static void ValidateAssetMenu() => Debug.Log(ValidateAssets());

    public static string ValidateAssets()
    {
        checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChameleonCreatureSetup.PrefabPath);
        Check(prefab != null, "prefab exists");
        var creature = prefab.GetComponent<ChameleonCreature>();
        Check(creature != null && creature.HeldInView && creature.CanUseWhileCarried, "shared held-use support");
        Check(prefab.GetComponent<Rigidbody>() != null && !prefab.GetComponent<BoxCollider>().isTrigger, "solid carry physics");
        var skin = Get<Renderer[]>(creature, "skinRenderers");
        Check(skin.Length > 20 && skin.All(r => r != null), "body, legs and tail have assigned skin renderers");
        Check(prefab.GetComponentsInChildren<Renderer>().Where(r => r.name == "Pupil").All(r => !skin.Contains(r)), "pupils excluded from copying");
        Check(Get<Vector2>(creature, "sampleInterval") == new Vector2(60f, 120f) && Get<float>(creature, "sampleRadius") == 5f, "sampling defaults");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Scene_Yoyo.unity");
        Check(scene.IsValid() && scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<ChameleonCreature>(true).Length) == 1,
            "regular Yoyo contains one chameleon");
        return "PASS: " + checks + " chameleon asset checks.";
    }

    [MenuItem("Tools/Creatures/Chameleon/Validate Gameplay (Play Mode)")]
    public static void ValidateGameplayMenu() => Debug.Log(RunGameplay());

    public static string RunGameplay()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        var interactor = Object.FindFirstObjectByType<PlayerCreatureInteractor>();
        var carrier = Get<PlayerCreatureCarrier>(interactor, "carrier");
        var camera = Get<Camera>(interactor, "interactionCamera");
        var playerBody = interactor.transform.root.GetComponentInChildren<FirstPersonBody>();
        Check(carrier != null && camera != null && playerBody != null, "real player rig available");
        if (carrier.CarriedCreature != null) throw new InvalidOperationException("Put down the held creature before validation.");
        bool carrierEnabled = carrier.enabled;
        carrier.enabled = false; // Keep ownership deterministic while the synchronous checks run.
        Vector3 cameraPosition = camera.transform.position;
        Quaternion cameraRotation = camera.transform.rotation;
        var origin = new Vector3(5000f, 5000f, 5000f);
        var created = new List<Object>();
        var objects = new List<GameObject>();
        var white = Get<HashSet<Renderer>>(playerBody, "mimicRenderers");
        Material originalWhite = new Material(white.First().sharedMaterial);
        created.Add(originalWhite);
        var dark = Get<Renderer[]>(playerBody, "renderers").Where(r => !white.Contains(r)).ToDictionary(r => r, r => r.sharedMaterial);
        try
        {
            camera.transform.SetPositionAndRotation(origin, Quaternion.identity);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChameleonCreatureSetup.PrefabPath);
            var instance = Object.Instantiate(prefab, origin + Vector3.right * 2f, Quaternion.identity);
            objects.Add(instance);
            var chameleon = instance.GetComponent<ChameleonCreature>();
            var resolver = new SurfaceMaterialResolver();
            var physics = instance.scene.GetPhysicsScene();
            Material first = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Validation Red" };
            first.SetColor("_BaseColor", Color.red);
            first.SetFloat("_Smoothness", 0.37f);
            Material second = new Material(Shader.Find("Capstone/Creatures/TentacleLines")) { name = "Validation Lines" };
            second.SetColor("_BaseColor", Color.cyan);
            created.Add(first); created.Add(second);
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(target);
            target.transform.position = origin + Vector3.forward * 3f;
            target.GetComponent<Renderer>().sharedMaterial = first;
            Physics.SyncTransforms();

            Call(chameleon, "Start");
            Check(chameleon.CurrentMaterial != null && chameleon.CurrentMaterial != first &&
                chameleon.CurrentMaterial.GetColor("_BaseColor") == Color.red, "initial nearby sample snapshots material");
            Check(resolver.TryNearby(instance.scene, origin, 5f, chameleon, out Material nearby) && nearby == first, "nearby source includes ordinary surfaces");
            target.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms();
            Check(!resolver.TryNearby(instance.scene, origin, 5f, chameleon, out _), "trigger-only sources and self excluded");
            target.GetComponent<Collider>().isTrigger = false;
            target.GetComponent<Renderer>().enabled = false;
            Check(!resolver.TryNearby(instance.scene, origin, 5f, chameleon, out _), "disabled renderers excluded");
            target.GetComponent<Renderer>().enabled = true;
            Physics.SyncTransforms();
            Check(interactor.TryGetSurfaceHit(out var hit) && hit.collider.gameObject == target, "crosshair selects source");
            Check(resolver.TryFromHit(hit, out Material sampled) && sampled == first, "hit resolves original material");

            Check(carrier.TryCarryFromInteraction(chameleon) && carrier.CarriedCreature == chameleon, "pickup uses shared carrier");
            Check(chameleon.GetComponent<Rigidbody>().isKinematic, "held creature is kinematic");
            Material playerBefore = white.First().sharedMaterial;
            interactor.TryInteract();
            Check(chameleon.CurrentMaterial != first && chameleon.CurrentMaterial.shader == first.shader &&
                Mathf.Approximately(chameleon.CurrentMaterial.GetFloat("_Smoothness"), 0.37f), "held use copies shader and settings");
            Check(white.All(r => r.sharedMaterial == playerBefore) && target.GetComponent<Renderer>().sharedMaterial == first,
                "held use changes neither player nor source");
            float heldTimer = Get<float>(chameleon, "sampleRemaining");
            Call(chameleon, "TickCreature", 150f);
            Check(Get<float>(chameleon, "sampleRemaining") == heldTimer, "held use suspends automatic timer");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(wall);
            wall.transform.position = origin + Vector3.forward * 1.5f;
            wall.transform.localScale = new Vector3(0.5f, 0.5f, 0.2f);
            wall.GetComponent<Renderer>().enabled = false;
            Physics.SyncTransforms();
            Material retained = chameleon.CurrentMaterial;
            interactor.TryInteract();
            Check(chameleon.CurrentMaterial == retained, "solid without material blocks sampling behind it");
            wall.SetActive(false);
            target.transform.position = origin + Vector3.forward * 6f;
            Physics.SyncTransforms();
            Check(!interactor.TryGetSurfaceHit(out _), "range rejects distant object");
            interactor.TryInteract();
            Check(chameleon.CurrentMaterial == retained, "miss retains material");
            target.transform.position = origin + Vector3.forward * 3f;
            Physics.SyncTransforms();
            carrier.Release();
            Check(!chameleon.IsCarried && !chameleon.GetComponent<Rigidbody>().isKinematic, "drop restores physics");
            Check(Get<float>(chameleon, "sampleRemaining") >= 60f && Get<float>(chameleon, "sampleRemaining") <= 120f, "drop preserves fresh randomized interval");

            var source = new CreatureInteraction(interactor.gameObject, instance.transform.position, Vector3.forward);
            instance.transform.position = origin + Vector3.forward * 2f;
            Physics.SyncTransforms();
            interactor.TryInteract();
            instance.transform.position = origin + Vector3.right * 2f;
            Physics.SyncTransforms();
            Material borrowed = white.First().sharedMaterial;
            Check(borrowed != chameleon.CurrentMaterial && borrowed.shader == first.shader && white.All(r => r.sharedMaterial == borrowed),
                "unheld interaction independently snapshots all white parts");
            Check(dark.All(pair => pair.Key.sharedMaterial == pair.Value), "dark material assignments untouched");
            Call(playerBody, "LateUpdate");
            var properties = new MaterialPropertyBlock();
            white.First().GetPropertyBlock(properties);
            Check(properties.isEmpty, "borrowed skin does not receive original line-shader properties");

            target.GetComponent<Renderer>().sharedMaterial = second;
            Set(chameleon, "sampleRemaining", 0.01f);
            Call(chameleon, "TickCreature", 0.02f);
            Check(chameleon.CurrentMaterial.shader == second.shader && chameleon.CurrentMaterial != second, "automatic expiry adopts animated source shader");
            Check(white.All(r => r.sharedMaterial == borrowed) && borrowed.shader == first.shader, "player snapshot survives later chameleon changes");
            Check(target.GetComponent<Renderer>().sharedMaterial == second && first.GetColor("_BaseColor") == Color.red, "source assets unchanged");
            chameleon.ReceiveInteraction(source);
            Check(white.All(r => r.sharedMaterial.shader == second.shader) && dark.All(pair => pair.Key.sharedMaterial == pair.Value),
                "repeated borrow still targets only originally white parts");

            var bubbleObject = new GameObject("Validation Bubble"); objects.Add(bubbleObject);
            bubbleObject.transform.position = origin + Vector3.up * 10f;
            var bubble = bubbleObject.AddComponent<TrappingBubble>();
            var capture = instance.AddComponent<BubbleCapture>();
            Check(capture.Attach(bubble) && !chameleon.SimulationEnabled, "bubble capture pauses creature simulation");
            float captureTimer = Get<float>(chameleon, "sampleRemaining");
            Call(chameleon, "TickCreature", 150f);
            Check(Get<float>(chameleon, "sampleRemaining") == captureTimer, "captured timer suspended");
            capture.Release();
            Check(chameleon.SimulationEnabled && !chameleon.IsBubbleCaptured && Get<float>(chameleon, "sampleRemaining") >= 60f,
                "capture release resumes with fresh interval");
            target.SetActive(false);
            Material previous = chameleon.CurrentMaterial;
            Call(chameleon, "SampleNearby");
            Check(chameleon.CurrentMaterial == previous, "empty surroundings retain current appearance");
            target.SetActive(true);

            ValidateMultipart(resolver, physics, origin, first, second, objects, created);
            ValidateTerrainAndSkin(resolver, physics, origin, first, second, objects, created);
            Check(carrier.TryCarryFromInteraction(chameleon), "repickup supported");
            var thrower = carrier.GetComponent<CreatureThrower>();
            thrower.Throw(0.5f);
            Check(!chameleon.IsCarried && !chameleon.SimulationEnabled, "shared throw takes flight ownership");
            typeof(CreatureThrower).GetMethod("ReleaseFlight", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { chameleon });
            Check(chameleon.SimulationEnabled, "flight release restores simulation");
            Check(carrier.TryCarryFromInteraction(chameleon), "pickup after flight supported");
            chameleon.Die();
            Check(chameleon.IsDead && !chameleon.IsCarried && !chameleon.CanUseWhileCarried && !chameleon.CanBeCarried,
                "death releases carry and disables ability");
            Material playerSnapshot = white.First().sharedMaterial;
            Object.DestroyImmediate(instance);
            Check(white.All(r => r.sharedMaterial == playerSnapshot), "player snapshot survives source destruction");
            return "PASS: " + checks + " chameleon gameplay checks.";
        }
        finally
        {
            carrier.Release();
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            playerBody.ApplyMimicMaterial(originalWhite);
            carrier.enabled = carrierEnabled;
            foreach (GameObject obj in objects) if (obj != null) Object.DestroyImmediate(obj);
            foreach (Object obj in created) if (obj != null) Object.DestroyImmediate(obj);
        }
    }

    private static void ValidateMultipart(SurfaceMaterialResolver resolver, PhysicsScene physics, Vector3 origin,
        Material first, Material second, List<GameObject> objects, List<Object> created)
    {
        var root = new GameObject("Multipart Validation"); objects.Add(root);
        root.transform.position = origin + Vector3.right * 10f;
        root.AddComponent<Rigidbody>().isKinematic = true;
        var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(4f, 2f, 1f);
        for (int side = -1; side <= 1; side += 2)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = Vector3.right * side;
            part.GetComponent<Renderer>().sharedMaterial = side < 0 ? first : second;
        }
        Physics.SyncTransforms();
        Check(physics.Raycast(root.transform.position + new Vector3(-1f, 0f, -3f), Vector3.forward, out var left, 5f), "multipart collider hit");
        Check(resolver.TryFromHit(left, out Material leftMaterial) && leftMaterial == first, "multipart nearest left renderer");
        Check(physics.Raycast(root.transform.position + new Vector3(1f, 0f, -3f), Vector3.forward, out var right, 5f) &&
            resolver.TryFromHit(right, out Material rightMaterial) && rightMaterial == second, "multipart nearest right renderer");

        var panel = new GameObject("Submesh Validation", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)); objects.Add(panel);
        panel.transform.position = origin + Vector3.left * 10f;
        var mesh = new Mesh { name = "Two Material Triangles" }; created.Add(mesh);
        mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(0f, -1f, 0f),
            new Vector3(0.1f, -1f, 0f), new Vector3(0.1f, 1f, 0f), new Vector3(1.1f, -1f, 0f) };
        mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        panel.GetComponent<MeshFilter>().sharedMesh = mesh;
        panel.GetComponent<MeshCollider>().sharedMesh = mesh;
        panel.GetComponent<Renderer>().sharedMaterials = new[] { first, second };
        Physics.SyncTransforms();
        Check(physics.Raycast(panel.transform.position + new Vector3(-0.8f, -0.5f, -2f), Vector3.forward, out var firstHit, 4f) &&
            resolver.TryFromHit(firstHit, out Material firstSlot) && firstSlot == first, "triangle selects first submesh material");
        Check(physics.Raycast(panel.transform.position + new Vector3(0.3f, -0.5f, -2f), Vector3.forward, out var secondHit, 4f) &&
            resolver.TryFromHit(secondHit, out Material secondSlot) && secondSlot == second, "triangle selects second submesh material");
        panel.GetComponent<MeshCollider>().sharedMesh = null;
        Object.DestroyImmediate(panel.GetComponent<MeshCollider>());
        panel.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        physics.Raycast(panel.transform.position + new Vector3(-0.8f, -0.5f, -2f), Vector3.forward, out var fallback, 4f);
        var choices = new HashSet<Material>();
        for (int i = 0; i < 64; i++) if (resolver.TryFromHit(fallback, out Material choice)) choices.Add(choice);
        Check(choices.Contains(first) && choices.Contains(second), "box collider randomly selects among material slots");
    }

    private static void ValidateTerrainAndSkin(SurfaceMaterialResolver resolver, PhysicsScene physics, Vector3 origin,
        Material first, Material second, List<GameObject> objects, List<Object> created)
    {
        var data = new TerrainData { heightmapResolution = 33, size = new Vector3(4f, 1f, 4f) }; created.Add(data);
        var terrainObject = Terrain.CreateTerrainGameObject(data); objects.Add(terrainObject);
        terrainObject.transform.position = origin + new Vector3(20f, -1f, 0f);
        terrainObject.GetComponent<Terrain>().materialTemplate = first;
        Physics.SyncTransforms();
        Check(physics.Raycast(terrainObject.transform.position + new Vector3(2f, 3f, 2f), Vector3.down, out var terrainHit, 5f) &&
            resolver.TryFromHit(terrainHit, out Material terrainMaterial) && terrainMaterial == first, "Unity Terrain resolves assigned material");

        var skin = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(skin);
        skin.transform.position = origin + Vector3.left * 20f;
        Mesh mesh = skin.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(skin.GetComponent<MeshRenderer>());
        var renderer = skin.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.sharedMaterial = second;
        Physics.SyncTransforms();
        Check(physics.Raycast(skin.transform.position - Vector3.forward * 2f, Vector3.forward, out var skinHit, 4f) &&
            resolver.TryFromHit(skinHit, out Material skinMaterial) && skinMaterial == second, "skinned renderer resolves assigned material");
    }
}
