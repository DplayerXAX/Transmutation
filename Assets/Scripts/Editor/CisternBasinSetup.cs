using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Authors Cistern Basin: a region built only from the water creatures. Erosive water sits low, ore rides high
/// on the colossi's backs, and the Cistern Snail is the only thing that carries one up to the other.
///
///   North rim   Spring: a source on the mountain slope runs down a carved gully into the Main Pool.
///   Floor       Main Pool plus two seep-fed side pools; snails live round them. Rain roams the basin.
///   Colossus A  "Pool Warden": rests twice beside the Main Pool. Teaches carrying a snail up the tail ramp.
///   Colossus B  "Rim Walker": rests on the dry east floor, far from any pool. A full snail ages on the way,
///               so A's Geode Larva is meant as a stillness relay halfway.
///   Colossus C  "Mesa Keeper": rests on the West Mesa, 9 m up a long ramp. Height plus distance.
///
/// The terrain is a basin ProceduralWorld baked to a TerrainSculptData asset and then sculpted from code
/// (pools, gully, mesa, ramp), so erosion works on all of it and it can be hand-edited afterwards in the
/// Sculpt And Place window.
/// </summary>
public static class CisternBasinSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_CisternBasin.unity";
    private const string FluidMaterials = "Assets/Materials/Fluid";
    private const string ColossusMaterials = "Assets/Materials/Creatures/OreColossus/MAT_OreColossus_";

    private static readonly Vector2 Spawn = new Vector2(0f, -58f);
    private static readonly Vector2 MainPool = new Vector2(0f, 24f), WestPool = new Vector2(-32f, 12f), EastPool = new Vector2(30f, 38f);
    private static readonly Vector2 MesaCentre = new Vector2(-58f, -28f), RampLow = new Vector2(-30f, -4f), RampHigh = new Vector2(-46f, -18f);
    private const float MainPoolRadius = 11f, SidePoolRadius = 7f, MesaRadius = 14f, MesaTop = 9f, RampRadius = 3.5f;
    private static readonly Vector2[] RouteA = { new Vector2(-17f, -2f), new Vector2(19f, 4f) };
    private static readonly Vector2[] RouteB = { new Vector2(58f, -14f), new Vector2(42f, -46f) };
    private static readonly Vector2[] RouteC = { new Vector2(-62f, -24f), new Vector2(-53f, -36f) };

    [MenuItem("Tools/Capstone/Cistern Basin/Build New Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building Cistern Basin.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException(ScenePath + " already exists. Edit it, or delete the scene (and its terrain asset) to rebuild it.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard changes in the open scenes first.");

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        var inner = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        var water = AssetDatabase.LoadAssetAtPath<Material>(FluidMaterials + "/MAT_ErosiveWater.mat");
        if (playerPrefab == null || surface == null || inner == null || water == null)
            throw new InvalidOperationException("Cistern Basin needs PlayerObjects.prefab, the world materials and the erosion materials (Tools > Capstone > Erosion creates them).");
        OreColossusSetup.CreateAssets();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // Saved once up front so the baked terrain asset is named after the scene.
        EditorSceneManager.SaveScene(scene, ScenePath);

        ProceduralWorld world = BuildWorld(surface, inner);
        var report = new StringBuilder();
        Sculpt(world, report);
        ErosiveFluidSystem fluid = BuildWater(world, out Vector3 springPoint, report);

        var root = new GameObject("Cistern Basin").transform;
        var notes = Group(root, "Layout Notes (empty markers, Scene view only)");
        Marker(notes, "Spring (north rim) — water runs down the gully into the Main Pool", springPoint);
        Marker(notes, "Main Pool", Ground(world, MainPool));
        Marker(notes, "West Pool (seep)", Ground(world, WestPool));
        Marker(notes, "East Pool (seep)", Ground(world, EastPool));
        Marker(notes, "West Mesa top (9 m)", Ground(world, MesaCentre));
        Marker(notes, "Mesa Ramp foot", Ground(world, RampLow));

        var colossi = Group(root, "Ore Colossi (each kneels at its own rest stops)");
        Colossus(colossi, world, "Colossus A — Pool Warden (tutorial: rests beside the Main Pool)", 101, RouteA, notes);
        Colossus(colossi, world, "Colossus B — Rim Walker (dry east floor: relay a snail with A's larva)", 202, RouteB, notes);
        Colossus(colossi, world, "Colossus C — Mesa Keeper (up on the West Mesa)", 303, RouteC, notes);

        var snails = Group(root, "Cistern Snails (round the pools)");
        Snail(snails, world, "Snail — Main Pool 1", MainPool + new Vector2(-MainPoolRadius - 2f, -3f));
        Snail(snails, world, "Snail — Main Pool 2", MainPool + new Vector2(MainPoolRadius + 2f, -5f));
        Snail(snails, world, "Snail — Main Pool 3", MainPool + new Vector2(4f, -MainPoolRadius + 3f));
        Snail(snails, world, "Snail — West Pool", WestPool + new Vector2(SidePoolRadius + 2f, 0f));
        Snail(snails, world, "Snail — East Pool", EastPool + new Vector2(-SidePoolRadius - 2f, -2f));

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        // PlayerObjects must stay a scene root: carry collision filtering uses transform.root.
        player.name = "PlayerObjects";
        Vector3 start = Ground(world, Spawn) + Vector3.up * 1.2f;
        player.transform.SetPositionAndRotation(start, Quaternion.identity);
        var hud = Group(root, "Interaction Hints").gameObject.AddComponent<InteractionPromptHUD>();
        var hudFields = new SerializedObject(hud);
        hudFields.FindProperty("showText").boolValue = true;
        hudFields.FindProperty("hintsVisible").boolValue = true;
        hudFields.FindProperty("carrier").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureCarrier>(true);
        hudFields.FindProperty("interactor").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureInteractor>(true);
        hudFields.ApplyModifiedPropertiesWithoutUndo();

        foreach (GameObject existing in scene.GetRootGameObjects())
        {
            if (existing.name == "Main Camera") { existing.name = "Original Scene Camera (inactive)"; existing.SetActive(false); }
            var light = existing.GetComponent<Light>();
            if (light != null && light.type == LightType.Directional)
            {
                existing.name = "Sun";
                light.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
                light.intensity = 1.2f;
                light.shadows = LightShadows.Soft;
                light.color = new Color(1f, 0.95f, 0.88f);
            }
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_QuietLines.mat");
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.55f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.7f, 0.72f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 260f;

        BuildDecor(world);
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, 0f, 0f), Quaternion.Euler(55f, 0f, 0f), 150f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Cistern Basin saved to " + ScenePath + ".\n" + report);
    }

    // ---------------- Terrain ----------------

    private static ProceduralWorld BuildWorld(Material surface, Material inner)
    {
        var go = new GameObject("Procedural World");
        var world = go.AddComponent<ProceduralWorld>();
        var f = new SerializedObject(world);
        f.FindProperty("worldSize").vector2Value = new Vector2(280f, 280f);
        f.FindProperty("seed").intValue = 23;
        // A nearly flat floor so the colossi can walk anywhere; the shape comes from the sculpting below.
        f.FindProperty("surfaceAmplitude").floatValue = 2.5f;
        f.FindProperty("ridges").floatValue = 0.2f;
        f.FindProperty("overhangAmplitude").floatValue = 0.6f;
        f.FindProperty("spawnFlatRadius").floatValue = 10f;
        f.FindProperty("spawnBlend").floatValue = 20f;
        f.FindProperty("basin").boolValue = true;
        f.FindProperty("basinFloor").floatValue = 0.62f;
        f.FindProperty("basinSlope").floatValue = 0.3f;
        f.FindProperty("rimHeight").floatValue = 34f;
        // Water stands in the pools for the whole session; a thin crust lets it drill through to the inner world.
        f.FindProperty("crustThickness").floatValue = 24f;
        f.FindProperty("densityBudgetPerSecond").floatValue = 2f;
        f.FindProperty("caveWidth").floatValue = 0f;
        f.FindProperty("sinkholeCount").intValue = 0;
        f.FindProperty("firstSinkholeDistance").floatValue = 0f;
        f.FindProperty("surfaceMaterial").objectReferenceValue = surface;
        f.FindProperty("innerMaterial").objectReferenceValue = inner;
        f.FindProperty("blockingRadius").floatValue = 90f;
        f.ApplyModifiedPropertiesWithoutUndo();
        // Freeze the layout into a sculpt asset (also builds the terrain in edit mode so it can be sculpted).
        TerrainSculptSetup.Bake(world);
        if (!world.CanSculpt) throw new InvalidOperationException("Cistern Basin: the baked terrain cannot be sculpted.");
        return world;
    }

    private static void Sculpt(ProceduralWorld world, StringBuilder report)
    {
        world.BeginSculptStroke();
        try
        {
            // West Mesa: raise a block, then flatten its top to MesaTop in repeated passes.
            for (float x = -MesaRadius; x <= MesaRadius; x += 4f)
                for (float z = -MesaRadius; z <= MesaRadius; z += 4f)
                {
                    if (new Vector2(x, z).magnitude > MesaRadius) continue;
                    Vector2 at = MesaCentre + new Vector2(x, z);
                    float h = Height(world, at);
                    world.Sculpt(TerrainSculptTool.Raise, new Vector3(at.x, (h + MesaTop) * 0.5f, at.y), 8f, MesaTop - h + 1f, 0f);
                }
            for (int pass = 0; pass < 3; pass++)
                for (float x = -MesaRadius; x <= MesaRadius; x += 3f)
                    for (float z = -MesaRadius; z <= MesaRadius; z += 3f)
                        if (new Vector2(x, z).magnitude <= MesaRadius - 1f)
                            world.Sculpt(TerrainSculptTool.Flatten, new Vector3(MesaCentre.x + x, MesaTop, MesaCentre.y + z), 8f, 1f, MesaTop);

            // Ramp from the floor up the mesa's north-east flank.
            float baseHeight = Height(world, RampLow);
            float length = Vector2.Distance(RampLow, RampHigh);
            for (int pass = 0; pass < 2; pass++)
                for (float d = 0f; d <= length + 0.01f; d += 1.5f)
                {
                    Vector2 at = Vector2.Lerp(RampLow, RampHigh, d / length);
                    float target = Mathf.Lerp(baseHeight, MesaTop, d / length);
                    float h = Height(world, at);
                    if (pass == 0 && target > h + 0.5f)
                        world.Sculpt(TerrainSculptTool.Raise, new Vector3(at.x, (h + target) * 0.5f, at.y), RampRadius + 1.5f, target - h, 0f);
                    else world.Sculpt(TerrainSculptTool.Flatten, new Vector3(at.x, target, at.y), RampRadius, 1f, target);
                }

            // Pools: bowls lowered into the floor.
            Bowl(world, MainPool, MainPoolRadius, 3.5f);
            Bowl(world, WestPool, SidePoolRadius, 2.5f);
            Bowl(world, EastPool, SidePoolRadius, 2.5f);

            // Gully from the spring down the north rim into the Main Pool.
            Vector2 spring = SpringColumn(world);
            Vector2 mouth = MainPool + new Vector2(0f, MainPoolRadius * 0.6f);
            float run = Vector2.Distance(spring, mouth);
            for (float d = 0f; d <= run; d += 2f)
            {
                Vector2 at = Vector2.Lerp(spring, mouth, d / run);
                world.Sculpt(TerrainSculptTool.Lower, new Vector3(at.x, Height(world, at), at.y), 3.2f, 1.6f, 0f);
            }
            report.AppendLine("Spring column " + spring + ", gully " + run.ToString("0") + " m to the Main Pool.");
        }
        finally { world.EndSculptStroke(); }
        EditorUtility.SetDirty(world.SculptData);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        report.AppendLine("Sculpted samples: " + world.SculptedSampleCount + ". Mesa top measured at " +
                          Height(world, MesaCentre).ToString("0.0") + " m, ramp middle at " +
                          Height(world, Vector2.Lerp(RampLow, RampHigh, 0.5f)).ToString("0.0") + " m, Main Pool floor at " +
                          Height(world, MainPool).ToString("0.0") + " m.");
    }

    private static void Bowl(ProceduralWorld world, Vector2 centre, float radius, float depth)
    {
        world.Sculpt(TerrainSculptTool.Lower, new Vector3(centre.x, Height(world, centre), centre.y), radius, depth, 0f);
        world.Sculpt(TerrainSculptTool.Smooth, new Vector3(centre.x, Height(world, centre), centre.y), radius + 2f, 0.6f, 0f);
    }

    /// <summary>First column north of the Main Pool that is 12 m up the rim.</summary>
    private static Vector2 SpringColumn(ProceduralWorld world)
    {
        for (float z = MainPool.y + 30f; z < 135f; z += 1f)
            if (Height(world, new Vector2(0f, z)) > 12f) return new Vector2(0f, z);
        return new Vector2(0f, 95f);
    }

    private static float Height(ProceduralWorld world, Vector2 at)
    {
        Vector3 top = new Vector3(at.x, 200f, at.y);
        if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 400f, 1 << 7, QueryTriggerInteraction.Ignore)) return hit.point.y;
        world.ColumnHeights(new Vector3(at.x, 0f, at.y), out float surface, out _, out _);
        return surface;
    }

    private static Vector3 Ground(ProceduralWorld world, Vector2 at) => new Vector3(at.x, Height(world, at), at.y);

    // ---------------- Water ----------------

    private static ErosiveFluidSystem BuildWater(ProceduralWorld world, out Vector3 springPoint, StringBuilder report)
    {
        var fluid = new GameObject("Erosive Fluid").AddComponent<ErosiveFluidSystem>();
        ErosionSceneSetup.Set(fluid, "world", world);
        ErosionSceneSetup.Set(fluid, "dropletMaterial", AssetDatabase.LoadAssetAtPath<Material>(FluidMaterials + "/MAT_ErosiveWater.mat"));
        ErosionSceneSetup.Set(fluid, "debrisMaterial", AssetDatabase.LoadAssetAtPath<Material>(FluidMaterials + "/MAT_ErosionDebris.mat"));
        ErosionSceneSetup.Set(fluid, "rippleMaterial", AssetDatabase.LoadAssetAtPath<Material>(FluidMaterials + "/MAT_ErosionRipple.mat"));
        ErosionSceneSetup.Set(fluid, "visualDistance", 90f);
        var controls = fluid.gameObject.AddComponent<ErosionDemoControls>();
        ErosionSceneSetup.Set(controls, "fluid", fluid);

        Vector2 spring = SpringColumn(world);
        springPoint = Ground(world, spring) + Vector3.up * 1.2f;
        // Gentle water: it still carves the gully over minutes, but pooled water barely wears the pool floors.
        Source(fluid, "Spring — north rim (runs down the gully)", springPoint, 8f, 40f, new Vector3(0f, 0f, -1.5f), 0.12f);
        Source(fluid, "Seep — West Pool", Ground(world, WestPool) + Vector3.up * 1.5f, 2f, 45f, Vector3.zero, 0.05f);
        Source(fluid, "Seep — East Pool", Ground(world, EastPool) + Vector3.up * 1.5f, 2f, 45f, Vector3.zero, 0.05f);
        report.AppendLine("Spring at " + springPoint.ToString("0.0") + ".");
        return fluid;
    }

    private static void Source(ErosiveFluidSystem fluid, string name, Vector3 at, float rate, float lifetime, Vector3 velocity, float erosion)
    {
        var source = new GameObject(name).AddComponent<ErosiveFluidSource>();
        source.transform.SetParent(fluid.transform);
        source.transform.position = at;
        ErosionSceneSetup.Set(source, "fluid", fluid);
        ErosionSceneSetup.Set(source, "emitting", true);
        ErosionSceneSetup.Set(source, "dropletsPerSecond", rate);
        ErosionSceneSetup.Set(source, "lifetime", lifetime);
        ErosionSceneSetup.Set(source, "initialVelocity", velocity);
        ErosionSceneSetup.Set(source, "erosionRate", erosion);
        ErosionSceneSetup.Set(source, "stationaryMultiplier", 0.05f);
    }

    // ---------------- Creatures ----------------

    private static void Colossus(Transform parent, ProceduralWorld world, string name, int seed, Vector2[] stops, Transform notes)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Vector3 first = Ground(world, stops[0]);
        go.transform.position = first;
        go.AddComponent<Rigidbody>().isKinematic = true;
        var colossus = go.AddComponent<OreColossus>();
        var f = new SerializedObject(colossus);
        f.FindProperty("world").objectReferenceValue = world;
        f.FindProperty("placeInWorld").boolValue = true;
        f.FindProperty("seed").intValue = seed;
        f.FindProperty("rockMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Rock.mat");
        f.FindProperty("oreMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Ore.mat");
        f.FindProperty("heartMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Heart.mat");
        f.FindProperty("geodeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Geode.mat");
        f.FindProperty("larvaPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GeodeLarva>(OreColossusSetup.LarvaPath);
        SerializedProperty route = f.FindProperty("route");
        route.arraySize = stops.Length;
        for (int i = 0; i < stops.Length; i++)
        {
            Vector3 stop = Ground(world, stops[i]);
            route.GetArrayElementAtIndex(i).vector3Value = stop;
            Marker(notes, name.Split('—')[0].Trim() + " rest stop " + (i + 1), stop);
        }
        f.FindProperty("maxHealth").floatValue = 1000f;
        f.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        f.FindProperty("age").floatValue = 100000f;
        f.FindProperty("ageRate").floatValue = 0f;
        f.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Snail(Transform parent, ProceduralWorld world, string name, Vector2 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = Ground(world, at) + Vector3.up * 0.6f;
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = 3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        var collider = go.AddComponent<SphereCollider>();
        collider.radius = 0.45f;
        collider.center = Vector3.up * 0.45f;
        var snail = go.AddComponent<CisternSnail>();
        var f = new SerializedObject(snail);
        f.FindProperty("maxHealth").floatValue = 60f;
        f.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        f.FindProperty("age").floatValue = 900f;
        f.FindProperty("ageRate").floatValue = 0.5f;
        f.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------- Decor ----------------

    private const string DecorRoot = "Cistern Basin Decor";
    private const string DecorFolder = "Assets/Prefabs/WorldDecor/Decor_";
    private static System.Random decorRandom;
    private static Material oreGlow;

    /// <summary>Clears and re-places the decor in the open Cistern Basin scene (terrain is built in edit mode first).</summary>
    [MenuItem("Tools/Capstone/Cistern Basin/Rebuild Decor (Open Scene)")]
    public static void RebuildDecor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before rebuilding decor.");
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null || SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open " + ScenePath + " first.");
        // The edit-mode chunks are not saved and vanish on a domain reload, even while the layout is still ready.
        if (world.transform.childCount == 0) TerrainSculptWindow.ShowTerrain(world);
        if (world.transform.childCount == 0) throw new InvalidOperationException("Cistern Basin: the terrain did not build in edit mode.");
        BuildDecor(world);
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
    }

    /// <summary>
    /// Hand-placed decor from the shared WorldDecor prefabs, by theme: shell beds and ore crust round the pools,
    /// stones along the gully with an arch over the spring, slag round each colossus rest stop, weathered relics on
    /// the dry east floor, cairns up the mesa ramp and obelisks on its rim, a boulder belt at the foot of the
    /// mountains and a light scatter over the floor. Pools, the gully, the colossus routes and the ramp stay clear.
    /// </summary>
    private static void BuildDecor(ProceduralWorld world)
    {
        foreach (GameObject existing in world.gameObject.scene.GetRootGameObjects())
            if (existing.name == DecorRoot) Object.DestroyImmediate(existing);
        decorRandom = new System.Random(5);
        oreGlow = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Ore.mat");
        var root = new GameObject(DecorRoot).transform;
        int count = 0;

        // Pool shores: snail shell beds and teal ore crust left by the water. Small, no colliders, so snails pass.
        var shores = Group(root, "Pool Shores — shell beds and ore crust");
        foreach (var (centre, radius) in new[] { (MainPool, MainPoolRadius), (WestPool, SidePoolRadius), (EastPool, SidePoolRadius) })
        {
            int pieces = Mathf.RoundToInt(radius * 2.2f);
            for (int i = 0; i < pieces; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                Vector2 at = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(radius, radius + 5f);
                if (InGully(world, at, 3.5f)) continue;
                bool ore = decorRandom.NextDouble() < 0.35;
                count += Place(world, shores, ore ? "Crystals" : "ShellCarpets", at, ore ? Range(0.5f, 0.95f) : Range(0.45f, 0.8f), false, ore ? oreGlow : null) ? 1 : 0;
            }
            Glow(shores, Ground(world, centre) + Vector3.up * 2.5f, radius * 2.2f, 1.2f);
        }

        // Gully banks: stones and eroded fins on both sides; an arch stands over the water just below the spring.
        var gully = Group(root, "Gully — bank stones, Spring Arch");
        Vector2 spring = SpringColumn(world), mouth = MainPool + new Vector2(0f, MainPoolRadius);
        float run = Vector2.Distance(spring, mouth);
        Vector2 along = (mouth - spring).normalized, side = new Vector2(along.y, -along.x);
        for (float d = 4f; d < run; d += Range(3f, 6f))
            foreach (float s in new[] { -1f, 1f })
            {
                Vector2 at = spring + along * d + side * s * Range(5.5f, 9f);
                bool fin = decorRandom.NextDouble() < 0.3;
                count += Place(world, gully, fin ? "Shards" : "Pebbles", at, fin ? Range(1f, 1.7f) : Range(0.9f, 1.6f), true) ? 1 : 0;
            }
        count += Place(world, gully, "Arches", spring + along * 6f, 1.7f, true, null, Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg, 0.4f) ? 1 : 0;

        // Colossus rest stops: chips of ore shaken off the backs while they knelt. Tiny, no colliders.
        var slag = Group(root, "Rest Stops — ore slag");
        foreach (Vector2[] route in new[] { RouteA, RouteB, RouteC })
            foreach (Vector2 stop in route)
                for (int i = 0; i < 7; i++)
                {
                    float angle = Range(0f, Mathf.PI * 2f);
                    count += Place(world, slag, "Crystals", stop + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(9f, 13f),
                        Range(0.3f, 0.55f), false, oreGlow) ? 1 : 0;
                }

        // Dry east floor: weathered arches, ring stones and totems: what the water left when it went.
        var east = Group(root, "Dry East Floor — weathered relics");
        string[] relics = { "Arches", "Halos", "Totems", "Shards" };
        for (int i = 0, tries = 0; i < 18 && tries < 400; tries++)
        {
            Vector2 at = new Vector2(Range(25f, 70f), Range(-70f, 10f));
            if (at.magnitude > 72f || !Clear(world, at, 6f)) continue;
            if (Place(world, east, relics[decorRandom.Next(relics.Length)], at, Range(1.2f, 2.2f), true)) { i++; count++; }
        }

        // West Mesa: cairns up both edges of the ramp, obelisks on the rim, ore veins at the cliff foot.
        var mesa = Group(root, "West Mesa — ramp cairns, rim obelisks, ore veins");
        Vector2 rampDir = (RampHigh - RampLow).normalized, rampSide = new Vector2(rampDir.y, -rampDir.x);
        float rampLength = Vector2.Distance(RampLow, RampHigh);
        for (float d = 2f; d < rampLength; d += 5f)
            foreach (float s in new[] { -1f, 1f })
                count += Place(world, mesa, "Cairns", RampLow + rampDir * d + rampSide * s * (RampRadius + 1.8f), Range(0.5f, 0.7f), true) ? 1 : 0;
        // Off to the sides of Colossus C's walk (its stops lie across the middle of the top).
        foreach (float a in new[] { 20f, 55f, 200f, 235f })
        {
            Vector2 at = MesaCentre + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * (MesaRadius - 1.5f);
            if (!NearRoute(at, 6f)) count += Place(world, mesa, "Obelisks", at, Range(1f, 1.3f), true) ? 1 : 0;
        }
        for (int i = 0; i < 10; i++)
        {
            float angle = Range(-40f, 120f) * Mathf.Deg2Rad;
            Vector2 at = MesaCentre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(MesaRadius + 4f, MesaRadius + 8f);
            if (Clear(world, at, 2f)) count += Place(world, mesa, "Crystals", at, Range(0.5f, 0.9f), true, oreGlow) ? 1 : 0;
        }

        // Boulder belt where the floor meets the mountains, thinned near the spring.
        var rim = Group(root, "Rim Foot — boulder belt");
        for (int i = 0; i < 120; i++)
        {
            float angle = Range(0f, Mathf.PI * 2f);
            Vector2 at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(66f, 92f);
            if (Vector2.Distance(at, spring) < 14f || !Clear(world, at, 0f)) continue;
            bool fin = decorRandom.NextDouble() < 0.35;
            count += Place(world, rim, fin ? "Shards" : "Pebbles", at, fin ? Range(1.6f, 3f) : Range(2f, 3.8f), true, null, Range(0f, 360f), 0.6f) ? 1 : 0;
        }

        // Erosion spires: tight stands of tall fins and totems, landmarks you can steer by from anywhere in the bowl.
        var spires = Group(root, "Erosion Spires — landmark stands");
        Vector2[] stands = { new Vector2(-40f, -52f), new Vector2(45f, 52f), new Vector2(-48f, 40f), new Vector2(28f, -62f), new Vector2(66f, 18f) };
        foreach (Vector2 stand in stands)
            for (int i = 0, tries = 0; i < 6 && tries < 40; tries++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                Vector2 at = stand + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(0f, 7f);
                if (!Clear(world, at, 1f)) continue;
                bool tall = i < 2;
                string kind = tall ? "Obelisks" : (decorRandom.NextDouble() < 0.6 ? "Shards" : "Totems");
                if (Place(world, spires, kind, at, tall ? Range(2.2f, 3.2f) : Range(1.4f, 2.4f), true, null, Range(0f, 360f), 0.1f)) { i++; count++; }
            }

        // Light scatter over the open floor, and a pair of totems framing the way in.
        var floor = Group(root, "Floor Scatter");
        string[] scatter = { "Pebbles", "Pebbles", "Cairns", "Shards" };
        for (int i = 0, tries = 0; i < 70 && tries < 900; tries++)
        {
            Vector2 at = new Vector2(Range(-68f, 68f), Range(-68f, 68f));
            if (at.magnitude > 66f || !Clear(world, at, 5f)) continue;
            string kind = scatter[decorRandom.Next(scatter.Length)];
            if (Place(world, floor, kind, at, kind == "Pebbles" ? Range(0.8f, 1.6f) : Range(0.7f, 1.3f), true)) { i++; count++; }
        }
        count += Place(world, floor, "Totems", Spawn + new Vector2(-4.5f, 6f), 0.8f, true) ? 1 : 0;
        count += Place(world, floor, "Totems", Spawn + new Vector2(4.5f, 6f), 0.8f, true) ? 1 : 0;

        count += BuildOreDecor(world, root, spring, along, side, run);
        Debug.Log("Cistern Basin decor: " + count + " pieces.");
    }

    /// <summary>
    /// The ore the colossi feed on, showing through the basin: teal veins breaking out of the mountain foot, purple
    /// geode pockets cracked open on the floor (a bright heart chip in each), boulders with ore tufts, seams the
    /// gully water has exposed along its banks, and a few tall glowing ore pillars to steer by.
    /// </summary>
    private static int BuildOreDecor(ProceduralWorld world, Transform root, Vector2 spring, Vector2 along, Vector2 side, float run)
    {
        Material geode = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Geode.mat");
        Material heart = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Heart.mat");
        Material rock = AssetDatabase.LoadAssetAtPath<Material>(ColossusMaterials + "Rock.mat");
        int count = 0;
        // Landmarks already standing (the erosion spires) count as taken, so new features keep their distance.
        var taken = new List<Vector2> { new Vector2(-40f, -52f), new Vector2(45f, 52f), new Vector2(-48f, 40f), new Vector2(28f, -62f), new Vector2(66f, 18f) };
        // Pillars first: they need the most open floor.
        count += BuildOrePillars(world, root, taken);

        // Rim veins: clusters of big teal crystals breaking out of the mountain foot, leaning out of the slope.
        var veins = Group(root, "Ore Veins — rim outcrops");
        for (int v = 0, tries = 0; v < 14 && tries < 200; tries++)
        {
            float angle = Range(0f, Mathf.PI * 2f);
            Vector2 centre = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(72f, 92f);
            if (Vector2.Distance(centre, spring) < 16f || !Clear(world, centre, 2f)) continue;
            bool near = false;
            foreach (Vector2 t in taken) if (Vector2.Distance(t, centre) < 16f) { near = true; break; }
            if (near) continue;
            taken.Add(centre);
            v++;
            int pieces = decorRandom.Next(5, 9);
            for (int i = 0; i < pieces; i++)
            {
                Vector2 at = centre + new Vector2(Range(-4f, 4f), Range(-4f, 4f));
                float scale = i == 0 ? Range(1.9f, 2.6f) : Range(0.7f, 1.5f);
                count += Place(world, veins, "Crystals", at, scale, true, oreGlow, Range(0f, 360f), 0.75f, 0.3f) ? 1 : 0;
            }
        }

        // Geode pockets: a ring of dark rock shell, cracked open, purple crystals inside and one heart chip.
        var geodes = Group(root, "Geode Pockets — purple crystal hollows");
        for (int g = 0; g < 7; g++)
        {
            if (!FreeSpot(world, taken, 22f, 4f, 64f, out Vector2 centre) || !TerrainHit(world, centre, out RaycastHit floorHit)) continue;
            float shell = Range(2.6f, 3.4f);
            int stones = decorRandom.Next(6, 9);
            float gap = Range(0f, Mathf.PI * 2f);   // one side left open, the crack you look in through
            for (int i = 0; i < stones; i++)
            {
                float a = gap + 0.7f + i * (Mathf.PI * 2f - 1.4f) / (stones - 1);
                Vector2 at = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * shell;
                if (!TerrainHit(world, at, out RaycastHit stoneHit) || stoneHit.normal.y < 0.45f) continue;
                // Shell fins lean outward, a cracked-open rind round the purple core.
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Quaternion lean = Quaternion.AngleAxis(Range(22f, 38f), Vector3.Cross(Vector3.up, outward)) * Quaternion.Euler(0f, Range(0f, 360f), 0f);
                float scale = Range(0.45f, 0.65f);
                count += SpawnAt(geodes, "Shards", stoneHit.point + Vector3.down * 0.1f * scale, lean, scale, true, null) != null ? 1 : 0;
            }
            // Dark rubble rind on the floor of the hollow.
            count += Place(world, geodes, "Pebbles", centre, Range(0.7f, 0.9f), false, rock) ? 1 : 0;
            for (int i = 0; i < 6; i++)
            {
                Vector2 at = centre + new Vector2(Range(-1.8f, 1.8f), Range(-1.8f, 1.8f));
                count += Place(world, geodes, "Crystals", at, Range(0.4f, 0.75f), false, geode, Range(0f, 360f), 0.5f, 0.4f) ? 1 : 0;
            }
            count += SpawnAt(geodes, "Crystals", floorHit.point + Vector3.down * 0.1f, Lean(10f), 0.38f, false, heart) != null ? 1 : 0;
            Glow(geodes, floorHit.point + Vector3.up * 1.8f, 9f, 1.4f, new Color(0.7f, 0.45f, 1f), "Geode Glow");
        }

        // Ore patches: a spill of rubble with teal tufts pushing up through it, scattered over the open floor.
        var patches = Group(root, "Ore Patches — rubble with ore tufts");
        for (int b = 0, tries = 0; b < 26 && tries < 600; tries++)
        {
            Vector2 at = new Vector2(Range(-66f, 66f), Range(-66f, 66f));
            if (at.magnitude > 68f || !Clear(world, at, 3f)) continue;
            if (!Place(world, patches, "Pebbles", at, Range(0.9f, 1.4f), true)) continue;
            b++; count++;
            int tufts = decorRandom.Next(2, 5);
            for (int i = 0; i < tufts; i++)
                count += Place(world, patches, "Crystals", at + new Vector2(Range(-1.8f, 1.8f), Range(-1.8f, 1.8f)),
                    Range(0.25f, 0.5f), false, oreGlow, Range(0f, 360f), 0.6f) ? 1 : 0;
        }

        // Gully seams: the spring water has worn the banks back to the ore underneath. Small, no colliders.
        var seams = Group(root, "Gully Seams — ore the water exposed");
        for (float d = 6f; d < run - 2f; d += Range(2.5f, 4.5f))
        {
            float s = decorRandom.NextDouble() < 0.5 ? -1f : 1f;
            Vector2 at = spring + along * d + side * s * Range(4f, 5.5f);
            count += Place(world, seams, "Crystals", at, Range(0.3f, 0.55f), false, oreGlow, Range(0f, 360f), 0.8f, 0.35f) ? 1 : 0;
        }

        return count;
    }

    /// <summary>A few tall glowing obelisks of solid ore, ringed with chips, visible across the bowl.</summary>
    private static int BuildOrePillars(ProceduralWorld world, Transform root, List<Vector2> taken)
    {
        int count = 0;
        var pillars = Group(root, "Ore Pillars — glowing landmarks");
        for (int p = 0; p < 4; p++)
        {
            if (!FreeSpot(world, taken, 24f, 4f, 66f, out Vector2 centre)) continue;
            GameObject pillar = PlaceGo(world, pillars, "Obelisks", centre, Range(1.3f, 1.8f), true, oreGlow, Range(0f, 360f), 0.1f, 0.6f);
            if (pillar == null) continue;
            count++;
            // Two shorter shafts leaning off the main one, so it reads as a crystal cluster, not a post.
            for (int i = 0; i < 2; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 foot = pillar.transform.position + outward * 1.2f;
                Quaternion lean = Quaternion.AngleAxis(Range(14f, 26f), Vector3.Cross(Vector3.up, outward)) * Quaternion.Euler(0f, Range(0f, 360f), 0f);
                count += SpawnAt(pillars, "Obelisks", foot + Vector3.down * 0.3f, lean, Range(0.7f, 1f), true, oreGlow) != null ? 1 : 0;
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                count += Place(world, pillars, "Crystals", centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(2f, 4.5f),
                    Range(0.35f, 0.7f), false, oreGlow, Range(0f, 360f), 0.5f) ? 1 : 0;
            }
            Glow(pillars, pillar.transform.position + Vector3.up * 6f, 14f, 1.6f, new Color(0.4f, 1f, 0.9f), "Pillar Glow");
        }
        return count;
    }

    /// <summary>One decor prefab on the terrain. Returns false when there is no ground or it is too steep.</summary>
    private static bool Place(ProceduralWorld world, Transform parent, string kind, Vector2 at, float scale, bool collider,
        Material material = null, float yaw = float.NaN, float alignToNormal = 0.3f, float minUp = 0.55f) =>
        PlaceGo(world, parent, kind, at, scale, collider, material, yaw, alignToNormal, minUp) != null;

    private static GameObject PlaceGo(ProceduralWorld world, Transform parent, string kind, Vector2 at, float scale, bool collider,
        Material material = null, float yaw = float.NaN, float alignToNormal = 0.3f, float minUp = 0.55f)
    {
        if (!TerrainHit(world, at, out RaycastHit hit) || hit.normal.y < minUp) return null;
        Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, hit.normal), alignToNormal);
        return SpawnAt(parent, kind, hit.point - hit.normal * 0.12f * scale,
            tilt * Quaternion.Euler(0f, float.IsNaN(yaw) ? Range(0f, 360f) : yaw, 0f), scale, collider, material);
    }

    /// <summary>One decor prefab at an exact pose (for pieces set on other pieces rather than on the ground).</summary>
    private static GameObject SpawnAt(Transform parent, string kind, Vector3 position, Quaternion rotation, float scale, bool collider, Material material)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecorFolder + kind + "_" + decorRandom.Next(6) + ".prefab");
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * scale;
        if (!collider) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        if (material != null) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = material;
        return go;
    }

    /// <summary>A random tilt of up to maxDegrees off vertical, then a random spin.</summary>
    private static Quaternion Lean(float maxDegrees) =>
        Quaternion.Euler(0f, Range(0f, 360f), 0f) * Quaternion.Euler(Range(0f, maxDegrees), 0f, 0f) * Quaternion.Euler(0f, Range(0f, 360f), 0f);

    /// <summary>A random open-floor point at least spacing from every point already taken (and then takes it).</summary>
    private static bool FreeSpot(ProceduralWorld world, List<Vector2> taken, float spacing, float margin, float maxRadius, out Vector2 at)
    {
        for (int tries = 0; tries < 300; tries++)
        {
            at = new Vector2(Range(-maxRadius, maxRadius), Range(-maxRadius, maxRadius));
            if (at.magnitude > maxRadius || !Clear(world, at, margin)) continue;
            bool near = false;
            foreach (Vector2 t in taken) if (Vector2.Distance(t, at) < spacing) { near = true; break; }
            if (near) continue;
            taken.Add(at);
            return true;
        }
        at = default;
        return false;
    }

    private static void Glow(Transform parent, Vector3 at, float range, float intensity, Color color, string name)
    {
        Glow(parent, at, range, intensity);
        Light light = parent.GetChild(parent.childCount - 1).GetComponent<Light>();
        light.color = color;
        light.name = name;
    }

    /// <summary>Highest terrain chunk under a column (ignores decor, which shares the Ground layer).</summary>
    private static bool TerrainHit(ProceduralWorld world, Vector2 at, out RaycastHit best)
    {
        best = default;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(at.x, 200f, at.y), Vector3.down, 400f, 1 << 7, QueryTriggerInteraction.Ignore))
            if (hit.collider.transform.IsChildOf(world.transform) && (!found || hit.point.y > best.point.y)) { best = hit; found = true; }
        return found;
    }

    /// <summary>Clear of the pools, the gully, the colossus routes, the ramp and the spawn, by margin metres.</summary>
    private static bool Clear(ProceduralWorld world, Vector2 at, float margin)
    {
        if (Vector2.Distance(at, MainPool) < MainPoolRadius + 2f + margin) return false;
        if (Vector2.Distance(at, WestPool) < SidePoolRadius + 2f + margin) return false;
        if (Vector2.Distance(at, EastPool) < SidePoolRadius + 2f + margin) return false;
        if (Vector2.Distance(at, Spawn) < 6f + margin) return false;
        if (Vector2.Distance(at, MesaCentre) < MesaRadius + 2f) return false;
        if (SegmentDistance(at, RampLow, RampHigh) < RampRadius + 1f + margin) return false;
        return !InGully(world, at, 4f + margin) && !NearRoute(at, 10f + margin);
    }

    private static bool InGully(ProceduralWorld world, Vector2 at, float margin) =>
        SegmentDistance(at, SpringColumn(world), MainPool) < margin;

    private static bool NearRoute(Vector2 at, float margin)
    {
        foreach (Vector2[] route in new[] { RouteA, RouteB, RouteC })
            for (int i = 0; i < route.Length; i++)
                if (SegmentDistance(at, route[i], route[(i + 1) % route.Length]) < margin) return true;
        return false;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    private static void Glow(Transform parent, Vector3 at, float range, float intensity)
    {
        var light = new GameObject("Pool Glow").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.position = at;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(0.4f, 1f, 0.95f);
        light.shadows = LightShadows.None;
    }

    private static float Range(float min, float max) => Mathf.Lerp(min, max, (float)decorRandom.NextDouble());

    // ---------------- Helpers ----------------

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void Marker(Transform parent, string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
    }
}
