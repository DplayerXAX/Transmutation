using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Play Mode reproduction of slime endpoints and deferred stream/collision work.</summary>
[InitializeOnLoad]
public static class RealmPerformanceValidation
{
    private const string Pending = "Capstone.RealmPerformanceValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static RealmPerformanceValidation()
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
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    private static void Validate()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int checks = 0;
        object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Realm performance: " + message);
            checks++;
        }
        bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        Mesh Triangle()
        {
            var mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
            mesh.triangles = new[] { 0, 2, 1 }; return mesh;
        }
        try
        {
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var worldObject = new GameObject("Performance terrain"); worldObject.SetActive(false);
            worldObject.transform.position = origin;
            var world = worldObject.AddComponent<ProceduralWorld>();
            Set(world, "sinkholeCount", 0); Set(world, "surfaceAmplitude", 0f); Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(fieldType, new object[] { world }));
            // A ridge whose crease does not coincide with the river sampling rows, plus a cave floor below it.
            var floorMesh = new Mesh();
            var floorVertices = new List<Vector3>(); var floorTriangles = new List<int>();
            for (int layer = 0; layer < 2; layer++)
            {
                int offset = floorVertices.Count;
                float drop = layer * 8f;
                foreach (Vector2 profile in new[] { new Vector2(-100f, 0f), new Vector2(4f, 0f), new Vector2(4.5f, 2f), new Vector2(100f, 0f) })
                {
                    floorVertices.Add(new Vector3(-100f, profile.y - drop, profile.x));
                    floorVertices.Add(new Vector3(100f, profile.y - drop, profile.x));
                }
                for (int row = 0; row < 3; row++)
                {
                    int a = offset + row * 2;
                    floorTriangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            floorMesh.SetVertices(floorVertices); floorMesh.SetTriangles(floorTriangles, 0);
            var floor = new GameObject("Owned floor"); floor.layer = world.TerrainLayer; floor.transform.position = origin;
            floor.AddComponent<MeshCollider>().sharedMesh = floorMesh;
            floor.AddComponent<ProceduralWorldChunk>().Initialize(world, Vector3Int.zero);
            Physics.SyncTransforms();
            var dressingObject = new GameObject("Dressing"); dressingObject.SetActive(false);
            var dressing = dressingObject.AddComponent<RealmDressing>();
            var root = new GameObject("Generated dressing").transform;
            Set(dressing, "world", world); Set(dressing, "root", root);
            Set(dressing, "start", origin); Set(dressing, "end", origin + Vector3.forward * 10.6f); Set(dressing, "built", true);
            Set(dressing, "meander", 0f);
            Set(dressing, "slimePatches", 4);
            ((List<Vector3>)Get(dressing, "lampBases")).Add(origin + Vector3.right * 3f);
            for (int seed = 0; seed < 8; seed++) Call(dressing, "Slime", new System.Random(seed));
            int slimeCount = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                bool finite = true;
                foreach (Vector3 vertex in mesh.vertices) finite &= Finite(vertex);
                foreach (Vector3 normal in mesh.normals) finite &= Finite(normal);
                Check(finite && mesh.vertexCount > 100, "real slime lumps and collars have finite endpoints, including vertex 100");
                Check(filter.GetComponent<MeshCollider>() != null && filter.GetComponent<MeshCollider>().sharedMesh == mesh,
                    "finite slime accepts its actual collision mesh");
                slimeCount++;
            }
            Check(slimeCount == 8, "multiple slime seeds exercise both end profiles");
            var tube = new RealmMeshes();
            tube.Tube(new[] { Vector3.zero, Vector3.zero, Vector3.up, Vector3.up, Vector3.right }, _ => 0.1f, 8, 0f, 1f, 0f);
            Mesh repeated = tube.Build("Repeated points");
            bool stable = true;
            foreach (Vector3 normal in repeated.normals) stable &= Finite(normal) && normal.sqrMagnitude > 0.5f;
            Check(stable, "duplicate points cannot collapse the tube frame into invalid normals");
            var rejected = new RealmMeshes();
            rejected.Tube(new[] { Vector3.zero, Vector3.up }, _ => float.NaN, 8, 0f, 1f, 0f);
            Check(rejected.VertexCount == 0, "an invalid radius is rejected before contaminating the merged mesh");

            IEnumerator stream = (IEnumerator)Call(dressing, "BuildStream", new System.Random(5), true);
            int yields = 0;
            while (stream.MoveNext())
            {
                yields++;
                Check(((List<GameObject>)Get(dressing, "streamPieces")).Count == 0, "new stream publishes only after all staged sections are ready");
                if (yields > 2000) throw new InvalidOperationException("stream did not finish");
            }
            var pieces = (List<GameObject>)Get(dressing, "streamPieces");
            Check(yields > 5 && pieces.Count > 1, "river projection and geometry split across bounded slices and short sections");
            bool small = true, above = true;
            float furthest = float.NegativeInfinity;
            foreach (GameObject piece in pieces)
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                small &= mesh.vertexCount <= 4096;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    above &= Finite(vertex) && vertex.y >= origin.y + RealmStreamSurface.Clearance - 0.002f;
                    furthest = Mathf.Max(furthest, vertex.z);
                }
            }
            Check(small && above, "small stream sections follow the exposed surface and do not stamp the cave floor below it");
            Check(furthest >= origin.z + 10.59f, "the final short section reaches the river endpoint");
            var riverColliders = new List<MeshCollider>();
            bool conforms = true, continuous = true;
            int seamMatches = 0;
            var previousVertices = new Dictionary<Vector3, Vector2>();
            foreach (GameObject piece in pieces)
            {
                Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices; Vector2[] uv = mesh.uv2;
                for (int i = 0; i < vertices.Length; i++)
                    if (previousVertices.TryGetValue(vertices[i], out Vector2 previousUV))
                    {
                        continuous &= Vector2.Distance(previousUV, uv[i]) < 0.003f; seamMatches++;
                    }
                previousVertices.Clear();
                for (int i = 0; i < vertices.Length; i++) previousVertices[vertices[i]] = uv[i];
                var collider = piece.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; riverColliders.Add(collider);
                // Test triangle interiors as well as vertices: a sampled height grid would bridge this sharp crease.
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 centre = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                    var ray = new Ray(centre + Vector3.up * 20f, Vector3.down);
                    conforms &= floor.GetComponent<MeshCollider>().Raycast(ray, out RaycastHit hit, 40f)
                        && Mathf.Abs(centre.y - hit.point.y - RealmStreamSurface.Clearance) < 0.003f;
                }
            }
            Check(conforms, "projected triangle interiors follow slopes and sharp terrain creases within 3mm of the intended clearance");
            Check(seamMatches >= 4 && continuous, "adjacent render sections share the same positions and continuous flow UVs");
            bool covered = true;
            Physics.SyncTransforms();
            for (int i = 1; i < 80; i++)
            {
                Vector3 point = (Vector3)Call(dressing, "PathPoint", i / 80f);
                var ray = new Ray(point + Vector3.up * 20f, Vector3.down);
                bool hit = false;
                foreach (MeshCollider collider in riverColliders) hit |= collider.Raycast(ray, out _, 40f);
                covered &= hit;
            }
            Check(covered, "the river remains continuous across steep slopes, terrain creases and render-section boundaries");
            Set(dressing, "streamDirty", false);
            Call(dressing, "TerrainChanged", world, new Bounds(origin + Vector3.down * 30f, Vector3.one * 10f));
            Check(!(bool)Get(dressing, "streamDirty"), "an underground chunk update does not rebuild the surface river");
            var surfaceEdit = new Bounds(origin + Vector3.forward * 10f, new Vector3(8f, 4f, 8f));
            Call(dressing, "TerrainChanged", world, surfaceEdit);
            Check(pieces.TrueForAll(piece => piece.GetComponent<Renderer>().enabled), "terrain refresh retains the old river until its replacement is ready");
            float refresh = (float)Get(dressing, "streamRefreshAt");
            Call(dressing, "TerrainChanged", world, surfaceEdit);
            Check((bool)Get(dressing, "streamDirty") && (float)Get(dressing, "streamRefreshAt") == refresh,
                "repeated surface edits coalesce without postponing the refresh forever");
            Set(dressing, "nextStreamRefresh", Time.time + 2f); Set(dressing, "streamRefreshAt", Time.time - 1f);
            Call(dressing, "Update");
            Check(!dressing.StreamBuilding, "refresh cooldown prevents a complete rebuild every terrain tick");

            var decorObject = new GameObject("Async decor"); decorObject.SetActive(false);
            var decorator = decorObject.AddComponent<WorldDecorator>();
            // Keep cold editor lookups out of this pacing assertion; the collider count limit is independent.
            Set(decorator, "updateBudgetMilliseconds", 100f);
            var chunk = new GameObject("Changing chunk");
            var oldDecor = new GameObject("Decor old", typeof(MeshFilter), typeof(MeshRenderer));
            oldDecor.transform.SetParent(chunk.transform);
            Mesh oldMesh = Triangle(); oldDecor.GetComponent<MeshFilter>().sharedMesh = oldMesh;
            Type pendingType = typeof(WorldDecorator).GetNestedType("PendingCollider", BindingFlags.NonPublic);
            object pending = Activator.CreateInstance(pendingType, true);
            var completion = new TaskCompletionSource<bool>();
            pendingType.GetField("mesh").SetValue(pending, oldMesh); pendingType.GetField("decor").SetValue(pending, oldDecor);
            pendingType.GetField("bake").SetValue(pending, completion.Task);
            var pendingList = (IList)Get(decorator, "pendingColliders"); pendingList.Add(pending);
            ((IList)Get(decorator, "built")).Add((chunk, oldMesh));
            // A regression would wait one second here; a correct refresh returns while baking is unfinished.
            _ = Task.Run(async () => { await Task.Delay(1000); completion.TrySetResult(true); });
            Call(decorator, "Redecorate", chunk, null, default(Bounds));
            Check(!completion.Task.IsCompleted && (bool)pendingType.GetField("retired").GetValue(pending),
                "terrain refresh retires old collision without blocking on its worker");
            Check(oldMesh != null && oldMesh.vertexCount == 3, "native mesh remains alive until collision cooking completes");
            completion.TrySetResult(true);
            Call(decorator, "Update");
            Check(pendingList.Count == 0, "finished retired collision is reclaimed on a later update");
            for (int i = 0; i < 5; i++)
            {
                var obj = new GameObject("Ready decor", typeof(MeshFilter), typeof(MeshRenderer));
                Mesh mesh = Triangle(); obj.GetComponent<MeshFilter>().sharedMesh = mesh;
                object entry = Activator.CreateInstance(pendingType, true);
                pendingType.GetField("mesh").SetValue(entry, mesh); pendingType.GetField("decor").SetValue(entry, obj);
                pendingType.GetField("bake").SetValue(entry, Task.CompletedTask); pendingList.Add(entry);
            }
            Call(decorator, "Update");
            Check(pendingList.Count >= 3, "completed colliders are attached over multiple frames instead of one unbounded burst");
            Debug.Log($"Realm performance Play Mode validation passed: {checks} assertions.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
