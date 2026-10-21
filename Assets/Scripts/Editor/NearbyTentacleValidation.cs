using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Play Mode regression for real Awake/model creation, run in an isolated batch project without -quit.</summary>
[InitializeOnLoad]
public static class NearbyTentacleValidation
{
    private const string Pending = "Capstone.NearbyTentacleValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static NearbyTentacleValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) RunInPlayMode();
        };
        if (EditorApplication.isPlaying && SessionState.GetBool(Pending, false)) EditorApplication.delayCall += RunInPlayMode;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void RunInPlayMode()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int checks = 0;
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Nearby tentacles: " + message);
            checks++;
        }
        try
        {
            Check(Application.isPlaying, "validation exercises real Play Mode lifecycle");
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var worldObject = new GameObject("Terrain layout");
            worldObject.SetActive(false);
            worldObject.transform.position = origin;
            var world = worldObject.AddComponent<ProceduralWorld>();
            Set(world, "sinkholeCount", 0); Set(world, "surfaceAmplitude", 0f); Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));

            var templateObject = new GameObject("Source tentacle");
            templateObject.SetActive(false);
            templateObject.transform.position = origin + Vector3.right * 45f;
            templateObject.transform.localScale = Vector3.one * 1.4f;
            templateObject.AddComponent<SphereCollider>().radius = 0.45f;
            var template = templateObject.AddComponent<TentacleCreature>();
            templateObject.SetActive(true);
            Check(template.GetComponentsInChildren<Renderer>().Length > 0, "the source's Awake constructs visible models");
            templateObject.transform.localScale = Vector3.one * 0.32f;
            Check(Mathf.Abs(template.UncarriedScale.x - 1.4f) < 0.001f, "source retains its free-body scale while shrunk");

            BoneKiln Mother(Vector3 position)
            {
                var go = new GameObject("Mother spawn controller");
                var mother = go.AddComponent<BoneKiln>();
                mother.enabled = false;
                var root = new GameObject("Bone Kiln (runtime)").transform;
                root.position = position;
                Set(mother, "world", world); Set(mother, "root", root); Set(mother, "mouth", position);
                return mother;
            }
            var mother = Mother(origin);
            mother.SetTentacleTemplate(template);
            Call(mother, "SpawnNearbyTentacles", Vector3.forward, 10f);
            Check(mother.SpawnedTentacleCount == 0, "no terrain means wait, not spawn above an estimated floor");

            var mesh = new Mesh { name = "Spawn ground" };
            mesh.vertices = new[] { new Vector3(-130f, 0f, -130f), new Vector3(130f, 0f, -130f),
                new Vector3(-130f, 0f, 130f), new Vector3(130f, 0f, 130f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            var terrain = new GameObject("Terrain collider");
            terrain.layer = world.TerrainLayer;
            terrain.transform.position = origin;
            terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
            Call(mother, "TrySpawnNearbyTentacles");
            Check(mother.SpawnedTentacleCount == 0, "a model collider cannot substitute for the real terrain");
            terrain.AddComponent<ProceduralWorldChunk>().Initialize(world, Vector3Int.zero);
            Physics.SyncTransforms();
            Call(mother, "TrySpawnNearbyTentacles");
            Check(mother.SpawnedTentacleCount == 3, "late terrain arrival produces all three nearby creatures");
            var spawned = (List<TentacleCreature>)Get(mother, "spawnedTentacles");
            int visibleSide = 0;
            foreach (TentacleCreature creature in spawned)
            {
                Check(creature.gameObject.activeInHierarchy && creature.enabled && creature.SimulationEnabled,
                    "spawned creature is active and simulating");
                Check(creature.GetComponentsInChildren<Renderer>().Length > 0 && creature.GetComponent<Rigidbody>().isKinematic,
                    "activation runs Awake and builds its visible body");
                Check(Mathf.Abs(creature.transform.localScale.x - 1.4f) < 0.001f,
                    "new creature inherits full size rather than the source's carried size");
                Check(creature.transform.position.y >= origin.y + 0.9f
                    && Vector3.Distance((Vector3)Get(creature, "home"), creature.transform.position) < 0.001f,
                    "creature starts above the real floor with its home at the spawn point");
                if (creature.transform.position.z > origin.z) visibleSide++;
            }
            Check(visibleSide >= 2, "two creatures are on the mother's visible back/side rather than all behind the hole");
            Call(mother, "TrySpawnNearbyTentacles");
            Check(mother.SpawnedTentacleCount == 3, "retries never duplicate creatures");

            templateObject.SetActive(false);
            foreach (TentacleCreature creature in spawned) creature.gameObject.SetActive(false);
            var fallback = Mother(origin + Vector3.right * 80f);
            Call(fallback, "SpawnNearbyTentacles", Vector3.forward, 10f);
            Check(fallback.SpawnedTentacleCount == 3, "no available template still generates visible default creatures");
            // Exercise generated meshes and actual binding in Play Mode, not only lightweight Edit Mode anchors.
            BoneChild child = BoneChild.Spawn(null, origin + Vector3.left * 60f, 0f, 42, null, null, origin);
            var reserve = new List<FireBubble>();
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Play Mode reserve bubble");
                go.transform.position = child.Chest + Vector3.up;
                FireBubble bubble = go.AddComponent<FireBubble>();
                Check(child.Catch(bubble), "generated rib pair " + i + " accepts a bubble");
                reserve.Add(bubble);
            }
            Check(child.StoredBubbleCount == 3 && child.FuelHeat == 90f, "the built creature stores all three real fuel reserves");
            var heldFuel = (System.Collections.IList)Get(child, "storedFuel");
            foreach (object fuel in heldFuel) fuel.GetType().GetField("start").SetValue(fuel, Time.time - 3f);
            Call(child, "LateUpdate");
            Check(Vector3.Distance(reserve[0].transform.position, reserve[1].transform.position) > 0.5f
                && Vector3.Distance(reserve[0].transform.position, reserve[2].transform.position) > 0.5f,
                "all bubbles are held at separate visible rib pairs");
            var filters = (MeshFilter[,,])Get(child, "cradleFilters");
            var originals = (Mesh[,,])Get(child, "openRibMeshes");
            bool allCradled = true;
            for (int rib = 0; rib < 3; rib++) allCradled &= filters[rib, 0, 0].sharedMesh != originals[rib, 0, 0];
            Check(allCradled, "each pair generates its own hugging mesh instead of replacing the first pair");
            Call(child, "SpendFlightHeat", 30.75f / child.flightHeatPerSecond, 0f);
            Check(reserve[0].IsDead && child.StoredBubbleCount == 2 && reserve[1].Heat > 29f,
                "a depleted first bubble leaves the generated reserve intact");
            Debug.Log($"Nearby tentacle Play Mode validation passed: {checks} assertions.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
