using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Authors Fungal Hollow: one big cavern overgrown with mushrooms, explored freely rather than in order.
/// Mushrooms are the terrain. Green caps (the BounceShroom's colour) always bounce, blue caps are solid
/// to stand on, shelf fungi climb the slime walls, and carryable BounceShrooms grow in nurseries for the
/// spots no fixed cap reaches. Six pockets hang at different heights, most with more than one way in.
/// The walls are slime (SlipperySurface): hands grab but slide, except one rough chimney.
/// Build also runs a reachability pass over every standable top and logs how each pocket can be reached.
/// </summary>
public static class FungalHollowSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_FungalHollow.unity";
    private const string Generated = "Assets/Generated/FungalHollow";
    private const string ShroomPath = "Assets/Prefabs/Creatures/BounceShroom.prefab";
    private const float WallRadius = 68f, WallThickness = 6f, CeilingBottom = 60f;
    private const int WallSegments = 28, ChimneySegment = 7; // segment 7 faces +z (north)
    // Measured in play: launch speeds give apexes of ~0.95 v²/2g (gravity 30). The jump is 12 m/s.
    private const float Gravity = 30f, FallGravity = 51f, JumpSpeed = 12f, AirSpeed = 5.5f, CarriedLaunch = 22f;
    private static readonly Vector2 Bog = new Vector2(-22f, -12f);
    private static readonly Color BounceGlow = new Color(0.35f, 1f, 0.55f), SolidGlow = new Color(0.4f, 0.65f, 1f), RelicGlow = new Color(1f, 0.8f, 0.45f);

    private static Material rockMat, floorMat, slimeMat, roughMat, stalkMat, bounceMat, solidMat, glowMat, sporeMat;
    private static Mesh capMesh;
    private static RealmMeshes stalks, decor;
    private static System.Random random;
    private static Transform lights;
    private static readonly List<Surface> surfaces = new List<Surface>();
    private static readonly List<Vector2> keepClear = new List<Vector2>();

    /// <summary>A standable top for the reachability pass. Launch 0 = solid (jump only).</summary>
    private sealed class Surface
    {
        public string name; public Vector3 top; public float radius, launch;
        public string pocket; public bool nursery, climbFromFloor;
    }

    [MenuItem("Tools/Capstone/Fungal Hollow/Build New Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building Fungal Hollow.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException(ScenePath + " already exists. Edit its saved objects, or delete the scene to rebuild it.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard changes in the open scenes first.");

        EnsureFolder(Generated);
        rockMat = ReadableMaterial("Assets/Materials/World/MAT_Decor_Stone.mat", "HollowRock", new Color(0.36f, 0.36f, 0.42f));
        floorMat = ReadableMaterial("Assets/Materials/World/MAT_Decor_Organic.mat", "HollowFloor", new Color(0.26f, 0.3f, 0.29f));
        slimeMat = ReadableMaterial("Assets/Materials/World/MAT_Decor_Stone.mat", "SlimeWall", new Color(0.3f, 0.42f, 0.38f));
        roughMat = ReadableMaterial("Assets/Materials/World/MAT_Realm_Basalt.mat", "RoughChimney", new Color(0.3f, 0.29f, 0.3f));
        stalkMat = ReadableMaterial("Assets/Materials/World/MAT_Decor_Organic.mat", "Stalk", new Color(0.82f, 0.8f, 0.72f));
        bounceMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/BounceShroom.mat");
        solidMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
        glowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_Mushroom.mat");
        sporeMat = SporeMaterial();
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var shroomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShroomPath);
        if (rockMat == null || floorMat == null || slimeMat == null || roughMat == null || stalkMat == null || bounceMat == null ||
            solidMat == null || glowMat == null || playerPrefab == null || shroomPrefab == null)
            throw new InvalidOperationException("Fungal Hollow needs the existing world materials, player and BounceShroom prefabs.");

        surfaces.Clear();
        keepClear.Clear();
        random = new System.Random(11);
        stalks = new RealmMeshes();
        decor = new RealmMeshes();
        capMesh = SaveMesh(CapMesh(), "Cap");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("Fungal Hollow").transform;
        lights = Group(root, "Cap Glow Lights");
        BuildShell(Group(root, "Cavern — floor, slime walls, ceiling"));

        var arrival = Group(root, "Arrival Ledge (south)");
        Vector3 arrivalTop = new Vector3(0f, 9f, -59.5f);
        Box(arrival, "Arrival Ledge", arrivalTop - Vector3.up * 0.5f, new Vector3(10f, 1f, 9f), rockMat, true);
        Add("Arrival Ledge", arrivalTop, 4f, 0f);

        var shrooms = Group(root, "Carryable BounceShrooms (nurseries)");
        BuildCrown(Group(root, "Crown — spiral of bounce caps up the great mushroom, Sky Vent above"));
        BuildEast(Group(root, "East — shelf-fungus stair on the slime wall, or the bounce caps"), shroomPrefab, scene);
        BuildWest(Group(root, "West Nest — only a carried mushroom reaches it"), shrooms, shroomPrefab, scene);
        BuildBog(Group(root, "Bog — sunken bounce garden"));
        BuildChimney(Group(root, "North Chimney — the one rough wall, climb to the exit"));
        Nursery(shrooms, shroomPrefab, scene, "Hub Nursery", new Vector2(12f, -30f), 3);
        Scatter(Group(root, "Floor Caps (scattered)"));

        Part(root, "Mushroom Stalks", stalks, stalkMat, "Stalks");
        Part(root, "Glow Mushrooms (decor)", decor, glowMat, "GlowDecor");
        Spores(root);

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        // PlayerObjects must stay a scene root: carry collision filtering uses transform.root.
        player.transform.SetPositionAndRotation(arrivalTop + Vector3.up * 1.15f, Quaternion.identity);
        player.name = "PlayerObjects";
        foreach (GameObject existing in scene.GetRootGameObjects())
            if (existing.name == "Main Camera")
            {
                existing.name = "Original Scene Camera (inactive)";
                existing.SetActive(false);
            }
        var hud = Group(root, "Interaction Hints").gameObject.AddComponent<InteractionPromptHUD>();
        var hudFields = new SerializedObject(hud);
        hudFields.FindProperty("showText").boolValue = true;
        hudFields.FindProperty("hintsVisible").boolValue = true;
        hudFields.FindProperty("carrier").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureCarrier>(true);
        hudFields.FindProperty("interactor").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureInteractor>(true);
        hudFields.ApplyModifiedPropertiesWithoutUndo();

        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.gameObject.scene == scene && light.type == LightType.Directional)
            {
                // A faint fill under the ceiling; the caps and the vent do the real lighting.
                light.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
                light.intensity = 0.35f;
                light.shadows = LightShadows.None;
                light.color = new Color(0.75f, 0.85f, 1f);
            }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.36f, 0.42f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.22f, 0.3f, 0.32f);
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 140f;
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, 20f, 0f), Quaternion.Euler(30f, 20f, 0f), 110f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Fungal Hollow saved to " + ScenePath + ".\n" + Reachability());
    }

    // ---------------- Areas ----------------

    private static void BuildShell(Transform parent)
    {
        var floor = new GameObject("Cavern Floor", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        floor.transform.SetParent(parent, false);
        Walkable(floor);
        Mesh mesh = SaveMesh(FloorMesh(), "Floor");
        floor.GetComponent<MeshFilter>().sharedMesh = mesh;
        floor.GetComponent<MeshCollider>().sharedMesh = mesh;
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        for (float x = -60f; x <= 60f; x += 5f)
            for (float z = -60f; z <= 60f; z += 5f)
                if (new Vector2(x, z).magnitude < 63f) Add("Floor", new Vector3(x, H(x, z), z), 2.5f, 0f);

        var walls = Group(parent, "Slime Walls");
        float step = 360f / WallSegments;
        float chord = 2f * WallRadius * Mathf.Tan(step * 0.5f * Mathf.Deg2Rad) + 1f;
        for (int i = 0; i < WallSegments; i++)
        {
            bool rough = i == ChimneySegment;
            float angle = i * step * Mathf.Deg2Rad;
            var wall = Box(walls, rough ? "Rough Chimney Wall — climbable" : "Slime Wall " + i,
                new Vector3(Mathf.Cos(angle) * WallRadius, 30f, Mathf.Sin(angle) * WallRadius), new Vector3(WallThickness, 84f, chord), rough ? roughMat : slimeMat, true);
            wall.transform.rotation = Quaternion.Euler(0f, -i * step, 0f);
            if (!rough) wall.AddComponent<SlipperySurface>();
        }

        // Ceiling with the Sky Vent hole over the Crown; a rough shaft above it leads out.
        var ceiling = Group(parent, "Ceiling and Sky Vent");
        const float y = CeilingBottom + 3f, x0 = 0f, x1 = 16f, z0 = 2f, z1 = 26f;
        Box(ceiling, "Ceiling West", new Vector3(-40f, y, 0f), new Vector3(80f, 6f, 160f), rockMat, false);
        Box(ceiling, "Ceiling East", new Vector3(48f, y, 0f), new Vector3(64f, 6f, 160f), rockMat, false);
        Box(ceiling, "Ceiling South", new Vector3(8f, y, -39f), new Vector3(16f, 6f, 82f), rockMat, false);
        Box(ceiling, "Ceiling North", new Vector3(8f, y, 53f), new Vector3(16f, 6f, 54f), rockMat, false);
        Box(ceiling, "Vent Shaft West", new Vector3(x0 - 1f, 80f, (z0 + z1) * 0.5f), new Vector3(2f, 34f, z1 - z0), roughMat, true);
        Box(ceiling, "Vent Shaft East", new Vector3(x1 + 1f, 80f, (z0 + z1) * 0.5f), new Vector3(2f, 34f, z1 - z0), roughMat, true);
        Box(ceiling, "Vent Shaft South", new Vector3(8f, 80f, z0 - 1f), new Vector3(18f, 34f, 2f), roughMat, true);
        Box(ceiling, "Vent Shaft North", new Vector3(8f, 80f, z1 + 1f), new Vector3(18f, 34f, 2f), roughMat, true);
        var shaft = new GameObject("Sky Vent Light").AddComponent<Light>();
        shaft.transform.SetParent(ceiling, false);
        shaft.transform.SetPositionAndRotation(new Vector3(8f, 95f, 14f), Quaternion.Euler(90f, 0f, 0f));
        shaft.type = LightType.Spot;
        shaft.range = 110f;
        shaft.spotAngle = 22f;
        shaft.intensity = 3f;
        shaft.color = new Color(1f, 0.95f, 0.85f);
    }

    /// <summary>The great mushroom in the middle, ringed by a rising spiral of bounce caps.</summary>
    private static void BuildCrown(Transform parent)
    {
        var centre = new Vector2(8f, 10f);
        Surface crown = Cap(parent, "Great Mushroom — Crown", centre, 36f, 7f, 0f);
        Relic(parent, "Crown", new Vector3(centre.x - 3f, 36f, centre.y - 2f), crown);
        // A tall puffball on the crown throws you up under the vent.
        Cap(parent, "Crown Puffball — big launch", new Vector2(centre.x, centre.y + 4f), 37f, 1.6f, 30f, stalkFrom: 36f);
        var ledge = Group(parent, "Sky Vent Ledge");
        Box(ledge, "Vent Ledge", new Vector3(8f, 48.5f, 20f), new Vector3(7f, 1f, 6f), rockMat, true);
        Box(ledge, "Vent Column — rough, climb out", new Vector3(8f, 70f, 24f), new Vector3(3f, 44f, 2.5f), roughMat, true);
        Add("Sky Vent Ledge", new Vector3(8f, 49f, 20f), 3f, 0f, pocket: "Sky Vent (exit, climb the column)");
        Lamp(new Vector3(8f, 51f, 20f), 10f, 2f, RelicGlow);

        float[] tops = { 7f, 13f, 19f, 25f, 31f };
        for (int i = 0; i < tops.Length; i++)
        {
            float angle = (190f + i * 40f) * Mathf.Deg2Rad;
            Cap(parent, "Spiral Bounce Cap " + (i + 1), centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12.5f, tops[i], 3.2f, 22f);
        }
        float first = 190f * Mathf.Deg2Rad;
        Pad(parent, "Spiral Start Pad", centre + new Vector2(Mathf.Cos(first), Mathf.Sin(first)) * 19.5f, 1.6f, 22f);
    }

    /// <summary>Two ways to the east alcove: shelf fungi up the slime wall (jumps), or three bounce caps.</summary>
    private static void BuildEast(Transform parent, GameObject shroomPrefab, Scene scene)
    {
        // Laid from the alcove down and back along the wall, so the top step always meets it.
        float inner = WallRadius - WallThickness * 0.5f;
        int count = 1;
        for (float a = -5f, top = 21.9f; ; a -= 4.6f, top -= 1.6f, count++)
        {
            Bracket(parent, "Shelf Fungus " + count + " (from the top)", a, top, 3f);
            Vector3 foot = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * (inner - 4f);
            if (top - H(foot.x, foot.z) < 1.7f) break;
        }
        Alcove(parent, "East Alcove", 0f, 23.5f);
        Pad(parent, "East Pad", new Vector2(39.5f, -5f), 1.6f, 22f);
        Cap(parent, "East Bounce Cap", new Vector2(46f, -1f), 8.5f, 3f, 22f);
        // The sentinel throws harder (26): its bounce clears the nine metres up to the alcove.
        Cap(parent, "East Sentinel", new Vector2(53f, 4f), 14.5f, 3.4f, 26f);
    }

    /// <summary>Caps climb toward the west wall but stop short; a nursery on the last one does the rest.</summary>
    private static void BuildWest(Transform parent, Transform shrooms, GameObject shroomPrefab, Scene scene)
    {
        Pad(parent, "West Pad", new Vector2(-22f, 28f), 1.6f, 22f);
        Cap(parent, "West Bounce Cap 1", new Vector2(-28f, 22f), 7f, 3f, 22f);
        Cap(parent, "West Blue Cap 1", new Vector2(-36f, 16f), 13f, 4.5f, 0f);
        Cap(parent, "West Bounce Cap 2", new Vector2(-44f, 9f), 14f, 3f, 22f);
        Cap(parent, "West Blue Cap 2 — nursery", new Vector2(-50f, 0f), 19.5f, 5f, 0f);
        Nursery(shrooms, shroomPrefab, scene, "West Cap Nursery", new Vector2(-50f, 0f), 2, 19.5f);
        Alcove(parent, "West Nest", 180f, 26f);
    }

    /// <summary>A sunken bog of small bounce caps stepping up to a thin blue spire.</summary>
    private static void BuildBog(Transform parent)
    {
        float[] tops = { -3f, 1f, 5f };
        Vector2[] spots = { new Vector2(-16f, -6f), new Vector2(-21f, -2.5f), new Vector2(-26.5f, -5.5f) };
        for (int i = 0; i < tops.Length; i++) Cap(parent, "Bog Step Cap " + (i + 1), spots[i], tops[i], 2.2f, 19f);
        Surface spire = Cap(parent, "Bog Spire", new Vector2(-25f, -12f), 9.5f, 2.5f, 0f);
        Relic(parent, "Bog Spire", new Vector3(-25f, 9.5f, -12f), spire);
        for (int i = 0; i < 9; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = 4f + (float)random.NextDouble() * 9f;
            var at = Bog + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            if (Clear(at, 3.5f)) Pad(parent, "Bog Pad " + (i + 1), at, Mathf.Lerp(1f, 1.5f, (float)random.NextDouble()), Mathf.Lerp(14f, 17f, (float)random.NextDouble()));
        }
    }

    private static void BuildChimney(Transform parent)
    {
        Alcove(parent, "Chimney Top (exit)", ChimneySegment * 360f / WallSegments, 34f, climbFromFloor: true);
    }

    /// <summary>Small bounce pads and solid caps over the open floor, clear of the routes.</summary>
    private static void Scatter(Transform parent)
    {
        int pads = 0, solids = 0;
        for (int attempt = 0; attempt < 400 && (pads < 22 || solids < 28); attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = Mathf.Sqrt((float)random.NextDouble()) * 55f;
            var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            if (!Clear(at, 6f) || (at - Bog).magnitude < 15f) continue;
            if (pads < 22 && random.NextDouble() < 0.45)
                Pad(parent, "Floor Pad " + ++pads, at, Mathf.Lerp(1f, 1.7f, (float)random.NextDouble()), Mathf.Lerp(14f, 19f, (float)random.NextDouble()));
            else if (solids < 28)
            {
                float radius = Mathf.Lerp(1.2f, 2.8f, (float)random.NextDouble());
                Cap(parent, "Floor Cap " + ++solids, at, H(at.x, at.y) + Mathf.Lerp(0.8f, 2.6f, (float)random.NextDouble()), radius, 0f);
            }
        }
        for (int i = 0; i < 110; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = Mathf.Sqrt((float)random.NextDouble()) * 60f;
            var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            int count = random.Next(3, 8);
            for (int k = 0; k < count; k++)
            {
                var p = at + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 2.5f;
                RealmFlora.Mushroom(decor, new Vector3(p.x, H(p.x, p.y) - 0.05f, p.y), Vector3.up, Mathf.Lerp(0.4f, 1.1f, (float)random.NextDouble()), random);
            }
        }
    }

    // ---------------- Pieces ----------------

    /// <summary>A mushroom whose cap top (the standable collider top) is at capTop. Launch 0 = solid blue cap.</summary>
    private static Surface Cap(Transform parent, string name, Vector2 at, float capTop, float radius, float launch, float stalkFrom = float.NaN)
    {
        bool bouncy = launch > 0f;
        var tree = Group(parent, name);
        float origin = capTop - 0.35f * radius;
        float ground = float.IsNaN(stalkFrom) ? H(at.x, at.y) - 0.3f : stalkFrom;
        float stalkRadius = 0.25f + radius * 0.12f;
        if (origin - ground > 0.3f)
        {
            Stalk(new Vector3(at.x, ground, at.y), origin - ground + 0.1f * radius, stalkRadius);
            var stalk = new GameObject("Stalk Collider", typeof(CapsuleCollider));
            stalk.transform.SetParent(tree, false);
            var capsule = stalk.GetComponent<CapsuleCollider>();
            stalk.transform.position = new Vector3(at.x, (ground + origin) * 0.5f, at.y);
            capsule.height = origin - ground;
            capsule.radius = stalkRadius;
        }
        var cap = new GameObject(bouncy ? "Bounce Cap" : "Solid Cap", typeof(MeshFilter), typeof(MeshRenderer));
        cap.transform.SetParent(tree, false);
        cap.transform.position = new Vector3(at.x, origin, at.y);
        cap.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        cap.transform.localScale = Vector3.one * radius;
        cap.GetComponent<MeshFilter>().sharedMesh = capMesh;
        cap.GetComponent<MeshRenderer>().sharedMaterial = bouncy ? bounceMat : solidMat;
        var platform = new GameObject("Platform", typeof(BoxCollider));
        platform.transform.SetParent(cap.transform, false);
        Walkable(platform);
        platform.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        platform.transform.localScale = new Vector3(1.5f, 0.3f, 1.5f);
        if (bouncy)
        {
            var body = cap.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var spring = new SerializedObject(cap.AddComponent<BouncySurface>());
            spring.FindProperty("platformCollider").objectReferenceValue = platform.GetComponent<BoxCollider>();
            spring.FindProperty("launchSpeed").floatValue = launch;
            spring.ApplyModifiedPropertiesWithoutUndo();
        }
        if (radius >= 2.5f) Lamp(new Vector3(at.x, origin - 0.6f, at.y), radius * 3f, bouncy ? 2.2f : 1.6f, bouncy ? BounceGlow : SolidGlow);
        keepClear.Add(at);
        return Add(name, new Vector3(at.x, capTop, at.y), radius * 0.75f, launch);
    }

    private static Surface Pad(Transform parent, string name, Vector2 at, float radius, float launch) =>
        Cap(parent, name, at, H(at.x, at.y) + 0.6f + radius * 0.3f, radius, launch);

    /// <summary>A shelf fungus half sunk into the wall at this angle (degrees), standable.</summary>
    private static void Bracket(Transform parent, string name, float angleDegrees, float top, float radius)
    {
        float angle = angleDegrees * Mathf.Deg2Rad, inner = WallRadius - WallThickness * 0.5f;
        var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        var shelf = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        shelf.transform.SetParent(parent, false);
        shelf.transform.position = outward * inner + Vector3.up * (top - 0.35f * radius);
        shelf.transform.rotation = Quaternion.LookRotation(outward) * Quaternion.Euler(-6f, 0f, 0f);
        shelf.transform.localScale = new Vector3(radius, radius * 0.7f, radius);
        shelf.GetComponent<MeshFilter>().sharedMesh = capMesh;
        shelf.GetComponent<MeshRenderer>().sharedMaterial = solidMat;
        var platform = new GameObject("Platform", typeof(BoxCollider));
        platform.transform.SetParent(shelf.transform, false);
        Walkable(platform);
        platform.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        platform.transform.localScale = new Vector3(1.6f, 0.3f, 1.6f);
        Add(name, outward * (inner - radius * 0.45f) + Vector3.up * top, radius * 0.55f, 0f);
    }

    /// <summary>A rock ledge on the wall at this angle with a relic: a pocket to find.</summary>
    private static void Alcove(Transform parent, string name, float angleDegrees, float top, bool climbFromFloor = false)
    {
        float angle = angleDegrees * Mathf.Deg2Rad, inner = WallRadius - WallThickness * 0.5f;
        var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        var group = Group(parent, name);
        var ledge = Box(group, name + " Ledge", outward * (inner - 3.5f) + Vector3.up * (top - 0.6f), new Vector3(8f, 1.2f, 7f), rockMat, true);
        ledge.transform.rotation = Quaternion.LookRotation(Vector3.Cross(outward, Vector3.up));
        Relic(group, name, outward * (inner - 2f) + Vector3.up * top);
        Add(name, outward * (inner - 3.5f) + Vector3.up * top, 3.5f, 0f, pocket: name, climbFromFloor: climbFromFloor);
    }

    private static void Relic(Transform parent, string pocket, Vector3 at, Surface owner = null)
    {
        var relic = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        relic.name = "Relic — " + pocket + " (placeholder)";
        relic.transform.SetParent(parent, false);
        relic.transform.position = at + Vector3.up * 0.7f;
        relic.transform.localScale = Vector3.one * 0.8f;
        Object.DestroyImmediate(relic.GetComponent<Collider>());
        relic.GetComponent<Renderer>().sharedMaterial = glowMat;
        Lamp(at + Vector3.up * 1.5f, 9f, 2.5f, RelicGlow);
        if (owner != null) owner.pocket = pocket;
    }

    private static void Nursery(Transform parent, GameObject prefab, Scene scene, string name, Vector2 at, int count, float top = float.NaN)
    {
        float y = float.IsNaN(top) ? H(at.x, at.y) : top;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(parent, false);
            instance.transform.position = new Vector3(at.x + Mathf.Cos(angle) * 1.6f, y + 0.08f, at.y + Mathf.Sin(angle) * 1.6f);
            instance.name = name + " — BounceShroom " + (i + 1);
            instance.AddComponent<ReturnHome>();
        }
        keepClear.Add(at);
        foreach (var s in surfaces)
            if (new Vector2(s.top.x - at.x, s.top.z - at.y).magnitude < s.radius + 2f && Mathf.Abs(s.top.y - y) < 1f) s.nursery = true;
        if (float.IsNaN(top)) Add(name, new Vector3(at.x, y, at.y), 2.5f, 0f).nursery = true;
    }

    private static void Stalk(Vector3 foot, float height, float radius)
    {
        var path = new List<Vector3>();
        Vector2 lean = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * Mathf.Min(1.2f, height * 0.05f);
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f, bend = Mathf.Sin(t * Mathf.PI);
            path.Add(foot + new Vector3(lean.x * bend, height * t, lean.y * bend));
        }
        // Flared foot, slim neck under the cap.
        stalks.Tube(path, t => radius * (1f + 0.6f * (1f - t) * (1f - t) * (1f - t) - 0.15f * t), 10, 0f, 1f, (float)random.NextDouble());
    }

    private static void Spores(Transform root)
    {
        var go = new GameObject("Drifting Spores", typeof(ParticleSystem));
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(0f, 26f, 0f);
        var system = go.GetComponent<ParticleSystem>();
        var main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 30f;
        main.startSpeed = 0.15f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 1f, 0.7f, 0.85f), new Color(0.7f, 0.85f, 1f, 0.85f));
        main.gravityModifier = -0.003f;
        main.maxParticles = 2500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = system.emission;
        emission.rateOverTime = 80f;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(110f, 50f, 110f);
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.15f;
        if (sporeMat != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = sporeMat;
    }

    // ---------------- Reachability ----------------

    private static Surface Add(string name, Vector3 top, float radius, float launch, string pocket = null, bool climbFromFloor = false)
    {
        var s = new Surface { name = name, top = top, radius = radius, launch = launch, pocket = pocket, climbFromFloor = climbFromFloor };
        surfaces.Add(s);
        return s;
    }

    /// <summary>Can a body leaving the top of a at this upward speed land on b, steering at air speed?</summary>
    private static bool Reaches(Surface a, Surface b, float launch, float lift)
    {
        float apex = 0.95f * launch * launch / (2f * Gravity) + lift;
        float rise = b.top.y - a.top.y;
        if (rise > apex - 0.5f) return false;
        float gap = Mathf.Max(0f, new Vector2(b.top.x - a.top.x, b.top.z - a.top.z).magnitude - a.radius - b.radius);
        float air = launch / Gravity + Mathf.Sqrt(2f * (apex - rise) / FallGravity);
        return gap <= AirSpeed * air;
    }

    private static HashSet<Surface> Flood(bool carry)
    {
        var seen = new HashSet<Surface> { surfaces.Find(s => s.name == "Arrival Ledge") };
        // Repeat until nothing new: a nursery found late opens up solid tops reached earlier.
        for (bool grew = true; grew;)
        {
            grew = false;
            bool shroomInHand = carry && seen.Any(s => s.nursery);
            foreach (Surface a in seen.ToList())
                foreach (Surface b in surfaces)
                {
                    if (seen.Contains(b)) continue;
                    bool ok = Reaches(a, b, a.launch > 0f ? a.launch : JumpSpeed, 0f)
                        // A carried BounceShroom expanded on a solid top: a 1 m block that launches at 22.
                        || (shroomInHand && a.launch == 0f && a.radius >= 1.5f && Reaches(a, b, CarriedLaunch, 1f))
                        || (b.climbFromFloor && a.name == "Floor");
                    if (ok) grew |= seen.Add(b);
                }
        }
        return seen;
    }

    public static string Reachability()
    {
        HashSet<Surface> bare = Flood(false), carried = Flood(true);
        var report = new StringBuilder("Reachability from the Arrival Ledge (jump 12, bounce ~0.95 v²/2g, air speed " + AirSpeed + "):\n");
        var pockets = new SortedSet<string>();
        foreach (var s in surfaces) if (s.pocket != null) pockets.Add(s.pocket);
        foreach (string pocket in pockets)
        {
            Surface s = surfaces.Find(x => x.pocket == pocket);
            string how = bare.Contains(s) ? (s.climbFromFloor ? "by climbing" : "yes, without carrying")
                : carried.Contains(s) ? "only with a carried BounceShroom" : "UNREACHABLE";
            report.AppendLine("  " + pocket + ": " + how);
        }
        var missing = new List<string>();
        foreach (var s in surfaces)
            if (!carried.Contains(s) && s.name != "Floor") missing.Add(s.name);
        report.AppendLine("  standable tops never reached: " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
        return report.ToString();
    }

    // ---------------- Meshes ----------------

    /// <summary>Floor height: gentle swells, a sunken bog, rising toward the walls.</summary>
    public static float H(float x, float z)
    {
        float r = new Vector2(x, z).magnitude;
        float swell = 1.1f * (Mathf.Sin(x * 0.09f) + Mathf.Cos(z * 0.075f)) + 2f * (Mathf.PerlinNoise(x * 0.04f + 10f, z * 0.04f + 10f) - 0.5f);
        float bog = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 17f, (new Vector2(x, z) - Bog).magnitude));
        return swell - bog * 6f + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(50f, 66f, r)) * 6f;
    }

    private static Mesh FloorMesh()
    {
        const int cells = 72;
        const float size = 148f;
        var vertices = new Vector3[(cells + 1) * (cells + 1)];
        for (int i = 0; i <= cells; i++)
            for (int j = 0; j <= cells; j++)
            {
                float x = -size * 0.5f + size * i / cells, z = -size * 0.5f + size * j / cells;
                vertices[i * (cells + 1) + j] = new Vector3(x, H(x, z), z);
            }
        var triangles = new List<int>();
        for (int i = 0; i < cells; i++)
            for (int j = 0; j < cells; j++)
            {
                int a = i * (cells + 1) + j, b = a + 1, c = a + cells + 1, d = c + 1;
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        var mesh = new Mesh { name = "HollowFloor", indexFormat = IndexFormat.UInt32, vertices = vertices, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A unit cap: domed top, a little curled lip, and a shallow gilled underside. Origin at the rim.</summary>
    private static Mesh CapMesh()
    {
        Vector2[] profile = { new Vector2(0f, 0.45f), new Vector2(0.35f, 0.42f), new Vector2(0.6f, 0.35f), new Vector2(0.8f, 0.24f),
            new Vector2(0.93f, 0.12f), new Vector2(1f, 0.02f), new Vector2(1.03f, -0.06f), new Vector2(0.75f, -0.1f),
            new Vector2(0.4f, -0.13f), new Vector2(0.15f, -0.14f), new Vector2(0f, -0.14f) };
        const int sides = 18;
        var rings = new Vector3[profile.Length, sides];
        for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j < sides; j++)
            {
                float angle = j * Mathf.PI * 2f / sides;
                float wobble = 1f + 0.035f * Mathf.Sin(j * 2.7f + i * 1.3f);
                rings[i, j] = new Vector3(Mathf.Cos(angle) * profile[i].x * wobble, profile[i].y, Mathf.Sin(angle) * profile[i].x * wobble);
            }
        var flat = new List<Vector3>();
        for (int i = 0; i < profile.Length - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int k = (j + 1) % sides;
                flat.AddRange(new[] { rings[i, j], rings[i, k], rings[i + 1, j], rings[i, k], rings[i + 1, k], rings[i + 1, j] });
            }
        var indices = new int[flat.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        var mesh = new Mesh { name = "MushroomCap", vertices = flat.ToArray(), triangles = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Part(Transform root, string name, RealmMeshes mesh, Material material, string asset)
    {
        if (mesh.VertexCount == 0) return;
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().sharedMesh = SaveMesh(mesh.Build(name), asset);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------------- Helpers ----------------

    private static bool Clear(Vector2 at, float distance)
    {
        foreach (var other in keepClear) if ((other - at).magnitude < distance) return false;
        return true;
    }

    private static void Walkable(GameObject go)
    {
        go.layer = LayerMask.NameToLayer("Ground");
        go.tag = "Ground";
    }

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool walkable)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = size;
        if (walkable) Walkable(go);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static void Lamp(Vector3 position, float range, float intensity, Color color)
    {
        var light = Group(lights, "Glow").gameObject.AddComponent<Light>();
        light.transform.position = position;
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
    }

    /// <summary>Saves the mesh as an asset, replacing an older build's copy.</summary>
    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Generated + "/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Material SporeMaterial()
    {
        string path = Generated + "/Spore.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var original = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
        if (original == null) return null;
        material = new Material(original) { name = "Spore" };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    private static Material ReadableMaterial(string source, string name, Color color)
    {
        string path = Generated + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var original = AssetDatabase.LoadAssetAtPath<Material>(source);
        if (original == null) return null;
        material = new Material(original) { name = name };
        foreach (string property in new[] { "_DotChance", "_GridChance", "_GlitchAmount", "_RingCount", "_SwayAmount", "_BreathAmount", "_StrataFrequency" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        if (material.HasProperty("_HatchStrength")) material.SetFloat("_HatchStrength", 0.15f);
        if (material.HasProperty("_HatchScale")) material.SetFloat("_HatchScale", 3f);
        if (material.HasProperty("_LineWidth")) material.SetFloat("_LineWidth", 0.6f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_ShadowColor")) material.SetColor("_ShadowColor", color * 0.6f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
