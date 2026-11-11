using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Playable leaf layout, authored from native creature prefabs and the ArtReference material palette.</summary>
[InitializeOnLoad]
public static class LeafMapSetup
{
    public const string ScenePath="Assets/Scenes/Scene_LeafMap.unity";
    private const string Folder="Assets/Generated/LeafMap";
    private const string Captures="ArtCaptures/LeafMap";
    private static ProceduralWorld world;
    private static Transform root;
    private static LeafMapEcology ecology;
    private static Material stone, growth, crystal, time, heat, bone;
    private static System.Random random;
    private static int meshId;
    private static double nextPoll;

    static LeafMapSetup()
    {
        EditorSceneManager.sceneOpened += (scene,mode) => { if(scene.path==ScenePath)EditorApplication.delayCall+=Preview; };
        EditorApplication.update+=Poll;
        EditorApplication.playModeStateChanged+=state=> {if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=Preview;};
        EditorApplication.delayCall+=Preview;
    }
    private static void Poll()
    {
        if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)return;
        nextPoll=EditorApplication.timeSinceStartup+1;
        const string request="Temp/LeafMap.request";
        if(!File.Exists(request))return;
        string command=File.ReadAllText(request).Trim();File.Delete(request);
        try
        {
            if(command=="refresh")AssetDatabase.Refresh();
            else if(command=="create")Create();
            else if(command=="capture")Capture();
            else if(command=="preview")Preview();
            else if(command=="validate")LeafMapValidation.Run();
            else if(command=="validatequick")LeafMapValidation.RunQuick();
            else if(command=="refine") {Refine();Capture();}
            else if(command=="review")Review();
            else if(command=="artpass")LeafMapArtPass.ApplyAndCapture();
            else if(command=="inspecttutorial")LeafMapTutorialGraft.Inspect();
            else if(command=="timepass")LeafMapTimePass.ApplyAndCapture();
            else if(command=="timedecor")LeafMapTimePass.Apply();
            else if(command=="playcapture")LeafMapPlayCapture.Begin();
            else if(command=="perf")LeafMapPerfProbe.Begin();
            else if(command=="perfquick")LeafMapPerfProbe.BeginQuick();
            else if(command=="perfcontent")LeafMapPerfProbe.BeginContent();
            else if(command=="awaken")LeafMapAwakeningTest.Begin();
            else if(command=="lilytest")LeafMapLilyTest.Begin();
            else if(command=="motherprobe")LeafMapMotherProbe.Run();
            else if(command=="mothertest")LeafMapMotherTest.Begin();
            else throw new InvalidOperationException("Unknown leaf-map command: "+command);
            File.WriteAllText("Temp/LeafMap.status","OK: "+command);
        }
        catch(Exception e) { File.WriteAllText("Temp/LeafMap.status",e.ToString());Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Leaf Map/1 Create Playable Leaf Map")]
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Leave Play Mode first.");
        if(File.Exists(ScenePath))throw new InvalidOperationException("Scene_LeafMap already exists. Edit the saved scene; do not replace authored changes.");
        EnsureFolder(Folder);
        random=new System.Random(211026);meshId=0;
        Scene source=EditorSceneManager.OpenPreviewScene("Assets/Scenes/Scene_Daniel.unity");
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            root=new GameObject("Leaf Map — authored habitats and traversal").transform;
            Materials();Lighting();Terrain();
            ecology=root.gameObject.AddComponent<LeafMapEcology>();
            Edit(ecology,s=>Ref(s,"world",world));
            var sourceCreatures=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Creature>(true)).ToArray();
            var template=sourceCreatures.OfType<TentacleCreature>().First();
            // A scene-local template lives under an inactive holder; all material references remain native.
            var templates=Group("Creature templates (inactive)");
            var tentacle=Clone(template.gameObject,templates,Vector3.zero,"Tentacle template").GetComponent<TentacleCreature>();
            templates.gameObject.SetActive(false);
            Landmarks(source,tentacle);
            Tutorial(tentacle);
            FlowerMeadow();WestCliffs(tentacle);BoneRest(tentacle);Rift();NorthGrove();Squirrels();
            Cameras();
            Prefab("Assets/Prefabs/PlayerObjects.prefab",null,new Vector3(0,3.4f,-141),"PlayerObjects");
            var carrier=FindInScene<PlayerCreatureCarrier>(scene);
            var hud=new GameObject("InteractionPromptHUD").AddComponent<InteractionPromptHUD>();
            Edit(hud,s=> { Ref(s,"carrier",carrier);Ref(s,"interactor",carrier.GetComponent<PlayerCreatureInteractor>());Bool(s,"showText",true); });
            var originalAudio=source.GetRootGameObjects().FirstOrDefault(g=>g.name=="WwiseGlobal");
            if(originalAudio!=null)Object.Instantiate(originalAudio).name="WwiseGlobal";
            new GameObject("Native population limits").AddComponent<RealmPopulation>();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,ScenePath);
            Refine();
            Preview();
            ValidateGeometry();
            File.WriteAllText("Temp/LeafMap.build.txt","Created "+ScenePath+"\n"+Describe());
        }
        finally { EditorSceneManager.ClosePreviewScene(source); }
    }

    public static void BuildBatch()
    {
        try { Create();Capture();File.WriteAllText("Temp/LeafMap.batch.txt","PASS\n"+Describe()); }
        catch(Exception e) { File.WriteAllText("Temp/LeafMap.batch.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Capstone/Leaf Map/2 Show Terrain And Landmarks")]
    public static void Preview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        Scene scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)return;
        world=FindInScene<ProceduralWorld>(scene);
        if(world==null)return;
        if(world.SculptData!=null)LeafMapTerrain.Scale=LeafMapTerrain.ReadScale(world.SculptData.Layout);
        if(!world.GenerationComplete || world.GetComponentsInChildren<ProceduralWorldChunk>().Length==0)world.Generate(true);
        LandmarkScenePreview.BuildNow();
        LeafMapArtPass.PreviewChildren(FindInScene<BoneKiln>(scene));
    }

    private static void Materials()
    {
        stone=Variant("Assets/Materials/HabitatBlock/HB_Ground.mat","Leaf_InkStone",m=> {
            m.SetColor("_Color",new Color(0.26f,0.28f,0.30f));m.SetColor("_ShadowColor",new Color(0.075f,0.085f,0.105f));
            m.SetColor("_LineColor",new Color(0.76f,0.79f,0.82f));m.SetColor("_LineColor2",new Color(0.64f,0.75f,0.79f));
            m.SetFloat("_FakeLightAmount",0.6f);m.SetFloat("_HatchStrength",0.1f);m.SetFloat("_FloorFreq",2.8f);
            m.SetFloat("_RingFreq",11f);m.SetFloat("_FloorTravel",0.015f);m.SetFloat("_RaySpeed",0.015f);
            m.SetVector("_Center",new Vector4(-200,0,220,0));
        });
        growth=Mat("Assets/Materials/HabitatBlock/HB_GreenKelp.mat");
        crystal=Mat("Assets/Materials/HabitatBlock/HB_Crystal_Fluorite.mat");
        time=Mat("Assets/Materials/HabitatBlock/HB_FX_TimeTide.mat");
        heat=Mat("Assets/Materials/HabitatBlock/HB_FX_HeatPool.mat");
        bone=Mat("Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat");
    }
    private static void Lighting()
    {
        var sun=Group("Sun — warm neutral key").gameObject.AddComponent<Light>();
        sun.type=LightType.Directional;sun.intensity=1.7f;sun.color=new Color(1,0.97f,0.9f);
        sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(43,-32,0);RenderSettings.sun=sun;
        RenderSettings.skybox=Mat("Assets/Materials/World/MAT_Sky_LiquidInk.mat");
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(0.38f,0.42f,0.5f);RenderSettings.ambientEquatorColor=new Color(0.24f,0.27f,0.32f);
        RenderSettings.ambientGroundColor=new Color(0.12f,0.14f,0.18f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=new Color(0.3f,0.35f,0.41f);RenderSettings.fogStartDistance=180;RenderSettings.fogEndDistance=470;
    }
    private static void Terrain()
    {
        var go=new GameObject("Procedural World — sculptable leaf landform");go.layer=7;go.tag="Ground";
        world=go.AddComponent<ProceduralWorld>();
        Edit(world,s=> {
            s.FindProperty("worldSize").vector2Value=new Vector2(216,324);Bool(s,"leafMap",true);
            Float(s,"cellSize",1.25f);Int(s,"chunkCells",24);Int(s,"seed",211026);
            Float(s,"surfaceAmplitude",24);Float(s,"ridges",0.2f);Float(s,"rimHeight",65);
            Float(s,"overhangAmplitude",0);Float(s,"caveWidth",0);Int(s,"sinkholeCount",0);Float(s,"firstSinkholeDistance",0);
            Float(s,"innerGap",45);Float(s,"innerDetail",0);Float(s,"ceilingMirror",0.2f);Float(s,"innerInvert",0.2f);
            Float(s,"blockingRadius",500);Int(s,"workerCount",2);Bool(s,"placePlayerOnSurface",false);
            Ref(s,"surfaceMaterial",stone);Ref(s,"innerMaterial",Mat("Assets/Materials/HabitatBlock/HB_Inner.mat"));
        });
        string layout=world.CaptureLayout(out _);
        layout=System.Text.RegularExpressions.Regex.Replace(layout,"\"sinkholes\":\\[.*?\\]","\"sinkholes\":[{\"position\":{\"x\":0,\"y\":10},\"radius\":5},{\"position\":{\"x\":46,\"y\":15},\"radius\":4.5}]");
        var data=ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(layout,Array.Empty<float>(),"Leaf map / user sketch 2026-11-02 / LeafMapTerrain v1");
        AssetDatabase.CreateAsset(data,Folder+"/LeafTerrain.asset");
        Edit(world,s=>Ref(s,"sculptData",data));
        world.Generate(true);
    }
    private static void Landmarks(Scene source,TentacleCreature tentacle)
    {
        var group=Group("04 Central tower — three climbing stages");
        var original=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<HangingSpire>(true)).First();
        var go=new GameObject("Hanging Spire — central goal");go.transform.SetParent(group,false);go.transform.position=Ground(0,10);
        var spire=go.AddComponent<HangingSpire>();EditorUtility.CopySerialized(original,spire);
        Edit(spire,s=> {
            Ref(s,"world",world);Ref(s,"tentacleTemplate",tentacle);Ref(s,"seedSource",null);Bool(s,"placeAtThisObject",true);
            Int(s,"sinkholeOverride",0);Float(s,"height",65);Float(s,"minStageClimb",13);Float(s,"secondStageClimb",17);
            Float(s,"finalStageClimb",19);Float(s,"crownSeedSize",3);Float(s,"crownSeedFloat",2);
            Int(s,"wallCrawlers",2);Int(s,"groundCrawlers",2);Bool(s,"built",false);
        });
        var family=Group("05 Bone sanctuary — mother and young");
        var originalMother=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BoneKiln>(true)).First();
        var mother=new GameObject("Bone mother — fifteen fire bubbles").AddComponent<BoneKiln>();
        mother.transform.SetParent(family,false);mother.transform.position=Ground(46,15);EditorUtility.CopySerialized(originalMother,mother);
        Edit(mother,s=> {
            Ref(s,"world",world);Ref(s,"tentacleTemplate",tentacle);Ref(s,"childStartingFuel",null);
            Bool(s,"placeAtThisObject",true);Int(s,"sinkholeOverride",1);Int(s,"bubblesToWarm",15);Int(s,"childStartingBubbleCount",0);
            Int(s,"nearbyTentacles",2);Int(s,"children",3);Float(s,"flowers",0.18f);Bool(s,"built",false);
        });
    }
    private static void Tutorial(TentacleCreature tentacle)
    {
        var group=Group("01 Tutorial cavern — stem and first reveal");
        Spawn("BounceShroom",group,new Vector3(-4,2.1f,-134),"Bounce practice");
        Spawn("GooCreature",group,new Vector3(3,2.4f,-128),"Goo practice");
        Clone(tentacle.gameObject,group,new Vector3(-3,3,-114),"Tentacle — cave companion");
        Lamp(group,new Vector3(0,6,-141),new Color(0.5f,0.7f,1),18,18);
        Lamp(group,new Vector3(0,6,-108),new Color(0.7f,1,0.7f),14,18);
        Stream(group,new []{new Vector2(3,-146),new Vector2(3,-132),new Vector2(3,-115),new Vector2(4,-103)},0.65f,time,"Cyan thread toward the exit",2.1f);
    }
    private static void FlowerMeadow()
    {
        var group=Group("02 Bubble meadow — seeds and rising canopies");
        var flowerTemplate=Prefab("Assets/Prefabs/Creatures/BubbleFlower.prefab",null,Vector3.zero,"Leaf flower template");
        Edit(flowerTemplate.GetComponent<BubbleFlower>(),s=> {
            s.FindProperty("groundFiringInterval").vector2Value=new Vector2(15,26);Float(s,"ageRate",0);
        });
        var bubbleObject=Prefab("Assets/Prefabs/Creatures/TrappingBubble.prefab",null,Vector3.zero,"Meadow bubble template");
        Edit(bubbleObject.GetComponent<TrappingBubble>(),s=> {Float(s,"upwardSpeed",0.85f);Float(s,"ageRate",0);});
        var bubble=PrefabUtility.SaveAsPrefabAsset(bubbleObject,Folder+"/MeadowBubble.prefab").GetComponent<TrappingBubble>();
        Object.DestroyImmediate(bubbleObject);
        Edit(flowerTemplate.GetComponent<BubbleFlower>(),s=>Ref(s,"bubblePrefab",bubble));
        var flowerPrefab=PrefabUtility.SaveAsPrefabAsset(flowerTemplate,Folder+"/MeadowFlower.prefab");Object.DestroyImmediate(flowerTemplate);
        var points=new[]{new Vector2(-17,-83),new Vector2(-22,-73),new Vector2(-20,-60),new Vector2(-24,-50),new Vector2(-19,-39),new Vector2(-29,-39),
            new Vector2(9,-81),new Vector2(14,-70),new Vector2(15,-58),new Vector2(13,-47),new Vector2(18,-37),new Vector2(24,-49),new Vector2(-32,-63),new Vector2(27,-61)};
        foreach(var p in points)
        {
            var go=Prefab(AssetDatabase.GetAssetPath(flowerPrefab),group,Ground(p.x,p.y),"Bubble flower — meadow");
            SquirrelTree.PlaceOnGround(go.transform,Ground(p.x,p.y)+Vector3.up*0.05f);ecology.RegisterFlower(go.GetComponent<BubbleFlower>());
        }
        var seedObject=Prefab("Assets/Prefabs/Creatures/Food.prefab",null,Vector3.zero,"Bubble flower seed");
        var seed=seedObject.AddComponent<PlantableBubbleSeed>();Edit(seed,s=>Ref(s,"flowerPrefab",flowerPrefab.GetComponent<BubbleFlower>()));
        Edit(seedObject.GetComponent<SquirrelFood>(),s=>Float(s,"ageRate",0));
        PrefabUtility.SaveAsPrefabAsset(seedObject,Folder+"/BubbleFlowerSeed.prefab");Object.DestroyImmediate(seedObject);
        for(int i=0;i<10;i++)
        {
            Vector2 p=new Vector2(i%2==0 ? -15f-i*0.7f : 16f+i*0.5f,-81+i*4.1f);
            var go=Prefab(Folder+"/BubbleFlowerSeed.prefab",group,Ground(p.x,p.y,0.3f),"Plantable bubble flower seed "+(i+1));
            SquirrelTree.PlaceOnGround(go.transform,Ground(p.x,p.y,0.1f));
        }
        // Tall silhouettes frame the playable plants. Clear gaps between their trunks preserve sightlines.
        foreach(var p in new[]{new Vector2(-32,-78),new Vector2(-37,-58),new Vector2(-28,-27),new Vector2(28,-75),new Vector2(36,-56),new Vector2(27,-32)})
            Canopy(group,p,Range(8,15));
        Grove(group,new Vector2(-23,-64),24,32,growth);
        Grove(group,new Vector2(24,-56),21,28,growth);
        Stream(group,new[]{new Vector2(-39,-28),new Vector2(-28,-44),new Vector2(-20,-59),new Vector2(-12,-78),new Vector2(-8,-96)},0.65f,Mat("Assets/Materials/World/MAT_Realm_Stream.mat"),"Meadow runnel");
        Spawn("CompostCreature",group,Ground(28,-36,0.2f),"Compost — meadow edge");
    }
    private static void WestCliffs(TentacleCreature tentacle)
    {
        var group=Group("03 West cliffs — tentacle ledges and alternate ascent");
        var points=new[]{new Vector2(-36,-28),new Vector2(-48,-1),new Vector2(-57,17),new Vector2(-55,44),new Vector2(-37,62)};
        foreach(var p in points)Clone(tentacle.gameObject,group,Ground(p.x,p.y,1),"Tentacle — western shelf");
        foreach(var p in new[]{new Vector2(-37,-25),new Vector2(-49,1),new Vector2(-47,32),new Vector2(-30,55)})Spawn("GooCreature",group,Ground(p.x,p.y,0.4f),"Goo — climbing rest stop");
        Spawn("SkySpider",group,Ground(-67,38,2),"Spider — high shelf shortcut");
        Spawn("BubbleFlower",group,Ground(-32,-27,0.1f),"Bubble flower — first cliff lift");
        ecology.RegisterFlower(group.GetComponentsInChildren<BubbleFlower>().Last());
        for(int i=0;i<11;i++)
        {
            Vector2 p=new Vector2(Range(-87,-66),Range(-8,77));
            if(LeafMapTerrain.Footprint(p.x,p.y)<8 || LeafMapTerrain.RouteDistance(p)<6)continue;
            Rock(group,Ground(p.x,p.y,-0.5f),new Vector3(Range(2,4),Range(4,9),Range(2,4)),stone,"Grip crag");
        }
        // Two reachable islands over a low saddle: a deliberate gap, with a walk-around on the ridge.
        Rock(group,new Vector3(-39,21,30),new Vector3(4.5f,9,5),stone,"Cross-gap landing 1");
        Rock(group,new Vector3(-26,22,32),new Vector3(3.8f,11,4),stone,"Cross-gap landing 2");
        Clone(tentacle.gameObject,group,new Vector3(-39,31,30),"Tentacle — gap lookout");
        Grove(group,new Vector2(-64,39),24,22,Mat("Assets/Materials/World/MAT_Realm_Frost.mat"));
    }
    private static void BoneRest(TentacleCreature tentacle)
    {
        var group=Group("05b Quiet bone meadow — rest and approach");
        Grove(group,new Vector2(52,-15),17,16,Mat("Assets/Materials/HabitatBlock/HB_GrowthMushroom.mat"));
        Grove(group,new Vector2(63,14),11,14,Mat("Assets/Materials/World/MAT_Realm_Frost.mat"));
        Clone(tentacle.gameObject,group,Ground(29,-4,0.8f),"Tentacle — bone approach");
        foreach(var p in new[]{new Vector2(33,-10),new Vector2(60,2)})
        {
            var go=Spawn("BubbleFlower",group,Ground(p.x,p.y,0.1f),"Bubble flower — sanctuary edge");
            Edit(go.GetComponent<BubbleFlower>(),s=>s.FindProperty("groundFiringInterval").vector2Value=new Vector2(18,30));
            ecology.RegisterFlower(go.GetComponent<BubbleFlower>());
        }
        Stream(group,new[]{new Vector2(23,-28),new Vector2(30,-14),new Vector2(27,0),new Vector2(25,17),new Vector2(26,33)},0.8f,time,"Cyan time beside the sleeping bones");
    }
    private static void Rift()
    {
        var group=Group("06 Fire rift — rising fuel and cliff traversal");
        var fire=Prefab("Assets/Prefabs/Creatures/Bubble.prefab",null,Vector3.zero,"Rift bubble template");
        Edit(fire.GetComponent<FireBubble>(),s=> {Float(s,"upwardSpeed",1.5f);Float(s,"horizontalSpeed",0.03f);Float(s,"swayDistance",0.18f);Float(s,"ageRate",0);Bool(s,"limitLifetime",false);});
        var prefab=PrefabUtility.SaveAsPrefabAsset(fire,Folder+"/RiftFireBubble.prefab").GetComponent<FireBubble>();Object.DestroyImmediate(fire);
        Edit(ecology,s=>Ref(s,"riftBubblePrefab",prefab));
        Stream(group,new[]{new Vector2(78,30),new Vector2(73,43),new Vector2(67,59),new Vector2(61,76),new Vector2(57,85)},3.8f,heat,"Heat at the bottom of the incision");
        for(int i=0;i<4;i++)
        {
            var p=new Vector2(73-i*4,37+i*12);
            Rock(group,Ground(p.x-10,p.y,-1),new Vector3(3,4,4),stone,"Rift viewing perch");
        }
        // Native fire flowers remain below the ledge, while the vent budget supplies catchable ascending fuel.
        Spawn("FireSource",group,new Vector3(71,-17,48),"Fire source — deep vent");
        Spawn("Fire Flower",group,new Vector3(73,-18,47),"Fire flower — deep vent");
        Grove(group,new Vector2(54,56),14,15,Mat("Assets/Materials/HabitatBlock/HB_Flora.mat"));
        Lamp(group,new Vector3(70,-10,49),new Color(1,0.12f,0.025f),70,32);
    }
    private static void NorthGrove()
    {
        var group=Group("07 Leaf tip — light grove and return loop");
        for(int i=0;i<9;i++)
        {
            var p=new Vector2(Range(-20,24),Range(91,116));
            if(LeafMapTerrain.RouteDistance(p)<4 || LeafMapTerrain.Footprint(p.x,p.y)<6)continue;
            var mesh=new RealmMeshes();mesh.Crystal(Vector3.zero,Quaternion.Euler(Range(-12,12),Range(0,360),Range(-12,12)),Range(4,10),Range(0.6f,1.6f),6,i,random);
            Part("Fluorite landmark",group,mesh.Build("Fluorite"),crystal,Ground(p.x,p.y),true);
        }
        Grove(group,new Vector2(-20,93),19,24,growth);Grove(group,new Vector2(23,92),18,22,growth);
        Spawn("GooCreature",group,Ground(13,79,0.4f),"Goo — return path");
        Spawn("BounceShroom",group,Ground(15,65),"Bounce — tower return");
        Stream(group,new[]{new Vector2(-7,113),new Vector2(-12,96),new Vector2(-4,80),new Vector2(9,62),new Vector2(13,44)},0.8f,time,"Time vein down from the leaf tip");
        var manager=Group("Bird flock — meadow to tip").gameObject.AddComponent<BirdFlockManager>();
        manager.SkyBounds=new Bounds(new Vector3(0,32,10),new Vector3(140,30,210));
        var area=Group("Birds over the meadow").gameObject.AddComponent<BirdSpawnArea>();area.transform.position=new Vector3(8,23,-37);
        area.Configure(AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab"),7,8,719);
    }
    private static void Squirrels()
    {
        var group=Group("02b Seed gatherers — meadow ecology");
        var go=Prefab("Assets/Prefabs/Systems/SquirrelSystemManager.prefab",group,Ground(17,-65),"Meadow seed gatherers");
        var manager=go.GetComponent<SquirrelSystemManager>();
        Edit(manager,s=> {Int(s,"population",3);Int(s,"initialFoodCount",9);Int(s,"maxUnstoredFood",18);Int(s,"foodPerCycle",2);Float(s,"respawnInterval",40);Float(s,"territoryRadius",19);Float(s,"foodSpawnRadius",10);Bool(s,"autoDiscoverScene",false);Ref(s,"foodPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/BubbleFlowerSeed.prefab"));});
        foreach(var p in new[]{new Vector2(22,-78),new Vector2(-27,-68),new Vector2(22,-47)})
        {
            var tree=Rock(group,Ground(p.x,p.y,3),new Vector3(1.2f,3,1.2f),stone,"Seed grove trunk");
            var anchor=tree.gameObject.AddComponent<SquirrelTree>();manager.Register(anchor);
            for(int j=0;j<4;j++)Frond(group,Ground(p.x,p.y,3),j*Mathf.PI*0.5f,Range(2.5f,4.5f),growth);
        }
        foreach(var food in root.GetComponentsInChildren<SquirrelFood>())manager.Register(food);
        // Author visible individuals; initialize their territories once, after runtime terrain generation.
        for(int i=0;i<3;i++)
        {
            var p=new[]{new Vector2(22,-78),new Vector2(-27,-68),new Vector2(22,-47)}[i];
            var squirrel=Prefab("Assets/Prefabs/Creatures/Squirrel.prefab",group,Ground(p.x+2,p.y,0.1f),"Meadow squirrel "+(i+1)).GetComponent<Squirrel>();
            var nest=Prefab("Assets/Prefabs/Creatures/Nest.prefab",group,Ground(p.x+2,p.y+1,0.1f),"Seed nest "+(i+1)).GetComponent<SquirrelNest>();
            SquirrelTree.PlaceOnGround(squirrel.transform,Ground(p.x+2,p.y,0.05f));
            SquirrelTree.PlaceOnGround(nest.transform,Ground(p.x+2,p.y+1,0.05f));
            manager.Register(squirrel);manager.Register(nest);
        }
        manager.enabled=false;
        Edit(ecology,s=>Ref(s,"squirrels",manager));
    }

    private static void Grove(Transform parent,Vector2 centre,float radius,int count,Material material)
    {
        var group=Group("Composed grove",parent);var leaves=new RealmMeshes();
        for(int i=0;i<count;i++)
        {
            float a=Range(0,Mathf.PI*2),r=radius*Mathf.Sqrt(Range(0,1));Vector2 p=centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;
            if(LeafMapTerrain.Footprint(p.x,p.y)<7 || LeafMapTerrain.RouteDistance(p)<5 || new Vector2(p.x,p.y-10).magnitude<24 || new Vector2(p.x-46,p.y-15).magnitude<24 || LeafMapTerrain.Height(p.x,p.y)<0)continue;
            Vector3 at=Ground(p.x,p.y,0.03f);float size=Range(1.2f,3.8f);
            for(int j=0;j<3;j++)AddFrond(leaves,at,a+j*2.1f,size,0.8f+i*0.03f);
        }
        Part("Fronds — open route between clusters",group,leaves.Build("Clustered foliage"),material,Vector3.zero,false);
    }
    private static void Frond(Transform group,Vector3 at,float angle,float size,Material material)
    {
        var mesh=new RealmMeshes();AddFrond(mesh,at,angle,size,Range(0,1));Part("Leaf",group,mesh.Build("Leaf"),material,Vector3.zero,false);
    }
    private static void AddFrond(RealmMeshes mesh,Vector3 at,float angle,float size,float piece)
    {
        Vector3 outward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),side=Vector3.Cross(Vector3.up,outward);
        var rows=new List<Vector3[]>();
        for(int i=0;i<=12;i++)
        {
            float t=i/12f;var row=new Vector3[5];
            for(int j=0;j<5;j++) {float u=j/4f*2-1;row[j]=at+outward*(t*t*size*0.7f)+Vector3.up*(Mathf.Sin(t*1.9f)*size-Mathf.Abs(u)*0.15f*size)+side*(u*Mathf.Sin(t*Mathf.PI)*size*0.2f);}
            rows.Add(row);
        }
        int start=mesh.VertexCount;
        for(int i=0;i<rows.Count;i++)for(int j=0;j<5;j++)
            mesh.Vertex(rows[i][j],Vector3.up,new Vector2(i/12f,piece),new Vector2(j/4f*2f-1f,i/12f*size));
        for(int i=0;i<12;i++)for(int j=0;j<4;j++)
        {
            int a=start+i*5+j,b=a+1,c=a+5,d=c+1;
            Vector3 normal=Vector3.Cross(rows[i][j+1]-rows[i][j],rows[i+1][j]-rows[i][j]);
            if(normal.y>=0){mesh.Triangle(a,b,c);mesh.Triangle(b,d,c);}else{mesh.Triangle(a,c,b);mesh.Triangle(b,c,d);}
        }
    }
    private static void Canopy(Transform group,Vector2 p,float height)
    {
        Vector3 at=Ground(p.x,p.y);var stem=new RealmMeshes();
        stem.Tube(Enumerable.Range(0,17).Select(i=>at+new Vector3(Mathf.Sin(i/16f*2)*1.5f,i/16f*height,0)).ToArray(),t=>Mathf.Lerp(0.22f,0.1f,t),7,0,1,Range(0,1));
        Part("Tall bubble flower canopy — stem",group,stem.Build("Canopy stalk"),Mat("Assets/Materials/World/MAT_Realm_Root.mat"),Vector3.zero,true);
        Vector3 head=at+new Vector3(1.36f,height,0);
        for(int i=0;i<5;i++)Frond(group,head-Vector3.up*0.7f,i*Mathf.PI*2/5f,3.6f,growth);
        var pearl=new RealmMeshes();pearl.Sphere(Vector3.zero,Vector3.one*Range(1.1f,1.8f),Quaternion.identity,14,22,0.5f,0.7f);
        Part("Iridescent canopy pearl",group,pearl.Build("Canopy pearl"),Mat("Assets/Materials/HabitatBlock/HB_Bubble.mat"),head,false);
    }
    private static void Stream(Transform group,Vector2[] path,float width,Material material,string name,float? fixedY=null)
    {
        var rows=new List<Vector3[]>();
        for(int i=0;i<path.Length-1;i++)
        for(int j=0;j<18;j++)
        {
            Vector2 p=Vector2.Lerp(path[i],path[i+1],j/17f),tangent=(path[i+1]-path[i]).normalized,side=new Vector2(tangent.y,-tangent.x);
            var row=new Vector3[9];
            for(int k=0;k<9;k++){Vector2 q=p+side*((k/8f*2-1)*width);row[k]=new Vector3(q.x,fixedY??(LeafMapTerrain.Height(q.x,q.y)+0.09f),q.y);}
            rows.Add(row);
        }
        var mesh=new RealmMeshes();mesh.Grid(rows,0.6f);Part(name,group,mesh.Build(name),material,Vector3.zero,false);
    }
    private static Transform Rock(Transform group,Vector3 at,Vector3 size,Material material,string name)
    {
        var mesh=new RealmMeshes();mesh.Sphere(Vector3.zero,size,Quaternion.Euler(0,Range(0,360),0),6,10,0.5f,Range(0,1));
        return Part(name,group,mesh.Build(name),material,at,true).transform;
    }
    private static GameObject Part(string name,Transform parent,Mesh mesh,Material material,Vector3 at,bool solid)
    {
        string path=Folder+"/Mesh_"+(meshId++).ToString("D3")+".asset";mesh.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(mesh,path);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.transform.position=at;
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
        if(solid){go.layer=7;go.tag="Ground";go.AddComponent<MeshCollider>().sharedMesh=mesh;}
        return go;
    }
    private static void Lamp(Transform group,Vector3 at,Color color,float intensity,float range)
    {
        var light=Group("Reflected habitat light",group).gameObject.AddComponent<Light>();light.transform.position=at;light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;
    }
    private static void Cameras()
    {
        var views=Group("08 Review cameras — disabled during play");
        View("01_Layout",new Vector3(0,310,-10),new Vector3(0,0,-10),true);
        View("02_Overview",new Vector3(220,225,-285),new Vector3(0,3,-3));
        View("03_CaveExit",new Vector3(0,6,-101),new Vector3(0,24,2));
        View("04_FlowerMeadow",new Vector3(-30,10,-89),new Vector3(-2,15,-40));
        View("05_WestCliffs",new Vector3(-94,49,-29),new Vector3(-44,30,37));
        View("06_BonesAndRift",new Vector3(102,42,-20),new Vector3(44,5,39));
        void View(string name,Vector3 at,Vector3 target,bool top=false)
        {
            var go=Group(name,views).gameObject;go.transform.position=at;
            go.transform.rotation=top ? Quaternion.Euler(90,0,0) : Quaternion.LookRotation(target-at);
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=name=="03_CaveExit"?66:52;
            camera.nearClipPlane=0.15f;camera.farClipPlane=800;camera.allowHDR=true;
            camera.orthographic=top;camera.orthographicSize=160;camera.clearFlags=top?CameraClearFlags.SolidColor:CameraClearFlags.Skybox;
            camera.backgroundColor=new Color(0.11f,0.14f,0.18f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        }
    }

    [MenuItem("Tools/Capstone/Leaf Map/3 Capture Review Views")]
    public static void Capture()
    {
        Scene scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);Preview();Directory.CreateDirectory(Captures);
        bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var cameras=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Where(c=>c.transform.parent!=null && c.transform.parent.name=="08 Review cameras — disabled during play").ToArray();
        var target=new RenderTexture(1600,1200,24,RenderTextureFormat.ARGB32){antiAliasing=4};var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        RenderTexture old=RenderTexture.active;
        bool fog=RenderSettings.fog;
        // The art-reference scene can stay open beside this one without entering the review renders.
        var renderObjects=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Select(r=>r.gameObject).Distinct().ToDictionary(g=>g,g=>g.layer);
        foreach(var go in renderObjects.Keys)go.layer=29;
        try
        {
            foreach(var camera in cameras)
            {
                int mask=camera.cullingMask;camera.cullingMask=1<<29;
                RenderSettings.fog=camera.name=="01_Layout" || camera.name=="02_Overview" ? false : fog;
                camera.scene=scene;camera.targetTexture=target;camera.aspect=4f/3f;camera.Render();camera.Render();
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();
                File.WriteAllBytes(Captures+"/"+camera.name+".png",texture.EncodeToPNG());camera.targetTexture=null;camera.scene=default;camera.cullingMask=mask;
            }
            var diagnostics=new List<string>();
            foreach(var shader in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(m=>m.shader).Distinct())
                foreach(var message in ShaderUtil.GetShaderMessages(shader))
                    if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)diagnostics.Add(shader.name+": "+message.message);
            File.WriteAllText(Captures+"/Validation.txt",Describe()+"\nShader errors: "+diagnostics.Count+"\n"+string.Join("\n",diagnostics));
            if(diagnostics.Count>0)throw new InvalidOperationException(string.Join("\n",diagnostics));
        }
        finally {foreach(var pair in renderObjects)if(pair.Key!=null)pair.Key.layer=pair.Value;RenderSettings.fog=fog;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);ShaderUtil.allowAsyncCompilation=async;}
    }
    [MenuItem("Tools/Capstone/Leaf Map/Refine Leaf Colour And Route Readability")]
    public static void Refine()
    {
        Scene scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("Open the leaf scene first.");
        root=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        world=FindInScene<ProceduralWorld>(scene);random=new System.Random(221026);
        Preview();
        meshId=Directory.GetFiles(Folder,"Mesh_*.asset").Select(p=>int.Parse(Path.GetFileNameWithoutExtension(p).Substring(5))).DefaultIfEmpty(-1).Max()+1;
        Material water=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Leaf_CyanVein.mat");
        if(water==null)water=Variant("Assets/Materials/World/MAT_Realm_Stream.mat","Leaf_CyanVein",m=> {
            m.SetColor("_ColorA",new Color(0.035f,0.34f,0.42f));m.SetColor("_ColorB",new Color(0.015f,0.11f,0.18f));
            m.SetColor("_ColorC",new Color(0.45f,0.92f,1));m.SetColor("_ColorD",new Color(0.05f,0.56f,0.72f));
            m.SetColor("_ColorE",new Color(0.008f,0.04f,0.07f));m.SetColor("_VeinColor",new Color(0.6f,1.5f,1.7f));m.SetFloat("_FlowSpeed",0.25f);
        });
        foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh=mf.sharedMesh;if(mesh==null || !AssetDatabase.GetAssetPath(mesh).StartsWith(Folder))continue;
            if(mf.name=="Leaf" || mf.name=="Fronds — open route between clusters")
            {
                var a=mesh.uv;var b=new List<Vector2>();mesh.GetUVs(1,b);
                var lengths=a.Select((v,i)=>new {key=v.y,length=b[i].y}).GroupBy(v=>v.key).ToDictionary(g=>g.Key,g=>g.Max(v=>v.length));
                for(int i=0;i<a.Length;i++)a[i].x=b[i].y/Mathf.Max(0.01f,lengths[a[i].y]);
                mesh.uv=a;EditorUtility.SetDirty(mesh);
            }
            if(mf.name=="Meadow runnel" || mf.name.StartsWith("Cyan time") || mf.name.StartsWith("Cyan thread") || mf.name.StartsWith("Time vein"))mf.GetComponent<Renderer>().sharedMaterial=water;
        }
        Material pathMat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Leaf_PathStone.mat");
        if(pathMat==null)pathMat=Variant(Folder+"/Leaf_InkStone.mat","Leaf_PathStone",m=> {
            m.SetColor("_Color",new Color(0.39f,0.43f,0.44f));m.SetColor("_LineColor",new Color(0.46f,0.56f,0.57f));m.SetColor("_LineColor2",new Color(0.46f,0.56f,0.57f));m.SetFloat("_FloorFreq",1.3f);
        });
        if(root.Find("Leaf veins — readable walking routes")==null)
        {
            var routes=Group("Leaf veins — readable walking routes");
            foreach(var route in new[]{LeafMapTerrain.MainRoute,LeafMapTerrain.WestRoute,LeafMapTerrain.EastRoute,LeafMapTerrain.NorthRoute})
                Stream(routes,route.Select(v=>new Vector2(v.x,v.z)).ToArray(),2f,pathMat,"Walkable leaf vein");
        }
        ecology=root.GetComponent<LeafMapEcology>();
        foreach(var tree in root.GetComponentsInChildren<SquirrelTree>())
        {
            var p=tree.transform.position;p.y=LeafMapTerrain.Height(p.x,p.z)+3f;tree.transform.position=p;
        }
        var seedGrove=root.Find("02b Seed gatherers — meadow ecology");
        if(seedGrove!=null)foreach(var leaf in seedGrove.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Leaf"))
            leaf.transform.position=Vector3.up*3f;
        if(root.Find("Rising fire — initial catchable bubbles")==null)
        {
            var group=Group("Rising fire — initial catchable bubbles");
            for(int i=0;i<3;i++)
            {
                float t=0.3f+i*0.17f;
                var at=Vector3.Lerp(new Vector3(80,0,23),new Vector3(56,0,88),t);at.y=-5+i*9;
                var fire=Prefab(Folder+"/RiftFireBubble.prefab",group,at,"Ascending fire bubble "+(i+1)).GetComponent<FireBubble>();
                ecology.RegisterRiftBubble(fire);
            }
        }
        var overview=root.GetComponentsInChildren<Camera>(true).First(c=>c.name=="02_Overview");
        overview.transform.position=new Vector3(170,190,-205);overview.transform.LookAt(new Vector3(0,4,-15));overview.fieldOfView=48;
        overview.clearFlags=CameraClearFlags.SolidColor;
        if(!scene.GetRootGameObjects().Any(g=>g.name=="Leave tutorial — contextual hints"))
        {
            var trigger=Prefab("Assets/Prefabs/Gameplay/InteractionHintsTrigger.prefab",null,new Vector3(0,5,-101),"Leave tutorial — contextual hints");
            trigger.GetComponent<BoxCollider>().size=new Vector3(22,12,4);
            Edit(trigger.GetComponent<InteractionHintsTrigger>(),s=>Ref(s,"hud",FindInScene<InteractionPromptHUD>(scene)));
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);ValidateGeometry();
    }
    private static void ValidateGeometry()
    {
        Physics.SyncTransforms();
        Scene scene=world.gameObject.scene;
        foreach(var mf in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)))
        {
            if(mf.sharedMesh==null)continue;
            if(!mf.sharedMesh.isReadable)continue;
            foreach(var v in mf.sharedMesh.vertices)if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))throw new InvalidOperationException("Non-finite vertex in "+mf.name);
        }
        if(!world.GenerationComplete)throw new InvalidOperationException("Terrain not ready.");
        foreach(var p in LeafMapTerrain.MainRoute)
            if(!Physics.Raycast(new Vector3(p.x,LeafMapTerrain.Height(p.x,p.z)+1.7f,p.z),Vector3.down,out var hit,5,1<<7) || hit.normal.y<0.65f)throw new InvalidOperationException("Main route lacks walkable ground at "+p);
    }
    private static string Describe()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        var roots=scene.GetRootGameObjects();
        return "Scene: "+ScenePath+"\nCreature count: "+roots.Sum(g=>g.GetComponentsInChildren<Creature>(true).Length)+"\nMesh renderers: "+roots.Sum(g=>g.GetComponentsInChildren<MeshRenderer>(true).Length)+"\nInteractive bubble flowers: "+roots.Sum(g=>g.GetComponentsInChildren<BubbleFlower>(true).Length)+"\nPlantable seeds: "+roots.Sum(g=>g.GetComponentsInChildren<PlantableBubbleSeed>(true).Length);
    }
    [MenuItem("Tools/Capstone/Leaf Map/Open And Frame Saved Leaf Map")]
    public static void Review()
    {
        Scene scene=SceneManager.GetSceneByPath(ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        for(int i=SceneManager.sceneCount-1;i>=0;i--)
        {
            var other=SceneManager.GetSceneAt(i);
            if(other!=scene && !other.isDirty)EditorSceneManager.CloseScene(other,true);
        }
        Preview();
        root=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        var carrier=FindInScene<PlayerCreatureCarrier>(scene);
        if(carrier!=null)foreach(var camera in carrier.transform.root.GetComponentsInChildren<Camera>(true))
        {
            camera.clearFlags=CameraClearFlags.Skybox;
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        }
        if(root.Find("01 Tutorial — original YoYoTutorialFall cave")!=null)
        {
            var legacy=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Leave tutorial — contextual hints");
            if(legacy!=null)legacy.SetActive(false);
        }
        Selection.activeGameObject=root.gameObject;
        var view=SceneView.lastActiveSceneView;
        if(view!=null)view.LookAt(new Vector3(0,8,-6),Quaternion.Euler(62,-18,0),170);
        EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Captures);
        File.WriteAllText(Captures+"/EditorState.txt",string.Join("\n",Enumerable.Range(0,SceneManager.sceneCount).Select(i=> {
            var s=SceneManager.GetSceneAt(i);return s.path+" / dirty: "+s.isDirty+" / active: "+(s==SceneManager.GetActiveScene());
        })));
    }
    private static T FindInScene<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).FirstOrDefault();
    private static Vector3 Ground(float x,float z,float offset=0) => new Vector3(x,LeafMapTerrain.Height(x,z)+offset,z);
    private static Transform Group(string name,Transform parent=null){var go=new GameObject(name);go.transform.SetParent(parent??root,false);return go.transform;}
    private static float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    private static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path)??throw new InvalidOperationException("Missing material "+path);
    private static Material Variant(string path,string name,Action<Material> tune){var m=new Material(Mat(path)){name=name};tune(m);AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;}
    private static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    private static GameObject Prefab(string path,Transform parent,Vector3 at,string name)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(path)??throw new InvalidOperationException("Missing prefab "+path);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.transform.SetParent(parent,false);go.transform.position=at;go.name=name;return go;
    }
    private static GameObject Spawn(string kind,Transform parent,Vector3 at,string name)
    {
        var go=Prefab("Assets/Prefabs/Creatures/"+kind+".prefab",parent,at,name);var c=go.GetComponent<Creature>();if(c!=null)Edit(c,s=>Float(s,"ageRate",0));return go;
    }
    private static GameObject Clone(GameObject source,Transform parent,Vector3 at,string name){var go=Object.Instantiate(source,parent);go.name=name;go.transform.SetPositionAndRotation(at,Quaternion.identity);go.SetActive(true);return go;}
    private static void Edit(Object o,Action<SerializedObject> change){var s=new SerializedObject(o);change(s);s.ApplyModifiedPropertiesWithoutUndo();}
    private static void Float(SerializedObject s,string n,float v)=>s.FindProperty(n).floatValue=v;
    private static void Int(SerializedObject s,string n,int v)=>s.FindProperty(n).intValue=v;
    private static void Bool(SerializedObject s,string n,bool v)=>s.FindProperty(n).boolValue=v;
    private static void Ref(SerializedObject s,string n,Object v)=>s.FindProperty(n).objectReferenceValue=v;
}
