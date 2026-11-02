using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Author the user's sketch with the existing density terrain, creature prefabs and landmark builders.</summary>
public static class YoYoTutorialFallSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_YoYoTutorialFall.unity";
    private const string TerrainPath = "Assets/Terrain/Sculpted/Scene_YoYoTutorialFall_Terrain.asset";
    private const string Folder = "Assets/Generated/YoYoTutorialFall";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static ProceduralWorld world;
    private static Transform actors;

    [MenuItem("Tools/Capstone/YoYo Tutorial Fall/1 Create New Procedural Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null || AssetDatabase.LoadAssetAtPath<TerrainSculptData>(TerrainPath) != null)
            throw new InvalidOperationException("YoYo Tutorial Fall already exists; open and sculpt it rather than replacing it.");
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty) EditorSceneManager.SaveScene(previous);
        GameObject oldLight = previous.GetRootGameObjects().FirstOrDefault(g => g.GetComponent<Light>() != null);
        GameObject oldAudio = previous.GetRootGameObjects().FirstOrDefault(g => g.name == "WwiseGlobal");
        Material sky = RenderSettings.skybox;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        if (oldLight != null) Object.Instantiate(oldLight).name = "Directional Light";
        else
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }
        if (oldAudio != null) Object.Instantiate(oldAudio).name = "WwiseGlobal";
        EditorSceneManager.CloseScene(previous, true);
        RenderSettings.skybox = Mat("Assets/Materials/World/MAT_Sky_FractalCalm.mat") ?? sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.55f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 180f;
        RenderSettings.fogEndDistance = 650f;
        RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.71f);
        EnsureFolder(Folder);

        var terrainObject = new GameObject("Procedural World — sketch terrain");
        terrainObject.transform.position = new Vector3(0f, 0f, 10f);
        terrainObject.layer = LayerMask.NameToLayer("Ground"); terrainObject.tag = "Ground";
        world = terrainObject.AddComponent<ProceduralWorld>();
        var settings = new SerializedObject(world);
        settings.FindProperty("worldSize").vector2Value = new Vector2(372f, 348f);
        F(settings, "cellSize", 1.5f); I(settings, "chunkCells", 24); I(settings, "seed", 27);
        F(settings, "surfaceAmplitude", 12f); F(settings, "surfaceFrequency", 0.014f); F(settings, "ridges", 0.45f);
        F(settings, "overhangAmplitude", 1.8f); F(settings, "spawnFlatRadius", 15f); F(settings, "spawnBlend", 25f);
        F(settings, "rimHeight", 46f); B(settings, "basin", true); F(settings, "basinFloor", 0.76f); F(settings, "basinSlope", 0.19f);
        F(settings, "crustThickness", 10f); F(settings, "innerGap", 24f); F(settings, "ceilingMirror", 0.65f); F(settings, "innerInvert", 0.7f);
        F(settings, "innerDetail", 2f); F(settings, "caveWidth", 0f); I(settings, "sinkholeCount", 0); F(settings, "firstSinkholeDistance", 0f);
        F(settings, "blockingRadius", 600f); B(settings, "placePlayerOnSurface", false);
        settings.FindProperty("surfaceMaterial").objectReferenceValue = Mat("Assets/Materials/World/MAT_World_Surface.mat");
        settings.FindProperty("innerMaterial").objectReferenceValue = Mat("Assets/Materials/World/MAT_World_Inner.mat");
        settings.ApplyModifiedPropertiesWithoutUndo();
        string layout = world.CaptureLayout(out _);
        // The only shaft in the sketch belongs to the large dragon-bone landmark.
        layout = Regex.Replace(layout, "\"sinkholes\":\\[.*?\\]", "\"sinkholes\":[{\"position\":{\"x\":98.0,\"y\":-14.0},\"radius\":7.0}]");
        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(layout, Array.Empty<float>(), "User's abstract sketch / existing ProceduralWorld / EncounterFlow.v2");
        AssetDatabase.CreateAsset(data, TerrainPath);
        settings.Update(); settings.FindProperty("sculptData").objectReferenceValue = data; settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        world.Generate(true);
    }

    [MenuItem("Tools/Capstone/YoYo Tutorial Fall/2 Shape Terrain From Sketch")]
    public static object Shape()
    {
        world = FindWorld();
        if (world.SculptData.Edits.Count != 0) throw new InvalidOperationException("This terrain is already sculpted; use the existing sculpt brushes.");
        if (!world.LayoutReady) world.Generate(true);
        var field = typeof(ProceduralWorld).GetField("field", Private).GetValue(world);
        var height = (Func<float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float>), field, "SurfaceHeight");
        var ceiling = (Func<float,float>)Delegate.CreateDelegate(typeof(Func<float,float>), field, "CeilingHeight");
        var floor = (Func<float,float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float,float>), field, "InnerGroundHeight");
        var originalDensity = (Func<Vector3,float,float,float,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float,float,float,float>), field, "Density");
        Vector3 minimum = world.GridMinimum;
        float cell = world.CellSize;
        Vector3Int count = Vector3Int.RoundToInt(world.VolumeBounds.size / cell);
        int edits = 0;
        for (int z = 0; z <= count.z; z++) for (int x = 0; x <= count.x; x++)
        {
            Vector3 column = minimum + new Vector3(x * cell, 0f, z * cell);
            if (Mathf.Abs(column.x) > 185f || column.z < -164f || column.z > 180f) continue;
            Vector3 local = column - world.transform.position;
            float oldH = height(local.x, local.z), oldC = ceiling(oldH), oldF = floor(local.x, local.z, oldH);
            float h = SketchHeight(column.x, column.z);
            float c = -10f - h * 0.65f;
            float f = -34f - h * 0.7f + Mathf.PerlinNoise(column.x * 0.04f + 91f, column.z * 0.04f + 33f) - 0.5f;
            for (int y = 0; y <= count.y; y++)
            {
                var key = new Vector3Int(x,y,z);
                Vector3 point = minimum + (Vector3)key * cell;
                if (point.y > 80f) continue;
                float original = originalDensity(point - world.transform.position,oldH,oldC,oldF);
                float density = Mathf.Max(Mathf.Min(h - point.y,point.y - c),f - point.y);
                if (column.x < -63f && column.z < -18f) density = IntroCave(point,density);
                if (column.x > -107f && column.x < -43f && column.z > 41f && column.z < 105f)
                    density = Mathf.Min(density,TentacleCave(point));
                if (column.x > -143f && column.x < -87f && column.z > -1f && column.z < 49f)
                    density = Mathf.Min(density,YoYoTutorialFallFlow.SpiderGrotto(point));
                if (Mathf.Abs(column.x - 98f) < 12f && Mathf.Abs(column.z + 4f) < 18f && point.y > f + 2f)
                {
                    float dx = point.x - 98f, dz = point.z + 4f + point.y * 0.07f;
                    float shaft = Mathf.Sqrt(dx * dx + dz * dz) - 7f * (1f + 0.18f * Mathf.Sin(point.y * 0.35f + 98f));
                    density = Mathf.Min(density,shaft);
                }
                if ((original > 5f && density > 5f) || (original < -5f && density < -5f)) continue;
                if (Mathf.Abs(density - original) < 0.002f) continue;
                world.SculptData.SetEdit(key,density - original); edits++;
            }
        }
        EditorUtility.SetDirty(world.SculptData);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        world.Generate(true);
        EditorSceneManager.SaveScene(world.gameObject.scene);
        return new { edits, ready = world.GenerationComplete, terrain = TerrainPath };
    }

    // Heights are seeded from the drawn regions, then frozen into the ordinary sculpt asset.
    public static float SketchHeight(float x, float z) => YoYoTutorialFallFlow.Height(x,z);

    public static float LegacySketchHeight(float x, float z)
    {
        float h = 2f + (Mathf.PerlinNoise(x * 0.032f + 17f,z * 0.032f + 43f) - 0.5f) * 2.2f;
        float oval = new Vector2(x / 174f,(z - 9f) / 153f).magnitude;
        float foot = 0.81f + (Mathf.PerlinNoise(x * 0.025f + 51f,z * 0.025f + 8f) - 0.5f) * 0.055f;
        h += Mathf.SmoothStep(0f,1f,(oval - foot) / 0.17f) * 43f;
        h = Plateau(h,x,z,-112f,21f,34f,25f,13f,0.27f);
        h = Plateau(h,x,z,-51f,5f,28f,20f,11f,0.23f);
        h = Plateau(h,x,z,-74f,74f,28f,29f,13f,0.3f);
        h += 28f * Mathf.SmoothStep(0f,1f,(1.11f - Oval(x,z,8f,26f,41f,47f)) / 0.28f);
        h += 13f * Mathf.SmoothStep(0f,1f,(1.05f - Oval(x,z,-45f,-19f,42f,7f)) / 0.48f);
        h += Hill(x,z,-145f,60f,14f,26f,10f);
        h = PathHeight(h,x,z,MainRoute,9f,5f);
        h = PathHeight(h,x,z,SpiderRoute,4.5f,4f);
        h = PathHeight(h,x,z,UpperRoute,4.5f,4f);
        h = PathHeight(h,x,z,FlowerRoute,4.2f,3f);
        h = Clearing(h,x,z,-104f,-48f,17f,2f);
        h = Clearing(h,x,z,-58f,-46f,19f,3f);
        h = Clearing(h,x,z,98f,-5f,39f,3f);
        h = Clearing(h,x,z,77f,66f,28f,4f);
        h = Clearing(h,x,z,111f,99f,29f,7f);
        return h;
    }

    private static readonly Vector3[] MainRoute =
    {
        new Vector3(-112f,2f,-52f),new Vector3(-86f,2f,-46f),new Vector3(-58f,3f,-46f),new Vector3(-13f,2f,-48f),
        new Vector3(35f,2f,-44f),new Vector3(78f,3f,-29f),new Vector3(102f,3f,-5f),new Vector3(99f,3f,34f),new Vector3(98f,4f,72f),
    };
    private static readonly Vector3[] SpiderRoute =
    {
        new Vector3(-121f,2f,-45f),new Vector3(-131f,4f,-22f),new Vector3(-128f,11f,5f),new Vector3(-113f,13f,21f),
    };
    private static readonly Vector3[] FlowerRoute =
    {
        new Vector3(-112f,13f,21f),new Vector3(-91f,12f,20f),new Vector3(-75f,11f,9f),new Vector3(-52f,11f,5f),new Vector3(-37f,3f,-36f),
    };
    private static readonly Vector3[] UpperRoute =
    {
        new Vector3(-110f,13f,30f),new Vector3(-89f,13f,47f),new Vector3(-66f,13f,65f),new Vector3(-34f,14f,83f),
        new Vector3(12f,13f,91f),new Vector3(48f,8f,87f),new Vector3(78f,4f,79f),
    };

    private static float IntroCave(Vector3 point,float original)
        => IntroCave(point,original,true);

    private static float IntroCave(Vector3 point,float original,bool revised)
        => IntroCave(point,original,revised,true);

    private static float IntroCave(Vector3 point,float original,bool revised,bool continuousExit)
    {
        Vector3 p = point - new Vector3(-112f,0f,-136f);
        float enclosure = Mathf.Max(-Box(p,new Vector3(-5f,-22f,20f),new Vector3(26f,21f,35f),3f),
            -Box(p,new Vector3(0f,-18f,63f),new Vector3(11f,22f,25f),3f));
        if(revised)
        {
            enclosure=-Box(p,new Vector3(-5f,-22f,20f),new Vector3(26f,21f,35f),3f);
            enclosure=Mathf.Max(enclosure,continuousExit?YoYoTutorialFallFlow.ExitRock(point):YoYoTutorialFallFlow.SteppedExitRock(point));
            if(point.y>-6f)enclosure=Mathf.Min(enclosure,SketchHeight(point.x,point.z)-point.y);
        }
        float density = Mathf.Max(original,enclosure);
        float rough = (Mathf.PerlinNoise(p.x * 0.23f + 19f,p.z * 0.18f + 71f) - 0.5f) * 0.8f;
        float room = Mathf.Max(Ellipsoid(p,new Vector3(0f,-23.5f,0f),new Vector3(6f,6f,8f)) + rough,-29f - p.y);
        float passage = float.MaxValue;
        foreach (var pair in new[] { (new Vector2(0f,0f),new Vector2(4f,8f)),(new Vector2(4f,8f),new Vector2(-7f,16f)),(new Vector2(-7f,16f),new Vector2(-8f,24f)) })
        {
            float distance = Segment(new Vector2(p.x,p.z),pair.Item1,pair.Item2,out _);
            float qy = (p.y + 24.2f) / 4.8f;
            passage = Mathf.Min(passage,Mathf.Max((Mathf.Sqrt(distance * distance / 12.25f + qy * qy) - 1f) * 3.5f + rough * 0.4f,-29f - p.y));
        }
        float pitFloor = Mathf.Lerp(-29f,-34f,Mathf.SmoothStep(0f,1f,(p.z - 27f) / 8f));
        float plan = (new Vector2((p.x + 8f) / 14f,(p.z - 28f) / 17f).magnitude - 1f) * 14f + rough;
        float chamber = Mathf.Max(plan,Mathf.Max(pitFloor - p.y,p.y + 12f + 0.4f * Mathf.Sin(p.x * 0.4f)));
        float slope = Mathf.Lerp(-26f,2f,Mathf.Clamp01((p.z - 43f) / 41f));
        float slopePlan = Box(new Vector3(p.x,0f,p.z),new Vector3(0f,0f,64f),new Vector3(5.5f,30f,22f),1f);
        float ramp = Mathf.Max(slopePlan,Mathf.Max(slope - p.y,p.y - slope - 8f));
        if(revised)ramp=continuousExit?YoYoTutorialFallFlow.ExitTunnel(point):YoYoTutorialFallFlow.SteppedExitTunnel(point);
        return Mathf.Min(density,Mathf.Min(Mathf.Min(room,passage),Mathf.Min(chamber,ramp)));
    }

    private static readonly Vector3[] TentacleTunnel =
    {
        new Vector3(-85f,13f,48f),new Vector3(-82f,7f,59f),new Vector3(-77f,0f,70f),new Vector3(-68f,-6f,81f),
    };
    private static float TentacleCave(Vector3 p)
    {
        float d = float.MaxValue;
        for (int i = 1; i < TentacleTunnel.Length; i++)
        {
            Vector3 a = TentacleTunnel[i-1],b = TentacleTunnel[i];
            float distance = Segment(new Vector2(p.x,p.z),new Vector2(a.x,a.z),new Vector2(b.x,b.z),out float t);
            float floor = Mathf.Lerp(a.y,b.y,t);
            d = Mathf.Min(d,Mathf.Max(distance - 4.4f,Mathf.Max(floor - p.y,p.y - floor - 8f)));
        }
        float room = Mathf.Max(Ellipsoid(p,new Vector3(-67f,-0.6f,83f),new Vector3(15f,6f,17f)),-6f - p.y);
        return Mathf.Min(d,room);
    }

    [MenuItem("Tools/Capstone/YoYo Tutorial Fall/3 Populate Sketch Regions")]
    public static object Populate()
    {
        world = FindWorld();
        if (!world.GenerationComplete) world.Generate(true);
        if (GameObject.Find("Sketch Regions and Creatures") != null) throw new InvalidOperationException("Population already exists.");
        actors = new GameObject("Sketch Regions and Creatures").transform;
        var player = Prefab("Assets/Prefabs/PlayerObjects.prefab",null,new Vector3(-112f,-27.8f,-136f),"PlayerObjects");
        player.transform.rotation = Quaternion.identity;
        var hints = new GameObject("Cave Interaction Hints").AddComponent<InteractionPromptHUD>();
        Edit(hints,s => { B(s,"showText",true); s.FindProperty("carrier").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureCarrier>(true); s.FindProperty("interactor").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureInteractor>(true); });
        var trigger = Prefab("Assets/Prefabs/Gameplay/InteractionHintsTrigger.prefab",actors,new Vector3(-112f,6f,-48f),"Leave Start Cave — hide hints");
        trigger.GetComponent<BoxCollider>().size = new Vector3(14f,14f,4f);
        Edit(trigger.GetComponent<InteractionHintsTrigger>(),s => s.FindProperty("hud").objectReferenceValue = hints);
        var start = Region("01 P and BS — underground start",new Vector3(-112f,-29f,-136f));
        for (int i=0;i<3;i++) Spawn("BounceShroom",start,new Vector3(-124f+i*3f,-28.85f,-111f+i%2),"BS — cave practice " + (i+1));
        var entry = Region("02 Entry glen and scattered goo",new Vector3(-105f,2f,-48f));
        var ss = Region("03 SS — high western spider shelf",Surface(-112f,21f));
        Spawn("SkySpider",ss,Surface(-116f,22f,3f),"SS — western web builder");
        var bf = Region("04 BF — middle flower terrace",Surface(-51f,5f));
        Spawn("BubbleFlower",bf,Surface(-57f,6f,0.1f),"BF — terrace flower 1");
        Spawn("BubbleFlower",bf,Surface(-46f,9f,0.1f),"BF — terrace flower 2");
        var compo = Region("05 Compo — recycling hollow",Surface(-58f,-46f));
        Spawn("CompostCreature",compo,Surface(-58f,-45f,0.2f),"Compo — recycling creature");
        Spawn("BirdBones",compo,Surface(-62f,-43f,0.3f),"Bones for compost");
        var tentacles = Region("06 Tentacles — underground side cave",new Vector3(-68f,-6f,81f));
        var final = Region("07 Final basin — spires, DB and mixed creatures",Surface(94f,22f));

        Scene source = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Scene_Daniel.unity");
        try
        {
            var native = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Creature>(true)).ToArray();
            GameObject tentacle = native.OfType<TentacleCreature>().First().gameObject;
            Clone(tentacle,tentacles,new Vector3(-73f,-5.8f,79f),"Tentacle — cave resident 1");
            Clone(tentacle,tentacles,new Vector3(-62f,-5.8f,87f),"Tentacle — cave resident 2");
            Clone(tentacle,final,Surface(61f,58f,0.2f),"Tentacle — spire approach");
            Clone(tentacle,final,Surface(118f,98f,0.2f),"Tentacle — upper spire");
            var fuzz = native.OfType<FuzzCreature>().First().gameObject;
            Clone(fuzz,entry,Surface(-100f,-42f,0.5f),"Fuzz — first glen");
            Clone(fuzz,final,Surface(61f,31f,0.5f),"Fuzz — final grove 1");
            Clone(fuzz,final,Surface(69f,36f,0.5f),"Fuzz — final grove 2");
            var eater = native.OfType<FireEater>().First().gameObject;
            Clone(eater,final,Surface(132f,10f,0.7f),"Fire Eater — final fire garden");
            MakeDragonFamily(source,final);
        }
        finally { EditorSceneManager.ClosePreviewScene(source); }

        var gooPoints = new[] { new Vector2(-115f,-47f),new Vector2(-94f,-42f),new Vector2(-73f,-49f),new Vector2(-126f,9f),new Vector2(-101f,25f),new Vector2(-62f,1f),new Vector2(-43f,10f),new Vector2(-63f,-39f),new Vector2(-30f,-44f),new Vector2(58f,-27f),new Vector2(71f,30f),new Vector2(86f,48f),new Vector2(113f,64f),new Vector2(126f,90f) };
        foreach (var p in gooPoints) Spawn("GooCreature",actors,Surface(p.x,p.y,0.45f),"Goo — scattered");
        Spawn("SkySpider",final,Surface(137f,53f,3f),"SS — final basin web builder");
        Spawn("BubbleFlower",final,Surface(68f,48f,0.1f),"BF — final basin");
        Spawn("BubbleFlower",final,Surface(119f,82f,0.1f),"BF — tall spire approach");
        Spawn("CompostCreature",final,Surface(65f,13f,0.2f),"Compo — final basin");
        Spawn("BounceShroom",final,Surface(81f,62f,0.1f),"BS — short spire base");
        Spawn("BounceShroom",final,Surface(106f,93f,0.1f),"BS — tall spire base");
        Spawn("FireSource",final,Surface(121f,-5f,0.7f),"Fire Source — dragon garden");
        Spawn("Fire Flower",final,Surface(124f,-7f,0.1f),"Fire Flower — dragon garden");
        Spawn("FireSource",final,Surface(125f,12f,0.7f),"Fire Source — final garden");
        Spawn("Fire Flower",final,Surface(128f,15f,0.1f),"Fire Flower — final garden");
        Spawn("CorruptSeed",final,Surface(145f,32f,0.6f),"Corrupt Seed — outer grove");
        Spawn("GeodeLarva",final,Surface(136f,92f,0.5f),"Geode Larva — final grove");
        Spawn("Food",compo,Surface(-54f,-48f,0.5f),"Food — recycling hollow");
        var birdArea=new GameObject("Birds — native spawn area").AddComponent<BirdSpawnArea>();
        birdArea.transform.SetParent(final,false); birdArea.transform.position=Surface(95f,30f,17f);
        birdArea.Configure(AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab"),6,12f,7127);
        var flock = new GameObject("Final Basin Bird Flock").AddComponent<BirdFlockManager>();
        flock.SkyBounds = new Bounds(new Vector3(100f,22f,48f),new Vector3(115f,36f,140f));
        var squirrels = Prefab("Assets/Prefabs/Systems/SquirrelSystemManager.prefab",final,Surface(129f,106f),"Squirrel Grove");
        Edit(squirrels.GetComponent<SquirrelSystemManager>(),s => { I(s,"population",3); F(s,"squirrelSpawnRadius",4f); });
        AddGroveTrees(squirrels.transform);
        AddColossus(final);
        var edge = Region("08 Optional Asura outer pocket",Surface(145f,-27f));
        Spawn("Asura/MeleeAsura",edge,Surface(143f,-27f,0.5f),"Melee Asura — outer pocket");
        Spawn("Asura/RangedAsura",edge,Surface(151f,-23f,0.5f),"Ranged Asura — outer pocket");
        Spawn("Asura/GeneralAsura",edge,Surface(146f,-37f,0.5f),"General Asura — outer pocket");
        new GameObject("Native population limits").AddComponent<RealmPopulation>();
        MakeSpire(final,new Vector2(76f,66f),46f,7,71,"Spire A — lower tower");
        MakeSpire(final,new Vector2(111f,103f),73f,11,92,"Spire B — high tower");
        SaveGeneratedMeshes(actors);
        YoYoTutorialFallFlow.ArrangePlacements();
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
        AssetDatabase.SaveAssets();
        LandmarkScenePreview.BuildNow();
        TerrainSculptWindow.Open();
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(-4f,0f,4f),Quaternion.Euler(61f,-8f,0f),230f);
        return new { scene = ScenePath, placedCreatures = actors.GetComponentsInChildren<Creature>(true).Length, groups = actors.childCount };
    }

    private static void MakeDragonFamily(Scene source,Transform parent)
    {
        BoneKiln original = source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BoneKiln>(true)).First();
        var go = new GameObject("DB — large dragon bone and three flying children");
        go.transform.SetParent(parent,false); go.transform.position = Surface(98f,-4f);
        var mother = go.AddComponent<BoneKiln>();
        EditorUtility.CopySerialized(original,mother);
        var bubblePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Bubble.prefab");
        var fuelObject = (GameObject)PrefabUtility.InstantiatePrefab(bubblePrefab);
        fuelObject.name = "Starting Dragon Fuel";
        Edit(fuelObject.GetComponent<FireBubble>(),s => { F(s,"boneFuelHeat",240f); F(s,"ageRate",0f); });
        var fuel = PrefabUtility.SaveAsPrefabAsset(fuelObject,Folder + "/StartingDragonFuel.prefab").GetComponent<FireBubble>();
        Object.DestroyImmediate(fuelObject);
        Edit(mother,s => { s.FindProperty("world").objectReferenceValue = world; B(s,"placeAtThisObject",true); I(s,"sinkholeOverride",0); I(s,"children",3); I(s,"nearbyTentacles",0); s.FindProperty("tentacleTemplate").objectReferenceValue = null; s.FindProperty("childStartingFuel").objectReferenceValue = fuel; I(s,"childStartingBubbleCount",3); });
    }

    private static void MakeSpire(Transform parent,Vector2 at,float height,int ledges,int seed,string name)
    {
        var shape = new SpireShape { height=height,bury=5f,innerRadius=3.4f,wallBase=2.6f,wallTop=2.2f,topScale=0.9f,flare=3.8f,lean=1.1f,crown=1.5f,doorHalfAngle=0.23f,windows=0.12f,noiseSeed=seed,glazePatchDensity=0f,roughness=0.12f,drumLength=9f };
        Mesh mesh = LandmarkShapes.Spire(shape,new System.Random(seed),ledges,false,false,out _);
        var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
        go.transform.SetParent(parent,false); go.transform.position = Surface(at.x,at.y,-0.2f);
        go.layer=7; go.tag="Ground";
        go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshCollider>().sharedMesh=mesh;
        var material = Mat("Assets/Materials/Landmarks/MAT_Landmark_SpireStone.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial=material;
        SpireStructure.Build(go.transform,shape,material);
        // Cap the hollow shaft with the same native faceted geometry used by BasinLevel.
        var cap = new FacetMesh(height-0.35f,height,0.3f);
        Vector3 centre=shape.Axis(height-0.05f);
        for(int i=0;i<28;i++)
        {
            float a=i*Mathf.PI*2f/28f,b=(i+1)*Mathf.PI*2f/28f;
            Vector3 p0=centre+new Vector3(Mathf.Cos(a),0f,Mathf.Sin(a))*(shape.Inner(a,height)+0.6f);
            Vector3 p1=centre+new Vector3(Mathf.Cos(b),0f,Mathf.Sin(b))*(shape.Inner(b,height)+0.6f);
            cap.Triangle(centre,p0,p1,Vector3.up);
            cap.Triangle(centre-Vector3.up*0.3f,p0-Vector3.up*0.3f,p1-Vector3.up*0.3f,Vector3.down);
        }
        var lid=new GameObject("Crown Platform",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
        lid.transform.SetParent(go.transform,false); lid.layer=7; lid.tag="Ground";
        Mesh capMesh=cap.Build("Spire Crown"); lid.GetComponent<MeshFilter>().sharedMesh=capMesh; lid.GetComponent<MeshCollider>().sharedMesh=capMesh; lid.GetComponent<MeshRenderer>().sharedMaterial=material;
        RealmSites.Seed("tutorial_fall_spire_"+seed,go.transform.position+shape.Axis(height)+Vector3.up*1.5f,Mat("Assets/Materials/Landmarks/MAT_Landmark_Seed.mat"),go.transform);
    }

    private static void AddColossus(Transform parent)
    {
        var go=new GameObject("Ore Colossus — final upper grove"); go.transform.SetParent(parent,false); go.transform.position=Surface(137f,76f);
        go.AddComponent<Rigidbody>().isKinematic=true;
        var colossus=go.AddComponent<OreColossus>();
        Edit(colossus,s=>{ s.FindProperty("world").objectReferenceValue=world; B(s,"placeInWorld",false); I(s,"seed",901); s.FindProperty("larvaPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GeodeLarva>("Assets/Prefabs/Creatures/OreColossus/GeodeLarva.prefab"); foreach(string n in new[]{"rock","ore","heart","geode"}) s.FindProperty(n+"Material").objectReferenceValue=Mat("Assets/Materials/Creatures/OreColossus/"+char.ToUpper(n[0])+n.Substring(1)+".mat"); F(s,"ageRate",0f); F(s,"age",100000f); });
    }

    public static void AddGroveTrees(Transform parent)
    {
        for(int i=0;i<3;i++)
        {
            float a=i*Mathf.PI*2f/3f;
            var tree=Prefab("Assets/Prefabs/WorldDecor/Decor_Fronds_"+i+".prefab",parent,Surface(128f+Mathf.Cos(a)*7f,106f+Mathf.Sin(a)*7f),"Grove Tree "+(i+1));
            tree.transform.localScale=Vector3.one*2.5f;
            tree.AddComponent<SquirrelTree>();
        }
    }

    private static void SaveGeneratedMeshes(Transform root)
    {
        var saved=new Dictionary<Mesh,Mesh>(); int index=0;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh=filter.sharedMesh;
            if(mesh==null || EditorUtility.IsPersistent(mesh)) continue;
            if(!saved.ContainsKey(mesh)) { AssetDatabase.CreateAsset(mesh,Folder+"/Mesh_"+(index++)+".asset"); saved[mesh]=mesh; }
        }
        foreach(var collider in root.GetComponentsInChildren<MeshCollider>(true))
            if(collider.sharedMesh!=null && !EditorUtility.IsPersistent(collider.sharedMesh)) AssetDatabase.CreateAsset(collider.sharedMesh,Folder+"/Mesh_"+(index++)+".asset");
    }

    private static Transform Region(string name,Vector3 position)
    {
        var go=new GameObject(name); go.transform.SetParent(actors,false); go.transform.position=position; return go.transform;
    }
    private static GameObject Spawn(string kind,Transform parent,Vector3 position,string name)
    {
        string path=kind=="GeodeLarva" ? "Assets/Prefabs/Creatures/OreColossus/GeodeLarva.prefab" : "Assets/Prefabs/Creatures/"+kind+".prefab";
        var go=Prefab(path,parent,position,name);
        var creature=go.GetComponent<Creature>();
        if(creature!=null) Edit(creature,s=>F(s,"ageRate",0f));
        return go;
    }
    private static GameObject Clone(GameObject source,Transform parent,Vector3 position,string name)
    {
        var go=Object.Instantiate(source,parent); go.name=name; go.transform.position=position; go.transform.rotation=Quaternion.identity; go.SetActive(true);
        var creature=go.GetComponent<Creature>(); if(creature!=null) Edit(creature,s=>F(s,"ageRate",0f));
        return go;
    }
    private static GameObject Prefab(string path,Transform parent,Vector3 position,string name)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(prefab==null) throw new InvalidOperationException("Missing prefab: "+path);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab); if(parent!=null) go.transform.SetParent(parent,false); go.transform.position=position; go.name=name; return go;
    }
    public static Vector3 Surface(float x,float z,float lift=0f)
    {
        if(world==null) world=FindWorld();
        float y=float.NegativeInfinity;
        foreach(var hit in Physics.RaycastAll(new Vector3(x,100f,z),Vector3.down,180f,1<<7,QueryTriggerInteraction.Ignore))
            if(world.OwnsCollider(hit.collider)) y=Mathf.Max(y,hit.point.y);
        if(float.IsNegativeInfinity(y)) y=SketchHeight(x,z);
        return new Vector3(x,y+lift,z);
    }
    private static float PathHeight(float h,float x,float z,Vector3[] nodes,float radius,float blend)
    {
        float nearest=float.MaxValue,target=h;
        for(int i=1;i<nodes.Length;i++)
        {
            float d=Segment(new Vector2(x,z),new Vector2(nodes[i-1].x,nodes[i-1].z),new Vector2(nodes[i].x,nodes[i].z),out float t);
            if(d<nearest){nearest=d;target=Mathf.Lerp(nodes[i-1].y,nodes[i].y,t);}
        }
        return Mathf.Lerp(h,target,1f-Mathf.SmoothStep(0f,1f,(nearest-radius)/blend));
    }
    private static float Plateau(float h,float x,float z,float cx,float cz,float rx,float rz,float target,float edge) => Mathf.Lerp(h,target,Mathf.SmoothStep(0f,1f,(1.1f-Oval(x,z,cx,cz,rx,rz))/edge));
    private static float Clearing(float h,float x,float z,float cx,float cz,float radius,float target) => Mathf.Lerp(h,target,1f-Mathf.SmoothStep(0f,1f,(Vector2.Distance(new Vector2(x,z),new Vector2(cx,cz))-radius)/8f));
    private static float Oval(float x,float z,float cx,float cz,float rx,float rz) => new Vector2((x-cx)/rx,(z-cz)/rz).magnitude;
    private static float Hill(float x,float z,float cx,float cz,float rx,float rz,float height) => height*Mathf.Exp(-1.5f*((x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz)));
    private static float Segment(Vector2 p,Vector2 a,Vector2 b,out float t){var d=b-a;t=Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t);}
    private static float Ellipsoid(Vector3 p,Vector3 centre,Vector3 radius){var d=p-centre;return(new Vector3(d.x/radius.x,d.y/radius.y,d.z/radius.z).magnitude-1f)*Mathf.Min(radius.x,Mathf.Min(radius.y,radius.z));}
    private static float Box(Vector3 p,Vector3 centre,Vector3 half,float r){var d=p-centre;var q=new Vector3(Mathf.Abs(d.x),Mathf.Abs(d.y),Mathf.Abs(d.z))-half+Vector3.one*r;return Vector3.Max(q,Vector3.zero).magnitude+Mathf.Min(Mathf.Max(q.x,Mathf.Max(q.y,q.z)),0f)-r;}
    private static ProceduralWorld FindWorld(){var w=Object.FindFirstObjectByType<ProceduralWorld>();if(w==null||w.gameObject.scene.path!=ScenePath)throw new InvalidOperationException("Open YoYo Tutorial Fall first.");return w;}
    private static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path);
    private static void Edit(Object target,Action<SerializedObject> apply){var s=new SerializedObject(target);apply(s);s.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);}
    private static void F(SerializedObject s,string name,float value){var p=s.FindProperty(name);if(p==null)throw new MissingFieldException(name);p.floatValue=value;}
    private static void I(SerializedObject s,string name,int value)=>s.FindProperty(name).intValue=value;
    private static void B(SerializedObject s,string name,bool value)=>s.FindProperty(name).boolValue=value;
    private static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;int split=path.LastIndexOf('/');EnsureFolder(path.Substring(0,split));AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));}
}
