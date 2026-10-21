using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Isolated Play Mode regression for real nest placement, cinematic ownership and stable emergence.</summary>
[InitializeOnLoad]
public static class BoneAwakeningValidation
{
    private const string Pending = "Capstone.BoneAwakeningValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static BoneAwakeningValidation()
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
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Mother awakening: " + message);
            checks++;
        }
        try
        {
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var worldObject = new GameObject("Terrain layout"); worldObject.SetActive(false);
            worldObject.transform.position = origin;
            var world = worldObject.AddComponent<ProceduralWorld>();
            Set(world, "sinkholeCount", 0); Set(world, "surfaceAmplitude", 0f); Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));
            var oldBoneMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            var nest = KilnBubbleNest.Build(null, origin, oldBoneMaterial, 43, origin + Vector3.forward * 10f, origin + Vector3.back * 10f);
            Set(nest, "world", world);
            Check(!nest.Placed && !nest.GetComponentInChildren<MeshRenderer>().enabled, "nest waits invisibly until real terrain is ready");
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-120f, 0f, -120f), new Vector3(120f, 0f, -120f),
                new Vector3(-120f, 0f, 120f), new Vector3(120f, 0f, 120f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            var ground = new GameObject("Ground collider"); ground.layer = world.TerrainLayer;
            ground.transform.position = origin; ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms(); Call(nest, "TryPlace");
            Check(!nest.Placed, "another model on the terrain layer cannot substitute for generated ground");
            ground.AddComponent<ProceduralWorldChunk>().Initialize(world, Vector3Int.zero);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall at the old nest point"; wall.transform.position = origin + Vector3.up * 2f;
            wall.transform.localScale = new Vector3(5f, 5f, 5f);
            Physics.SyncTransforms(); Call(nest, "TryPlace");
            Check(nest.Placed && Vector3.Distance(nest.transform.position, origin) > 3f,
                "nest searches for an unobstructed floor patch instead of staying inside a wall");
            Check(Mathf.Abs(nest.transform.position.y - (origin.y + 0.025f)) < 0.01f,
                "the placed bowl rests on terrain rather than the top of a bone or model");
            var shell = nest.GetComponentInChildren<MeshRenderer>();
            Check(shell.sharedMaterials.Length == 2 && shell.sharedMaterials[0] != oldBoneMaterial
                && shell.sharedMaterials[0].shader != oldBoneMaterial.shader, "fire source has independent ash-bone and ember materials");
            Check(nest.GetComponentInChildren<MeshCollider>().enabled && shell.sharedMaterials[1].IsKeywordEnabled("_EMISSION"),
                "placed basin has collision and a glowing interior");
            var source = new GameObject("Nest bubble").AddComponent<FireBubble>();
            var boundOwner = new GameObject("Fuel owner").AddComponent<BoneKiln>();
            Check(source.BindToBone(boundOwner, boundOwner.transform), "a nest bubble binds as normal bone fuel");
            var alive = (List<(FireBubble bubble, float born)>)Get(nest, "alive");
            alive.Add((source, Time.time - 200f));
            Call(nest, "Update");
            Check(alive.Count == 0 && source != null && !source.IsDead, "attached mother fuel is released from the nest's expiry list");

            var player = new GameObject("Player"); player.transform.position = origin + Vector3.right * 70f;
            var body = player.AddComponent<Rigidbody>(); body.useGravity = false;
            player.AddComponent<CapsuleCollider>();
            var controller = player.AddComponent<SmoothFirstPersonController>();
            var camera = new GameObject("Gameplay camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = player.transform.position + Vector3.up;
            camera.fieldOfView = 73f;
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            var tether = player.AddComponent<TentacleTether>();
            Type cordType = typeof(TentacleTether).GetNestedType("Cord", BindingFlags.NonPublic);
            object drawnCord = Activator.CreateInstance(cordType, true);
            cordType.GetField("held").SetValue(drawnCord, true);
            ((IList)Get(tether, "cords")).Add(drawnCord);
            var held = new GameObject("Carried creature").AddComponent<FireBubble>();
            Call(carrier, "BeginCarry", held);
            Check(carrier.CarriedCreature == held, "test begins with a carried bubble");
            Vector3 cameraStart = camera.transform.position;
            var mother = new GameObject("Awakening mother").AddComponent<BoneKiln>();
            var root = new GameObject("Skeleton").transform; root.position = origin + Vector3.forward * 50f;
            Set(mother, "root", root); Set(mother, "world", world); Set(mother, "mouth", root.position);
            Set(mother, "warmth", 1f);
            var path = new List<Vector3> { new Vector3(0f, -8f, 0f), new Vector3(0f, -3f, 1f),
                new Vector3(0f, 2f, 4f), new Vector3(0f, 3f, 10f) };
            Set(mother, "path", path);
            Type stationType = typeof(BoneKiln).GetNestedType("Station", BindingFlags.NonPublic);
            var stations = (IList)Get(mother, "stations");
            float length = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) length += Vector3.Distance(path[i - 1], path[i]);
                object station = Activator.CreateInstance(stationType, true);
                var bone = new GameObject("Vertebra " + i).transform; bone.SetParent(root, false); bone.localPosition = path[i];
                stationType.GetField("transform").SetValue(station, bone);
                stationType.GetField("s").SetValue(station, length);
                stationType.GetField("flightRadius").SetValue(station, 0.5f);
                stationType.GetField("frameUp").SetValue(station, Vector3.forward);
                stations.Add(station);
            }
            mother.Wake();
            Check(mother.RequiredFireBubbles == 15 && mother.Emerging, "new mothers require fifteen bubbles and start tail-first emergence");
            var cutscene = UnityEngine.Object.FindFirstObjectByType<BoneAwakeningCutscene>();
            Check(BoneAwakeningCutscene.Playing && cutscene != null && !camera.enabled && Camera.main != camera,
                "activation switches to a separate cinematic camera");
            Check(Vector3.Distance(Camera.main.transform.position, mother.AwakeningFrame().center + Vector3.up * 2f) <= 40.1f,
                "opening cinematic stays close rather than retreating to frame the entire skeleton");
            Check(!controller.enabled && body.isKinematic && !BoneRide.CanToggle, "cinematic owns movement and blocks mount input");
            Check(!(bool)cordType.GetField("held").GetValue(drawnCord) && tether.Flung == 0,
                "camera takeover cancels a pending spring release without launching the player afterwards");
            ((IList)Get(tether, "cords")).Clear();
            Check(carrier.CarriedCreature == held && camera.transform.position == cameraStart && camera.fieldOfView == 73f,
                "camera switch preserves held creature and gameplay camera state");
            Vector3 tail = (Vector3)Get(mother, "head");
            Call(mother, "BackOut", 1f);
            Check((Vector3)Get(mother, "head") == tail, "opening beat holds position while ribs fold");
            Call(mother, "BackOut", 1.25f);
            Vector3 firstStep = (Vector3)Get(mother, "head");
            Check(Vector3.Distance(firstStep, tail) < 0.1f, "backward motion starts with near-zero speed");
            float maxRotation = 0f;
            for (int frame = 0; frame < 1100 && mother.Emerging; frame++)
            {
                var bone = (Transform)stationType.GetField("transform").GetValue(stations[1]);
                Quaternion previous = bone.rotation;
                Call(mother, "BackOut", 1f / 60f);
                Call(mother, "PlaceBodyStep", 1f / 60f);
                maxRotation = Mathf.Max(maxRotation, Quaternion.Angle(previous, bone.rotation));
            }
            Check(!mother.Emerging && Vector3.Dot((Vector3)Get(mother, "head") - tail, mother.ExitDirection) > 5f,
                "full animation clears the original neck before head-first roaming");
            Check(maxRotation < 25f, "vertical vertebrae never accumulate explosive per-frame twist");
            Check((float)Get(mother, "flightRoll") < 1f, "emergence does not multiply global play time into a roll angle");
            cutscene.Finish();
            Check(!BoneAwakeningCutscene.Playing && camera.enabled && controller.enabled && !body.isKinematic,
                "ending or skipping restores camera and player movement");
            Check(carrier.CarriedCreature == held && Camera.main == camera, "control restoration retains held creature and main camera");
            BoneAwakeningCutscene.Play(mother);
            cutscene = UnityEngine.Object.FindFirstObjectByType<BoneAwakeningCutscene>();
            // The prior object is destroyed at frame end, so choose the active owner explicitly.
            cutscene = (BoneAwakeningCutscene)typeof(BoneAwakeningCutscene).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            cutscene.enabled = false;
            Check(!BoneAwakeningCutscene.Playing && camera.enabled && controller.enabled && !body.isKinematic,
                "disabling an interrupted cinematic also restores ownership");
            var rib = new GameObject("Transport rib").AddComponent<BoneRib>();
            rib.free = true; rib.owner = rib.transform;
            rib.segments = new[] { new GameObject("Root joint").transform, new GameObject("Tip joint").transform };
            foreach (Transform joint in rib.segments) joint.SetParent(rib.transform, false);
            for (int frame = 0; frame < 120; frame++) rib.FoldForEmergence(1f / 60f);
            Quaternion folded = rib.segments[0].localRotation;
            rib.Drive(0.5f, 1f / 60f);
            Check(Quaternion.Angle(folded, rib.segments[0].localRotation) < 6f, "transport ribs ease into their first wingbeat rather than snapping open");
            Debug.Log($"Bone awakening Play Mode validation passed: {checks} assertions.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
