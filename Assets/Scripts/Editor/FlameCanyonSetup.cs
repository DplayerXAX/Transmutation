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
/// Authors Flame Canyon: a region built only from the heat creatures that already exist. A box canyon is cut
/// 24 m into a plateau; the walls are taller than a fire bubble rises (20 m), so everything the fire flowers
/// bloom stays inside and the canyon is one closed heat loop.
///
///   South   Cold Reach: where the player starts. Fuzz and closed fire flowers, and no fire source. Nothing
///           here wakes up until heat is carried in (a fire source can be carried and shoots fire bubbles).
///   Centre  Hearth: the main fire source in an ember pit. Its heat reaches about 17 m, the whole chamber;
///           fire flowers stand at 3, 6, 10 and 14 m (the nearer, the faster they bloom), fire eaters work
///           the fallen bubbles and fuzz warm red at the edge.
///   West    Fuzz Den: a dead-end side canyon off the Hearth. Cold; fuzz and closed flowers.
///   Bend    Ember Gate: a second, smaller fire garden at the north-east bend.
///   North   Bone Hollow: bone children loop round a charred nest that releases fire bubbles, with a
///           tentacle creature to rein them (built in play mode by FlameCanyonLife).
///
/// The canyon floor and walls are drawn with a fire-coloured copy of the inner-world material, its rings
/// centred on the Hearth. The world object sits 24 m up so the canyon floor is at y = 0, the same height fire bubbles are tuned for.
/// The terrain is baked to a TerrainSculptData asset and cut from code in terraces (wide at the rim, narrow at
/// the floor), so it can be hand-edited afterwards in the Sculpt And Place window.
/// </summary>
public static class FlameCanyonSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_FlameCanyon.unity";
    private const string TemplateScene = "Assets/Scenes/Scene_Marcus.unity";
    private const string DecorRoot = "Flame Canyon Decor";
    private const string DecorFolder = "Assets/Prefabs/WorldDecor/Decor_";
    private const string InnerMaterialPath = "Assets/Materials/World/MAT_FlameCanyon_Inner.mat";

    /// <summary>Height of the plateau above the canyon floor (the floor is at y = 0).</summary>
    private const float Depth = 24f;
    /// <summary>World heights of the terraces, rim first. Steps stay under the narrowest brush radius.</summary>
    private static readonly float[] Levels = { 22f, 18f, 14f, 10f, 6f, 2f, 0f };
    /// <summary>Each terrace above the floor is this much wider than the one below it.</summary>
    private const float Terrace = 1.6f;
    /// <summary>Share of a brush radius that ends up flat (the rest is the wall).</summary>
    private const float FlatShare = 0.65f;
    private const float CorridorRadius = 10f;

    private static readonly Vector2[] MainPath =
    {
        new Vector2(-6f, -70f), new Vector2(-26f, -42f), new Vector2(8f, -6f),
        new Vector2(34f, 30f), new Vector2(6f, 54f), new Vector2(-8f, 68f),
    };
    private static readonly Vector2[] DenPath = { new Vector2(8f, -6f), new Vector2(-16f, 6f), new Vector2(-34f, 14f) };

    private static readonly Vector2 ColdReach = new Vector2(-6f, -70f), Hearth = new Vector2(8f, -6f), EmberGate = new Vector2(34f, 30f),
        BoneHollow = new Vector2(-8f, 68f), FuzzDen = new Vector2(-34f, 14f);
    private const float ColdReachRadius = 16f, HearthRadius = 26f, EmberGateRadius = 15f, BoneHollowRadius = 21f, FuzzDenRadius = 14f;

    private struct Sample { public Vector2 at, dir; }

    [MenuItem("Tools/Capstone/Flame Canyon/Build New Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building Flame Canyon.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException(ScenePath + " already exists. Edit it, or delete the scene (and its terrain asset) to rebuild it.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard changes in the open scenes first.");

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        var inner = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/FireSource.prefab");
        var flowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Fire Flower.prefab");
        var bone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat");
        if (playerPrefab == null || surface == null || inner == null || sourcePrefab == null || flowerPrefab == null || bone == null)
            throw new InvalidOperationException("Flame Canyon needs PlayerObjects.prefab, the world materials, FireSource.prefab, Fire Flower.prefab and the bone material.");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        // Saved once up front so the baked terrain asset is named after the scene.
        EditorSceneManager.SaveScene(scene, ScenePath);

        ProceduralWorld world = BuildWorld(surface, FireRock(inner));
        var report = new StringBuilder();
        List<Sample> main = Samples(MainPath, 3f), den = Samples(DenPath, 3f);
        Sculpt(world, main, den, report);

        var root = new GameObject("Flame Canyon").transform;
        var notes = Group(root, "Layout Notes (empty markers, Scene view only)");
        Marker(notes, "Cold Reach (south end, spawn) — no fire source", Ground(ColdReach));
        Marker(notes, "Hearth (centre) — main fire source", Ground(Hearth));
        Marker(notes, "Fuzz Den (west side canyon) — cold dead end", Ground(FuzzDen));
        Marker(notes, "Ember Gate (north-east bend) — second fire garden", Ground(EmberGate));
        Marker(notes, "Bone Hollow (north end) — bone children and nest", Ground(BoneHollow));

        // Only fire flowers and bubbles that appear during play are capped; everything placed here is kept.
        var population = root.gameObject.AddComponent<RealmPopulation>();
        var caps = new SerializedObject(population);
        caps.FindProperty("maxNewFireFlowers").intValue = 14;
        caps.FindProperty("maxNewFireBubbles").intValue = 24;
        caps.ApplyModifiedPropertiesWithoutUndo();

        BuildLife(scene, root, world, sourcePrefab, flowerPrefab, bone, report);

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        // PlayerObjects must stay a scene root: carry collision filtering uses transform.root.
        player.name = "PlayerObjects";
        Vector2 spawn = main[2].at;
        player.transform.SetPositionAndRotation(Ground(spawn) + Vector3.up * 1.2f,
            Quaternion.LookRotation(new Vector3(main[2].dir.x, 0f, main[2].dir.y)));
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
                // Low and warm, shining down the length of the canyon so one wall is lit and the other in shadow.
                existing.name = "Sun";
                light.transform.rotation = Quaternion.Euler(48f, 20f, 0f);
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                light.color = new Color(1f, 0.72f, 0.48f);
            }
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_QuietLines.mat");
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.36f, 0.3f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.34f, 0.2f);
        RenderSettings.fogStartDistance = 35f;
        RenderSettings.fogEndDistance = 230f;

        BuildDecor(world, main, den);
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, 0f, 0f), Quaternion.Euler(55f, 0f, 0f), 150f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Flame Canyon saved to " + ScenePath + ".\n" + report);
    }

    // ---------------- Terrain ----------------

    private static ProceduralWorld BuildWorld(Material surface, Material inner)
    {
        var go = new GameObject("Procedural World");
        // Raised so the canyon floor, not the plateau, is at y = 0: fire bubbles stop rising at a fixed world height.
        go.transform.position = Vector3.up * Depth;
        var world = go.AddComponent<ProceduralWorld>();
        var f = new SerializedObject(world);
        f.FindProperty("worldSize").vector2Value = new Vector2(280f, 280f);
        f.FindProperty("seed").intValue = 41;
        // A nearly flat plateau; the shape comes from the sculpting below.
        f.FindProperty("surfaceAmplitude").floatValue = 3f;
        f.FindProperty("ridges").floatValue = 0.35f;
        f.FindProperty("overhangAmplitude").floatValue = 0.8f;
        f.FindProperty("spawnFlatRadius").floatValue = 10f;
        f.FindProperty("spawnBlend").floatValue = 20f;
        f.FindProperty("basin").boolValue = true;
        f.FindProperty("basinFloor").floatValue = 0.66f;
        f.FindProperty("basinSlope").floatValue = 0.3f;
        f.FindProperty("rimHeight").floatValue = 30f;
        // The crust has to be thicker than the canyon is deep, or the floor would open onto the inner world.
        f.FindProperty("crustThickness").floatValue = 40f;
        f.FindProperty("caveWidth").floatValue = 0f;
        f.FindProperty("sinkholeCount").intValue = 0;
        f.FindProperty("firstSinkholeDistance").floatValue = 0f;
        f.FindProperty("surfaceMaterial").objectReferenceValue = surface;
        f.FindProperty("innerMaterial").objectReferenceValue = inner;
        // The whole canyon (about 155 m from the spawn to the far wall) is built before the first frame:
        // the creatures placed in it have bodies and would fall through ground that is not there yet.
        f.FindProperty("blockingRadius").floatValue = 165f;
        f.ApplyModifiedPropertiesWithoutUndo();
        // Freeze the layout into a sculpt asset (also builds the terrain in edit mode so it can be sculpted).
        TerrainSculptSetup.Bake(world);
        if (!world.CanSculpt) throw new InvalidOperationException("Flame Canyon: the baked terrain cannot be sculpted.");
        return world;
    }

    /// <summary>
    /// The rock below the plateau's skin (the canyon walls and floor) uses the world's inner material. This is a
    /// copy of it in fire colours, with its ring pattern centred on the Hearth.
    /// </summary>
    private static Material FireRock(Material inner)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(InnerMaterialPath);
        if (material == null)
        {
            material = new Material(inner);
            AssetDatabase.CreateAsset(material, InnerMaterialPath);
        }
        else
        {
            material.shader = inner.shader;
            material.CopyPropertiesFromMaterial(inner);
        }
        material.SetColor("_Color", new Color(0.15f, 0.055f, 0.03f));
        material.SetColor("_ShadowColor", new Color(0.02f, 0f, 0f));
        material.SetColor("_LineColor", new Color(1f, 0.46f, 0.1f));
        material.SetColor("_LineColor2", new Color(1f, 0.82f, 0.3f));
        material.SetColor("_HatchColor", new Color(0.32f, 0.1f, 0.04f));
        material.SetColor("_HazeColor", new Color(0.05f, 0.012f, 0f, 0.97f));
        material.SetVector("_Center", new Vector4(Hearth.x, 0f, Hearth.y, 0f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Sculpt(ProceduralWorld world, List<Sample> main, List<Sample> den, StringBuilder report)
    {
        // Every dab of the cut: the two paths, plus a wide one at each chamber.
        var dabs = new List<(Vector2 at, float radius)>();
        foreach (Sample s in main) dabs.Add((s.at, CorridorRadius));
        foreach (Sample s in den) dabs.Add((s.at, CorridorRadius - 1.5f));
        dabs.Add((ColdReach, ColdReachRadius));
        dabs.Add((Hearth, HearthRadius));
        dabs.Add((EmberGate, EmberGateRadius));
        dabs.Add((BoneHollow, BoneHollowRadius));
        dabs.Add((FuzzDen, FuzzDenRadius));

        world.BeginSculptStroke();
        try
        {
            // One terrace at a time, rim first. Flatten also fills in below its height, so a level must be finished
            // along the whole canyon before the next, narrower one is cut into it.
            for (int level = 0; level < Levels.Length; level++)
            {
                EditorUtility.DisplayProgressBar("Flame Canyon", "Cutting terrace " + (level + 1) + " of " + Levels.Length, level / (float)Levels.Length);
                float wider = (Levels.Length - 1 - level) * Terrace;
                foreach (var (at, radius) in dabs)
                    world.Sculpt(TerrainSculptTool.Flatten, new Vector3(at.x, Levels[level], at.y), radius + wider, 3f, Levels[level]);
            }
            // Ember pits: shallow bowls the fire sources sit in.
            Bowl(world, Hearth, 4.5f, 1.3f);
            Bowl(world, EmberGate, 3.5f, 1f);
        }
        finally
        {
            world.EndSculptStroke();
            EditorUtility.ClearProgressBar();
        }
        EditorUtility.SetDirty(world.SculptData);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        report.AppendLine("Sculpted samples: " + world.SculptedSampleCount + ". Floor measured at Cold Reach " + Height(ColdReach).ToString("0.0") +
                          " m, Hearth " + Height(Hearth).ToString("0.0") + " m, Bone Hollow " + Height(BoneHollow).ToString("0.0") +
                          " m; plateau at " + Height(new Vector2(-60f, 0f)).ToString("0.0") + " m.");
    }

    private static void Bowl(ProceduralWorld world, Vector2 centre, float radius, float depth)
    {
        world.Sculpt(TerrainSculptTool.Lower, new Vector3(centre.x, Height(centre), centre.y), radius, depth, 0f);
        world.Sculpt(TerrainSculptTool.Smooth, new Vector3(centre.x, Height(centre), centre.y), radius + 2f, 0.6f, 0f);
    }

    /// <summary>Evenly spaced points along a smooth curve through the given points.</summary>
    private static List<Sample> Samples(Vector2[] points, float spacing)
    {
        var fine = new List<Vector2>();
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(i + 2, points.Length - 1)];
            for (int k = 0; k < 40; k++)
            {
                float t = k / 40f, t2 = t * t, t3 = t2 * t;
                fine.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
            }
        }
        fine.Add(points[points.Length - 1]);
        var samples = new List<Sample>();
        float carried = spacing;
        for (int i = 1; i < fine.Count; i++)
        {
            Vector2 step = fine[i] - fine[i - 1];
            carried += step.magnitude;
            if (carried < spacing) continue;
            carried = 0f;
            samples.Add(new Sample { at = fine[i], dir = step.normalized });
        }
        return samples;
    }

    private static float Height(Vector2 at)
    {
        Vector3 top = new Vector3(at.x, 200f, at.y);
        return Physics.Raycast(top, Vector3.down, out RaycastHit hit, 400f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
    }

    private static Vector3 Ground(Vector2 at) => new Vector3(at.x, Height(at), at.y);

    // ---------------- Creatures ----------------

    private static void BuildLife(Scene scene, Transform root, ProceduralWorld world, GameObject sourcePrefab, GameObject flowerPrefab,
        Material bone, StringBuilder report)
    {
        // Fire eater, fuzz and tentacle creature have no prefabs: copy the ones authored in an existing scene.
        GameObject eater = null, fuzz = null, tentacle = null;
        Scene from = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Additive);
        try
        {
            foreach (GameObject sceneRoot in from.GetRootGameObjects())
            {
                foreach (FireEater candidate in sceneRoot.GetComponentsInChildren<FireEater>(true))
                    if (eater == null && candidate.GetComponentInChildren<FireSource>(true) == null) eater = Copy(candidate.gameObject, scene);
                if (fuzz == null) { var found = sceneRoot.GetComponentInChildren<FuzzCreature>(true); if (found != null) fuzz = Copy(found.gameObject, scene); }
                if (tentacle == null) { var found = sceneRoot.GetComponentInChildren<TentacleCreature>(true); if (found != null) tentacle = Copy(found.gameObject, scene); }
            }
        }
        finally { EditorSceneManager.CloseScene(from, true); }
        SceneManager.SetActiveScene(scene);
        if (eater == null || fuzz == null || tentacle == null)
            report.AppendLine("Missing in " + TemplateScene + ": " + (eater == null ? "FireEater " : "") + (fuzz == null ? "FuzzCreature " : "") +
                              (tentacle == null ? "TentacleCreature" : "") + " (left out).");

        // Hearth: the main fire. The prefab radiates 25 heat a second, less 1.5 for every metre, so it reaches
        // about 17 m and a flower (80 heat to bloom) blooms faster the nearer it stands.
        var hearth = Group(root, "Hearth — main fire source, flowers at 3 / 6 / 10 / 14 m, fire eaters, fuzz");
        Source(hearth, sourcePrefab, "Hearth Fire Source (20 minutes of heat)", Hearth, 30000f);
        Flower(hearth, flowerPrefab, "Hearth Fire Flower 1 (3 m: blooms about every 4 s)", Hearth + Polar(20f, 3f));
        Flower(hearth, flowerPrefab, "Hearth Fire Flower 2 (6 m: about every 5 s)", Hearth + Polar(140f, 6f));
        Flower(hearth, flowerPrefab, "Hearth Fire Flower 3 (10 m: about every 8 s)", Hearth + Polar(255f, 10f));
        Flower(hearth, flowerPrefab, "Hearth Fire Flower 4 (14 m: about every 20 s)", Hearth + Polar(80f, 14f));
        Clone(hearth, eater, "Hearth Fire Eater 1", Hearth + Polar(200f, 9f), 0.2f);
        Clone(hearth, eater, "Hearth Fire Eater 2", Hearth + Polar(330f, 11f), 0.2f);
        Clone(hearth, fuzz, "Hearth Fuzz 1 (12 m from the fire: warms red)", Hearth + Polar(300f, 12f), 0.7f);
        Clone(hearth, fuzz, "Hearth Fuzz 2 (15 m: barely warm)", Hearth + Polar(170f, 15f), 0.7f);
        Glow(hearth, Ground(Hearth) + Vector3.up * 3f, 26f, 2.4f, "Hearth Glow");

        // Ember Gate: a smaller garden at the bend, so fire is in sight again on the way north. It burns out first.
        var gate = Group(root, "Ember Gate — second fire garden");
        Source(gate, sourcePrefab, "Ember Gate Fire Source (8 minutes of heat)", EmberGate, 12000f);
        Flower(gate, flowerPrefab, "Ember Gate Fire Flower 1 (3 m)", EmberGate + Polar(170f, 3f));
        Flower(gate, flowerPrefab, "Ember Gate Fire Flower 2 (6 m)", EmberGate + Polar(290f, 6f));
        Clone(gate, eater, "Ember Gate Fire Eater", EmberGate + Polar(120f, 7f), 0.2f);
        Glow(gate, Ground(EmberGate) + Vector3.up * 3f, 20f, 2f, "Ember Gate Glow");

        // Cold Reach: the same creatures with no heat (the Hearth is 60 m away). Nothing blooms; the fuzz stay black.
        var cold = Group(root, "Cold Reach — closed flowers and cold fuzz (no fire source)");
        Vector2 reach = Vector2.Lerp(MainPath[0], MainPath[1], 0.55f);
        Flower(cold, flowerPrefab, "Cold Fire Flower 1", reach + new Vector2(-2.5f, 1f));
        Flower(cold, flowerPrefab, "Cold Fire Flower 2", reach + new Vector2(2f, 3.5f));
        Flower(cold, flowerPrefab, "Cold Fire Flower 3", reach + new Vector2(0.5f, -3f));
        Clone(cold, fuzz, "Cold Fuzz 1", ColdReach + new Vector2(4f, 2f), 0.7f);
        Clone(cold, fuzz, "Cold Fuzz 2", ColdReach + new Vector2(5f, -2f), 0.7f);
        Clone(cold, fuzz, "Cold Fuzz 3", ColdReach + new Vector2(2f, 5f), 0.7f);

        // Fuzz Den: the dead end west of the Hearth.
        var denGroup = Group(root, "Fuzz Den — cold side canyon");
        for (int i = 0; i < 4; i++) Clone(denGroup, fuzz, "Den Fuzz " + (i + 1), FuzzDen + Polar(i * 90f + 30f, 3f), 0.7f);
        Flower(denGroup, flowerPrefab, "Den Fire Flower (closed)", FuzzDen + Polar(200f, 5.5f));

        // Bone Hollow: the children and their nest are built in play mode; the tentacle creature is their reins.
        var hollow = Group(root, "Bone Hollow — bone children and bubble nest (appear in play mode)");
        Vector3 centre = Ground(BoneHollow);
        var life = hollow.gameObject.AddComponent<FlameCanyonLife>();
        life.Setup(world, bone, centre, Ground(BoneHollow + new Vector2(2f, -3f)));
        EditorUtility.SetDirty(life);
        Clone(hollow, tentacle, "Bone Hollow Tentacle Creature (reins for a bone child)", BoneHollow + new Vector2(6f, -11f), 1.5f);
        Glow(hollow, centre + Vector3.up * 4f, 22f, 1.2f, "Bone Hollow Glow");

        foreach (GameObject template in new[] { eater, fuzz, tentacle }) if (template != null) Object.DestroyImmediate(template);
    }

    private static GameObject Copy(GameObject original, Scene into)
    {
        GameObject copy = Object.Instantiate(original);
        copy.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(copy, into);
        copy.SetActive(false);
        return copy;
    }

    private static void Clone(Transform parent, GameObject template, string name, Vector2 at, float lift)
    {
        if (template == null) return;
        GameObject go = Object.Instantiate(template, Ground(at) + Vector3.up * lift, Quaternion.identity, parent);
        go.name = name;
        go.SetActive(true);
    }

    /// <summary>A fire source holding the given heat (the prefab's 4000 lasts under three minutes).</summary>
    private static void Source(Transform parent, GameObject prefab, string name, Vector2 at, float heat)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = Ground(at) + Vector3.up * 0.6f;
        var f = new SerializedObject(go.GetComponent<FireSource>());
        f.FindProperty("heat").floatValue = heat;
        f.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Flower(Transform parent, GameObject prefab, string name, Vector2 at)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = Ground(at);
    }

    private static Vector2 Polar(float degrees, float radius) =>
        new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

    // ---------------- Decor ----------------

    private static System.Random decorRandom;

    /// <summary>Clears and re-places the decor in the open Flame Canyon scene (terrain is built in edit mode first).</summary>
    [MenuItem("Tools/Capstone/Flame Canyon/Rebuild Decor (Open Scene)")]
    public static void RebuildDecor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before rebuilding decor.");
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null || SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open " + ScenePath + " first.");
        // The edit-mode chunks are not saved and vanish on a domain reload, even while the layout is still ready.
        if (world.transform.childCount == 0) TerrainSculptWindow.ShowTerrain(world);
        if (world.transform.childCount == 0) throw new InvalidOperationException("Flame Canyon: the terrain did not build in edit mode.");
        BuildDecor(world, Samples(MainPath, 3f), Samples(DenPath, 3f));
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
    }

    /// <summary>
    /// Decor from the shared WorldDecor prefabs: red ember crystals round each fire, talus at the foot of the
    /// walls, stone teeth along the rim, an arch at each end of the Hearth, cairns marking the way north and a
    /// thin scatter over the plateau. The middle of the floor and the bone children's loop stay clear.
    /// </summary>
    private static void BuildDecor(ProceduralWorld world, List<Sample> main, List<Sample> den)
    {
        foreach (GameObject existing in world.gameObject.scene.GetRootGameObjects())
            if (existing.name == DecorRoot) Object.DestroyImmediate(existing);
        decorRandom = new System.Random(11);
        Material ember = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Red.mat");
        var root = new GameObject(DecorRoot).transform;
        int count = 0;

        // Ember crystals: red shards in the scorched ground round each fire. Small, no colliders.
        var embers = Group(root, "Ember Crystals — round the fires");
        foreach (var (centre, pieces, reach) in new[] { (Hearth, 22, 9f), (EmberGate, 14, 7f) })
            for (int i = 0; i < pieces; i++)
                count += Place(world, embers, "Crystals", centre + Polar(Range(0f, 360f), Range(2.5f, reach)), Range(0.3f, 0.7f), false, true, ember) ? 1 : 0;

        // Talus: fallen rock along the foot of both walls, both paths.
        var talus = Group(root, "Wall Foot — talus");
        foreach (List<Sample> path in new[] { main, den })
            foreach (Sample s in path)
                foreach (float side in new[] { -1f, 1f })
                {
                    if (decorRandom.NextDouble() < 0.45) continue;
                    Vector2 at = s.at + Across(s) * side * (CorridorRadius * FlatShare + Range(-1.2f, 0.4f));
                    if (InChamber(at, -2f)) continue;
                    bool fin = decorRandom.NextDouble() < 0.4;
                    count += Place(world, talus, fin ? "Shards" : "Pebbles", at, fin ? Range(1f, 1.9f) : Range(1.1f, 2.2f), true, true) ? 1 : 0;
                }
        // The chambers' walls get the same, round their edges.
        foreach (var (centre, radius) in Chambers())
            for (int i = 0; i < Mathf.RoundToInt(radius * 1.1f); i++)
            {
                Vector2 at = centre + Polar(Range(0f, 360f), radius * FlatShare + Range(-1.5f, 0.5f));
                if (NearPath(at, main, 6f) || NearPath(at, den, 5f)) continue;
                bool fin = decorRandom.NextDouble() < 0.4;
                count += Place(world, talus, fin ? "Shards" : "Pebbles", at, fin ? Range(1.2f, 2.2f) : Range(1.2f, 2.4f), true, true) ? 1 : 0;
            }

        // Rim teeth: tall stones leaning over the edge of the plateau, so the canyon reads from above and below.
        var rim = Group(root, "Rim — stone teeth");
        float rimOffset = CorridorRadius + (Levels.Length - 1) * Terrace + 2f;
        foreach (List<Sample> path in new[] { main, den })
            for (int i = 0; i < path.Count; i += 2)
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 at = path[i].at + Across(path[i]) * side * (rimOffset + Range(0f, 5f));
                    if (InChamber(at, (Levels.Length - 1) * Terrace + 3f)) continue;
                    bool tall = decorRandom.NextDouble() < 0.35;
                    count += Place(world, rim, tall ? "Obelisks" : "Shards", at, tall ? Range(1.6f, 2.6f) : Range(1.6f, 3f), true, false) ? 1 : 0;
                }

        // Arches where the canyon opens into the Hearth and where it leaves it, and cairns on the way north.
        var way = Group(root, "Waymarks — Hearth arches, cairns, spawn totems");
        foreach (Sample s in main)
        {
            float d = Vector2.Distance(s.at, Hearth);
            if (d > HearthRadius * FlatShare + 4f && d < HearthRadius * FlatShare + 7.2f)
                count += Place(world, way, "Arches", s.at, 3.2f, true, true, null, Mathf.Atan2(s.dir.x, s.dir.y) * Mathf.Rad2Deg, 0.1f) ? 1 : 0;
        }
        for (int i = 6; i < main.Count - 4; i += 6)
        {
            Vector2 at = main[i].at + Across(main[i]) * (CorridorRadius * FlatShare - 2.2f);
            if (!InChamber(at, 0f)) count += Place(world, way, "Cairns", at, Range(0.6f, 0.85f), true, true) ? 1 : 0;
        }
        count += Place(world, way, "Totems", main[4].at + Across(main[4]) * 4.5f, 0.9f, true, true) ? 1 : 0;
        count += Place(world, way, "Totems", main[4].at - Across(main[4]) * 4.5f, 0.9f, true, true) ? 1 : 0;

        // Plateau: a thin scatter, so the top is not bare when you look up or climb out.
        var plateau = Group(root, "Plateau Scatter");
        string[] scatter = { "Pebbles", "Pebbles", "Shards", "Cairns" };
        for (int i = 0, tries = 0; i < 70 && tries < 700; tries++)
        {
            Vector2 at = new Vector2(Range(-84f, 84f), Range(-84f, 84f));
            if (at.magnitude > 86f) continue;
            string kind = scatter[decorRandom.Next(scatter.Length)];
            if (Place(world, plateau, kind, at, Range(0.9f, 1.9f), true, false)) { i++; count++; }
        }

        Debug.Log("Flame Canyon decor: " + count + " pieces.");
    }

    private static IEnumerable<(Vector2 centre, float radius)> Chambers()
    {
        yield return (ColdReach, ColdReachRadius);
        yield return (Hearth, HearthRadius);
        yield return (EmberGate, EmberGateRadius);
        yield return (BoneHollow, BoneHollowRadius);
        yield return (FuzzDen, FuzzDenRadius);
    }

    /// <summary>Inside a chamber's floor, grown by margin metres.</summary>
    private static bool InChamber(Vector2 at, float margin)
    {
        foreach (var (centre, radius) in Chambers())
            if (Vector2.Distance(at, centre) < radius * FlatShare + margin) return true;
        return false;
    }

    private static bool NearPath(Vector2 at, List<Sample> path, float margin)
    {
        foreach (Sample s in path) if (Vector2.Distance(at, s.at) < margin) return true;
        return false;
    }

    private static Vector2 Across(Sample s) => new Vector2(s.dir.y, -s.dir.x);

    /// <summary>
    /// One decor prefab on the terrain, on the canyon floor (onFloor) or up on the plateau.
    /// Returns false when the ground there is the wrong one, or too steep.
    /// </summary>
    private static bool Place(ProceduralWorld world, Transform parent, string kind, Vector2 at, float scale, bool collider, bool onFloor,
        Material material = null, float yaw = float.NaN, float alignToNormal = 0.3f)
    {
        if (!TerrainHit(world, at, out RaycastHit hit) || hit.normal.y < 0.55f) return false;
        if (onFloor ? hit.point.y > 3f : hit.point.y < Depth - 5f) return false;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecorFolder + kind + "_" + decorRandom.Next(6) + ".prefab");
        if (prefab == null) return false;
        Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, hit.normal), alignToNormal);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(hit.point - hit.normal * 0.12f * scale, tilt * Quaternion.Euler(0f, float.IsNaN(yaw) ? Range(0f, 360f) : yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        if (!collider) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        if (material != null) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = material;
        return true;
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

    private static void Glow(Transform parent, Vector3 at, float range, float intensity, string name)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.position = at;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = new Color(1f, 0.45f, 0.15f);
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
