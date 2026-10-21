using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Focused physics regressions; run only in an isolated batch project.</summary>
public static class CreatureControlValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void RunAll()
    {
        // Keep this validation independently compilable; heat regressions have their own entry point.
        Run();
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var staging = new GameObject("Creature controls validation").transform;
        var meshes = new List<Mesh>();
        int checks = 0;
        object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Creature controls: " + message);
            checks++;
        }
        GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(staging);
            go.transform.position = at;
            return go;
        }
        try
        {
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            Camera camera = Make("Camera", origin).AddComponent<Camera>();
            var interactor = camera.gameObject.AddComponent<PlayerCreatureInteractor>();
            Set(interactor, "interactionCamera", camera);
            FireBubble bubble = Make("Bubble", origin + Vector3.forward * 3f).AddComponent<FireBubble>();
            bubble.GetComponent<SphereCollider>().radius = 0.12f;
            var sensor = Make("Irrelevant sensor", origin + Vector3.forward).AddComponent<BoxCollider>();
            sensor.isTrigger = true;
            sensor.size = Vector3.one;
            Physics.SyncTransforms();
            Check(interactor.FindCreature() == bubble, "a sensor cannot swallow a click on a creature");
            bubble.transform.position += Vector3.right * 0.22f;
            Physics.SyncTransforms();
            Check(interactor.FindCreature() == bubble, "a small aiming error still selects a small creature");
            var wall = Make("Wall", origin + Vector3.forward * 1.5f).AddComponent<BoxCollider>();
            wall.size = new Vector3(4f, 4f, 0.1f);
            Physics.SyncTransforms();
            Check(interactor.FindCreature() == null, "aim assistance never selects through a wall");
            Object.DestroyImmediate(wall.gameObject);

            var child = Make("Sleeping child", origin).AddComponent<BoneChild>();
            var beads = (List<Transform>)Get(child, "beads");
            for (int i = 0; i < 4; i++)
            {
                Transform bead = Make("Bead", origin).transform;
                bead.SetParent(child.transform, true);
                beads.Add(bead);
            }
            Set(child, "rest", origin);
            Set(child, "head", origin);
            Set(child, "headDir", Vector3.forward);
            Set(child, "restTimer", 8f);
            Check(!(bool)Call(child, "LookForFire") && child.Chasing == null, "a committed nap ignores nearby fire");
            Set(child, "resting", false);
            Set(child, "nextLook", 0f);
            Check((bool)Call(child, "LookForFire") && child.Chasing == bubble, "it notices fire again when awake");
            Set(child, "crawlTimer", 0.01f);
            Call(child, "Crawl", 0.1f);
            Check(child.Resting && child.Chasing == null, "chasing also ends in a rest instead of resetting fatigue forever");
            Check((float)Get(child, "restTimer") >= 6f, "a nap has a real minimum duration");

            var floor = Make("Ground", origin + Vector3.down).AddComponent<BoxCollider>();
            floor.gameObject.layer = 7;
            floor.size = new Vector3(20f, 1f, 20f);
            var ownCollider = beads[0].gameObject.AddComponent<SphereCollider>();
            ownCollider.gameObject.layer = 7;
            ownCollider.radius = 0.4f;
            Physics.SyncTransforms();
            Check(Mathf.Abs((float)Call(child, "GroundBelow", origin) - (origin.y - 0.5f)) < 0.01f,
                "its own vertebra does not hide the terrain raycast");

            var tentacle = Make("Tentacle", origin + Vector3.right * 6f).AddComponent<TentacleCreature>();
            Type dragArmType = typeof(TentacleCreature).GetNestedType("Tentacle", BindingFlags.NonPublic);
            Array dragArms = Array.CreateInstance(dragArmType, 3);
            for (int i = 0; i < 3; i++)
            {
                object dragArm = Activator.CreateInstance(dragArmType, true);
                dragArmType.GetField("pinned").SetValue(dragArm, i < 2);
                dragArmType.GetField("foot").SetValue(dragArm, origin + Vector3.forward * 3f + Vector3.right * (i - 1));
                dragArms.SetValue(dragArm, i);
            }
            Set(tentacle, "tentacles", dragArms);
            var petter = camera.gameObject.AddComponent<PlayerCreaturePetter>();
            Set(petter, "viewCamera", camera);
            var tentacleCollider = tentacle.gameObject.AddComponent<SphereCollider>();
            tentacleCollider.radius = 0.2f;
            tentacle.transform.position = origin + Vector3.forward;
            Physics.SyncTransforms();
            Call(petter, "StepHold", true, 0.1f, 0f);
            Check(!petter.IsPetting, "a brief click does not start a drag");
            Call(petter, "StepHold", true, 0.15f, 0f);
            Check(petter.Petted == tentacle && tentacle.Dragged, "holding starts dragging while still aimed at the creature");
            Check(Vector3.Distance(tentacle.PetPoint, tentacle.transform.position) < 0.001f,
                "the initial drag attaches to the body centre rather than the ray's arm/surface hit");
            bool armAttachedToHand = false;
            foreach (object dragArm in dragArms) armAttachedToHand |= (bool)dragArmType.GetField("touchingHand").GetValue(dragArm);
            Check(!armAttachedToHand && tentacle.PinnedCount == 2,
                "body dragging does not recruit a tentacle to wrap the hand or disturb the two shot arms");
            camera.transform.rotation = Quaternion.LookRotation(Vector3.right);
            Call(petter, "StepHold", true, 0.1f, 0f);
            Check(petter.Petted == tentacle && tentacle.Dragged, "turning the view does not lose or switch a held tentacle");
            float oldDistance = (float)Get(petter, "dragDistance");
            Call(petter, "StepHold", true, 0.1f, 120f);
            Check((float)Get(petter, "dragDistance") > oldDistance, "scrolling adjusts the held distance");
            Check(Vector3.Distance(tentacle.PetPoint, petter.DragTarget) < 0.001f
                && Vector3.Distance(petter.TouchPoint, tentacle.transform.position) < 0.7f,
                "pull destination is independent of the visible hand's grip on the central body");
            Vector3 oldTouch = petter.TouchPoint;
            tentacle.transform.position += Vector3.right * 0.5f;
            Check(Vector3.Distance(petter.TouchPoint, oldTouch) > 0.1f,
                "the hand grip follows late body movement without waiting for the next input update");
            var tether = Make("Central spring", origin).AddComponent<TentacleTether>();
            // The spring now checks the full body clearance; start this drag fixture above the floor.
            tentacle.transform.position += Vector3.up;
            Type cordType = typeof(TentacleTether).GetNestedType("Cord", BindingFlags.NonPublic);
            object cord = Activator.CreateInstance(cordType, true);
            Vector3 midpoint = tentacle.transform.position;
            cordType.GetField("a").SetValue(cord, midpoint + Vector3.left * 3f);
            cordType.GetField("b").SetValue(cord, midpoint + Vector3.right * 3f);
            cordType.GetField("pocket").SetValue(cord, midpoint);
            cordType.GetField("creature").SetValue(cord, tentacle);
            Call(tether, "Spring", cord, 0.25f);
            Vector3 pulledMiddle = (Vector3)cordType.GetField("pocket").GetValue(cord);
            Check(Vector3.Distance(pulledMiddle, petter.DragTarget) < Vector3.Distance(midpoint, petter.DragTarget),
                "dragging a two-anchor spring pulls its middle pocket toward the body target");
            Check(tentacle.PinnedCount == 2, "pulling the middle leaves both end anchors pinned");
            Call(petter, "StepHold", false, 0.1f, 0f);
            Check(!petter.IsPetting && !tentacle.Dragged, "releasing the button releases the drag immediately");
            camera.transform.rotation = Quaternion.identity;
            tentacle.transform.position = origin + Vector3.right * 6f;
            Object.DestroyImmediate(tentacleCollider);
            wall = Make("Drag obstacle", tentacle.transform.position + Vector3.forward * 2f).AddComponent<BoxCollider>();
            wall.gameObject.layer = 7;
            wall.size = new Vector3(4f, 4f, 0.2f);
            Physics.SyncTransforms();
            Vector3 before = tentacle.transform.position;
            Call(tentacle, "MoveOnSurface", Vector3.forward * 10f);
            Check(tentacle.transform.position.z < before.z + 1.6f && tentacle.transform.position.z > before.z,
                "a long drag step stops at the obstacle rather than tunnelling through");
            Call(tentacle, "MoveOnSurface", Vector3.down * 10f);
            Check(tentacle.transform.position.y >= origin.y - 0.14f, "dragging down never buries its body in terrain");
            Object.DestroyImmediate(wall.gameObject);

            // Use a real procedural arm curve without constructing cosmetic meshes in Edit Mode.
            Type armType = typeof(TentacleCreature).GetNestedType("Tentacle", BindingFlags.NonPublic);
            object arm = Activator.CreateInstance(armType, true);
            void Arm(string name, object value) => armType.GetField(name).SetValue(arm, value);
            Vector3 pin = origin + Vector3.forward * 4f;
            Arm("pinned", true); Arm("foot", pin); Arm("root", origin); Arm("tip", pin);
            Arm("c1", origin + Vector3.forward); Arm("c2", origin + Vector3.forward * 3f);
            Arm("thickness", 1f);
            Array armArray = Array.CreateInstance(armType, 1); armArray.SetValue(arm, 0);
            Set(tentacle, "tentacles", armArray);
            var strip = Make("Wide bridge", origin + Vector3.forward * 2f);
            var stripCollider = strip.AddComponent<BoxCollider>();
            var surface = strip.AddComponent<TentacleGripSurface>();
            surface.Setup(tentacle, pin);
            float parameter = surface.Capture(origin + Vector3.forward * 2f + Vector3.right * 0.5f);
            Check(surface.Resolve(parameter, Vector3.right, out Vector3 grip) && Mathf.Abs(grip.x - origin.x) < 0.3f,
                "grips project from the invisible wide walking strip onto the visible arm");
            var climber = Make("Climber", origin).AddComponent<HandClimber>();
            Set(climber, "climbing", true);
            Array hands = (Array)Get(climber, "hands");
            object hand = hands.GetValue(0);
            hand.GetType().GetField("normal").SetValue(hand, Vector3.right);
            hands.SetValue(hand, 0);
            Call(climber, "AttachGrip", 0, stripCollider, origin + Vector3.forward * 2f);
            Check(climber.GetHand(0, out Vector3 oldGrip, out _, out bool gripping) && gripping, "a planted grip is available to arm IK");
            Arm("root", origin + Vector3.up); Arm("tip", pin + Vector3.up);
            Arm("c1", origin + Vector3.forward + Vector3.up); Arm("c2", origin + Vector3.forward * 3f + Vector3.up);
            Check(climber.GetHand(0, out Vector3 movedGrip, out _, out _) && Mathf.Abs(movedGrip.y - oldGrip.y - 1f) < 0.01f,
                "a planted hand follows the animated arm without requiring another physics frame");

            var decorObject = Make("Decoration builder", origin);
            decorObject.SetActive(false);
            var decorator = decorObject.AddComponent<WorldDecorator>();
            Type bufferType = typeof(WorldDecorator).GetNestedType("Buffer", BindingFlags.NonPublic);
            foreach (WorldDecorator.Group group in new[] { WorldDecorator.Group.Organic, WorldDecorator.Group.Inner, WorldDecorator.Group.InnerGlow })
            {
                object buffer = Activator.CreateInstance(bufferType, true);
                ((List<Vector3>)bufferType.GetField("vertices").GetValue(buffer)).AddRange(new[] { Vector3.zero, Vector3.right, Vector3.forward });
                ((List<Vector3>)bufferType.GetField("normals").GetValue(buffer)).AddRange(new[] { Vector3.up, Vector3.up, Vector3.up });
                ((List<Vector2>)bufferType.GetField("uvs").GetValue(buffer)).AddRange(new[] { Vector2.zero, Vector2.zero, Vector2.zero });
                ((List<int>)bufferType.GetField("triangles").GetValue(buffer)).AddRange(new[] { 0, 2, 1 });
                var chunk = Make("Decor chunk", origin);
                Call(decorator, "CreateDecorObject", chunk, group, buffer);
                var collider = chunk.GetComponentInChildren<MeshCollider>();
                Check(collider != null && collider.sharedMesh != null, group + " decorations are solid");
                meshes.Add(collider.sharedMesh);
            }

            object placement = Activator.CreateInstance(bufferType, true);
            var shape = new DecorMesh();
            shape.vertices.AddRange(new[] { Vector3.zero, Vector3.right, Vector3.forward });
            shape.normals.AddRange(new[] { Vector3.up, Vector3.up, Vector3.up });
            shape.lengths.AddRange(new[] { 0f, 1f, 1f });
            shape.triangles.AddRange(new[] { 0, 2, 1 });
            var occupied = new List<Bounds>();
            var kind = new WorldDecorator.Kind { alignToNormal = 0f, sink = 0f };
            bool Place(Vector3 at) => (bool)Call(decorator, "Place", kind, shape, placement, at, Vector3.up, 1f,
                new System.Random(7), Matrix4x4.identity, occupied);
            Check(Place(origin), "a free decoration footprint can be placed");
            Check(!Place(origin), "a second solid decoration cannot occupy the same footprint");
            Check(Place(origin + Vector3.right * 10f), "clear nearby ground still accepts decoration");
            var cull = typeof(WorldDecorator).GetMethod("CullOccupied", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)cull.Invoke(null, new object[] { placement, new[] { new Bounds(origin, Vector3.one * 4f) } }),
                "a landmark footprint removes an intersecting decoration");
            Check(((List<int>)bufferType.GetField("triangles").GetValue(placement)).Count == 3,
                "clearance removes the whole conflicting piece and retains its neighbour");
            object slopeBuffer = Activator.CreateInstance(bufferType, true);
            Vector3 slopeNormal = new Vector3(0.5f, 1f, 0f).normalized;
            Check((bool)Call(decorator, "Place", kind, shape, slopeBuffer, origin, slopeNormal, 1f,
                new System.Random(7), Matrix4x4.identity, new List<Bounds>()), "a moderate slope still permits placement");
            bool clearsSlope = true;
            foreach (Vector3 vertex in (List<Vector3>)bufferType.GetField("vertices").GetValue(slopeBuffer))
                clearsSlope &= Vector3.Dot(vertex - origin, slopeNormal) >= -0.032f;
            Check(clearsSlope, "the whole base footprint clears the slope rather than burying one side");

            var world = decorObject.GetComponent<ProceduralWorld>();
            decorObject.transform.position = origin;
            Set(world, "sinkholeCount", 0);
            Set(world, "surfaceAmplitude", 0f);
            Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));
            var terrain = Make("Actual terrain mesh", origin);
            terrain.layer = world.TerrainLayer;
            var terrainMesh = new Mesh();
            terrainMesh.vertices = new[] { new Vector3(-20f, 0f, -20f), new Vector3(20f, 0f, -20f), new Vector3(-20f, 0f, 20f), new Vector3(20f, 0f, 20f) };
            terrainMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            terrain.AddComponent<MeshCollider>().sharedMesh = terrainMesh;
            terrain.AddComponent<ProceduralWorldChunk>().Initialize(world, Vector3Int.zero);
            meshes.Add(terrainMesh);
            var dressing = Make("Stream builder", origin).AddComponent<RealmDressing>();
            Set(dressing, "world", world); Set(dressing, "root", Make("Stream root", origin).transform);
            Set(dressing, "start", origin - Vector3.forward * 5f);
            Set(dressing, "end", origin + Vector3.forward * 5f);
            Set(dressing, "meander", 0f);
            Physics.SyncTransforms();
            Check((bool)Call(dressing, "PathLoaded"), "stream readiness uses actual terrain rather than requiring absent surrounding chunks");
            Call(dressing, "Stream", new System.Random(5));
            var streams = (List<GameObject>)Get(dressing, "streamPieces");
            Check(streams.Count > 0, "stream builds over real loaded ground");
            bool aboveGround = true;
            foreach (GameObject piece in streams)
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                meshes.Add(mesh);
                foreach (Vector3 vertex in mesh.vertices)
                    aboveGround &= Mathf.Abs(vertex.y - origin.y - RealmStreamSurface.Clearance) < 0.002f;
            }
            Check(aboveGround, "all generated river vertices clear the real terrain");
            Object.DestroyImmediate(terrain);
            foreach (GameObject piece in streams) Object.DestroyImmediate(piece);
            streams.Clear();
            Call(dressing, "Stream", new System.Random(5));
            Check(streams.Count == 0, "missing terrain never generates a river at an estimated buried height");
            Set(dressing, "root", null);
            Debug.Log($"Creature controls validation passed: {checks} assertions.");
        }
        finally
        {
            Object.DestroyImmediate(staging.gameObject);
            foreach (Mesh mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }
}
