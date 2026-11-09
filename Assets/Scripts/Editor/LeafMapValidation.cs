using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Exercise the scene's real terrain, planting ownership, nest storage and ambient ecology in Play Mode.</summary>
[InitializeOnLoad]
public static class LeafMapValidation
{
    private const string Running="Capstone.LeafMap.Validation";
    private const string Quick="Capstone.LeafMap.Validation.Quick";
    private static readonly List<string> results=new List<string>();
    private static float started;
    private static bool prepared;
    private static double nextStatus;
    private static Vector3[] initialSquirrels;
    private static Scene scene;
    static LeafMapValidation()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=state=> {
            if(!SessionState.GetBool(Running,false))return;
            File.AppendAllText("Temp/LeafMap.validation-state",DateTime.Now+" / "+state+"\n");
            if(state==PlayModeStateChange.EnteredPlayMode) {prepared=false;results.Clear();started=Time.time;Application.runInBackground=true;}
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Running,false);
                File.WriteAllText("ArtCaptures/LeafMap/PlayValidation.txt","INTERRUPTED: Play Mode stopped before the ecology observation completed.\n"+string.Join("\n",results));
            }
        };
    }
    [MenuItem("Tools/Capstone/Leaf Map/4 Validate Planting And Ecology In Play Mode")]
    public static void Run()
    {
        SessionState.SetBool(Quick,false);Begin();
    }
    public static void RunQuick()
    {
        SessionState.SetBool(Quick,true);Begin();
    }
    private static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Leave Play Mode first.");
        scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("Open Scene_LeafMap first.");
        File.WriteAllText("Temp/LeafMap.validation-state","Requested "+DateTime.Now+"\n");
        SessionState.SetBool(Running,true);EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(SessionState.GetBool(Running,false) && EditorApplication.timeSinceStartup>=nextStatus)
        {
            nextStatus=EditorApplication.timeSinceStartup+3;
            File.AppendAllText("Temp/LeafMap.validation-state",DateTime.Now+" / playing="+EditorApplication.isPlaying+" / paused="+EditorApplication.isPaused+" / time="+Time.time+" / prepared="+prepared+"\n");
        }
        if(!SessionState.GetBool(Running,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling)return;
        try
        {
            scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
            if(!scene.IsValid() || !scene.isLoaded)throw new InvalidOperationException("Leaf scene did not enter Play Mode.");
            if(!prepared)
            {
                var world=Find<ProceduralWorld>();
                if(world==null || !world.GenerationComplete) {if(Time.time-started>90)throw new InvalidOperationException("Terrain generation timed out.");return;}
                // Other open review scenes stay unchanged in Edit Mode and cannot affect the runtime check.
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    var other=SceneManager.GetSceneAt(i);if(other==scene)continue;
                    foreach(var root in other.GetRootGameObjects())root.SetActive(false);
                }
                var manager=Find<SquirrelSystemManager>();manager.InitializeSystem();
                initialSquirrels=manager.Squirrels.Select(s=>s.transform.position).ToArray();
                TutorialAndLayout();CarriedFlowers();Planting();prepared=true;started=Time.time;
                if(SessionState.GetBool(Quick,false))
                {
                    typeof(LeafMapEcology).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(Find<LeafMapEcology>(),null);
                    var family=Find<BoneKiln>().Root.GetComponentsInChildren<BoneChild>();
                    Check(family.Length==3 && family.All(c=>Vector3.Distance(c.transform.position,Find<BoneKiln>().transform.position)<32),"Three runtime bone children rest beside the mother and flower field");
                    var quickStones=All<MeshCollider>().Where(c=>c.name.Contains("floating stepping stone")).ToArray();
                    Check(quickStones.Length>=6 && quickStones.All(c=>c.enabled && c.gameObject.layer==7),"Floating stones retain ground collision");
                    Check(Find<BoneKiln>().Root!=null && Find<HangingSpire>().Built,"Bone mother and central tower build");
                    Check(Find<PlayerCreatureCarrier>().PlayerController.PlayerCamera.clearFlags==CameraClearFlags.Skybox,"Player camera displays the selected skybox");
                    CapturePlayViews();Finish(null);return;
                }
                return;
            }
            if(Time.time-started<27)return;
            var squirrels=Find<SquirrelSystemManager>();
            Check(squirrels.IsInitialized && squirrels.Squirrels.Count==3,"Three squirrels initialize with separate homes");
            Check(squirrels.Squirrels.Select((s,i)=>Vector3.Distance(s.transform.position,initialSquirrels[i])).Any(d=>d>0.5f),"Squirrels move through the flower meadow");
            Check(squirrels.Foods.Any(f=>f!=null && (f.IsStored || f.Owner!=null)),"Squirrels reserve or store the plantable seeds");
            foreach(var nest in squirrels.Nests)
                Check(nest.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y)>LeafMapTerrain.Height(nest.transform.position.x,nest.transform.position.z)-0.4f,"Nest rests above terrain: "+nest.name);
            Check(Find<LeafMapEcology>().RisingFireCount>0,"The cliff vent emits fire bubbles");
            var terrain=Find<ProceduralWorld>();
            foreach(var fire in All<FireBubble>().Where(f=>!f.IsBoundToBone && !f.IsCarried && !f.IsLeashed))
                Check(terrain.SampleDensity(fire.transform.position)<0,"Fire bubble remains outside rock: "+fire.name);
            for(int i=2;i<9 && LeafMapTerrain.HasRift;i++)
            {
                float t=i/10f;Vector3 p=LeafMapTerrain.RiftPoint(t);p.y=Mathf.Max(LeafMapTerrain.RiftFloor(t),-30f)+3;
                Check(Physics.Raycast(p,Vector3.down,60,1<<7),"Solid canyon floor at "+t);
            }
            var stones=All<MeshCollider>().Where(c=>c.name.Contains("floating stepping stone")).ToArray();
            if(!LeafMapTerrain.HasRift)stones=new MeshCollider[0];
            else Check(stones.Length>=6,"Floating stone traversal nodes are present: "+stones.Length);
            foreach(var stone in stones)
            {
                Check(stone.gameObject.layer==7 && stone.enabled,"Floating stone has ground collision: "+stone.name);
                Check(terrain.SampleDensity(stone.bounds.center)<0,"Floating stone stands clear of terrain: "+stone.name);
                Check(stone.Raycast(new Ray(stone.bounds.center+Vector3.up*4,Vector3.down),out var hit,8) && hit.normal.y>0.7f,"Floating stone has a walkable top: "+stone.name);
            }
            Check(Find<BoneKiln>().Root!=null,"The bone mother builds beside the eastern flower meadow");
            Check(Find<HangingSpire>().Built,"The central three-stage tower builds");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
            foreach(var flower in All<BubbleFlower>())
            {
                Check(flower.transform.Find("Pearl bloom petals")==null,"Replacement flower crown removed: "+flower.name);
                if(!flower.IsCarried && !flower.IsBubbleCaptured)
                    Check(flower.transform.position.y>LeafMapTerrain.Height(flower.transform.position.x,flower.transform.position.z)-0.5f,"Flower settled above the rolling ground: "+flower.name);
                for(int petal=0;petal<4;petal++)
                {
                    string name="Petal "+petal;var part=flower.transform.Find(name);var source=original.transform.Find(name);
                    Check(part!=null && part.GetComponent<Renderer>().enabled && part.GetComponent<MeshFilter>().sharedMesh==source.GetComponent<MeshFilter>().sharedMesh &&
                        part.GetComponent<Renderer>().sharedMaterial==source.GetComponent<Renderer>().sharedMaterial,"Original petal mesh and material: "+flower.name+" / "+name);
                }
            }
            Check(All<TentacleCreature>().Count(t=>t.isActiveAndEnabled)>=6,"Cliff and bone-approach tentacles are active");
            foreach(var body in All<Rigidbody>())Check(float.IsFinite(body.position.x)&&float.IsFinite(body.position.y)&&float.IsFinite(body.position.z),"Finite body: "+body.name);
            CapturePlayViews();
            Finish(null);
        }
        catch(Exception e){Finish(e);}
    }
    private static void Planting()
    {
        var carrier=Find<PlayerCreatureCarrier>();
        var seed=All<PlantableBubbleSeed>().First(s=>s.GetComponent<SquirrelFood>().IsAvailable);
        var food=seed.GetComponent<SquirrelFood>();
        carrier.PlayerBody.position=LeafMapTerrain.ToWorld(new Vector3(-12,6,-52));carrier.transform.position=carrier.PlayerBody.position;Physics.SyncTransforms();
        Check(carrier.TryCarryFromInteraction(food),"Player can pick up a plantable seed");
        Check(!seed.TryPlant(carrier,new Ray(LeafMapTerrain.ToWorld(new Vector3(-12,8,-50)),Vector3.up)) && carrier.CarriedCreature==food,"Invalid planting preserves held seed");
        int before=All<BubbleFlower>().Count();
        Check(seed.TryPlant(carrier,new Ray(LeafMapTerrain.ToWorld(new Vector3(-12,7,-50)),Vector3.down)),"Aimed ground planting succeeds");
        Check(carrier.CarriedCreature==null && All<BubbleFlower>().Count()==before+1,"Planting spends exactly one seed and creates one flower");
        var second=All<PlantableBubbleSeed>().First(s=>s.gameObject.activeSelf && s.GetComponent<SquirrelFood>().IsAvailable);
        Check(carrier.TryCarryFromInteraction(second.GetComponent<SquirrelFood>()),"Player picks up a second seed");
        Check(!second.TryPlant(carrier,new Ray(LeafMapTerrain.ToWorld(new Vector3(-12,7,-50)),Vector3.down)),"Planting rejects overlapping flowers");
        var nest=Find<SquirrelNest>();
        Check(nest.TryPlayerDeposit(carrier,second.GetComponent<SquirrelFood>()),"Plantable seed can be stored in a squirrel nest");
        Check(nest.TryWithdraw(nest.transform.position+Vector3.up,out var withdrawn) && withdrawn.GetComponent<PlantableBubbleSeed>()!=null,"Withdrawing from nest preserves planting ability");
    }
    private static void TutorialAndLayout()
    {
        var terrain=Find<ProceduralWorld>();
        var spawn=Find<PlayerCreatureCarrier>().PlayerBody.position;
        Check(Vector3.Distance(spawn,new Vector3(-112,-27.8f,-136)+LeafMapTutorialGraft.Offset)<3,"Player starts in the original underground tutorial");
        Check(terrain.SampleDensity(spawn)<0 && Physics.Raycast(spawn,Vector3.down,5,1<<7),"Tutorial spawn has air and a solid floor");
        Check(All<BounceShroom>().Count(s=>s.name.StartsWith("BS — cave practice"))==3,"All three original tutorial practice mushrooms are preserved");
        var route=YoYoTutorialFallFlow.ExitRoute;
        for(int i=1;i<route.Length;i++)for(int j=0;j<=8;j++)
        {
            Vector3 floor=Vector3.Lerp(route[i-1],route[i],j/8f)+LeafMapTutorialGraft.Offset;
            Check(terrain.SampleDensity(floor+Vector3.up*1.8f)<0,"Tutorial exit head clearance "+i+" / "+j);
            Check(Physics.Raycast(floor+Vector3.up*2,Vector3.down,out var hit,5,1<<7) && terrain.OwnsCollider(hit.collider),"Continuous original exit floor "+i+" / "+j);
        }
        Vector2 basin=LeafMapTerrain.ToWorld(new Vector2(-8,-61));
        Check(LeafMapTerrain.MainRoute[0].y-LeafMapTerrain.Height(basin.x,basin.y)>4,"Tutorial exit overlooks the lower flower basin");
        for(int x=-64;x<=-50;x+=7)for(int z=8;z<=22;z+=7)
            Check(Mathf.Abs(LeafMapTerrain.Height(LeafMapTerrain.ToWorld(new Vector2(x,z)).x,LeafMapTerrain.ToWorld(new Vector2(x,z)).y)-29)<1,"Walkable west highland plateau "+x+" / "+z);
        Check(RenderSettings.skybox==AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("02ec9149f3b94035aff10c37f9eaf9a3")),"Exact Scene_Daniel skybox is assigned");
        var black=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HabitatBlock/HB_Ground.mat");
        var leafInk=AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/LeafMap/Leaf_InkStone.mat");
        var innerWorld=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        Check(terrain.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterials.All(m=>m==black || m==leafInk || m==innerWorld)),"Terrain uses the black ground above and Scene_Daniel's inner world below");
        // The bubble groves emit on purpose (bubble hopping); the rest keep the sparse pattern.
        var flowers=All<BubbleFlower>().Where(f=>f.transform.parent==null || !f.transform.parent.name.StartsWith("12 Bubble") && !f.transform.parent.name.StartsWith("13 Field")).ToArray();int emitters=flowers.Count(f=>f.AutoEmitOnGround);
        Check(emitters>0 && emitters<=Mathf.CeilToInt(flowers.Length*.3f),"Only a quarter of interactive flowers automatically emit: "+emitters+" / "+flowers.Length);
    }
    private static void CarriedFlowers()
    {
        var carrier=Find<PlayerCreatureCarrier>();var camera=carrier.PlayerController.PlayerCamera;
        carrier.PlayerBody.position=new Vector3(0,40,-60);carrier.transform.position=carrier.PlayerBody.position;
        camera.transform.SetPositionAndRotation(new Vector3(0,42,-60),Quaternion.identity);
        foreach(bool automatic in new[]{false,true})
        {
            var flower=All<BubbleFlower>().First(f=>f.AutoEmitOnGround==automatic);
            Vector3 old=flower.transform.position;
            flower.AmbientEmissionAllowed=false;
            Check(carrier.TryCarryFromInteraction(flower),"Player picks up "+(automatic?"automatic":"quiet")+" flower");
            Physics.SyncTransforms();
            flower.ReceiveInteraction(new CreatureInteraction(carrier.gameObject,Vector3.zero,Vector3.forward));
            Check(flower.LastShot!=null && !flower.LastShot.IsPopped,"Carried flower shoots even when ambient production is paused: "+automatic);
            Object.Destroy(flower.LastShot.gameObject);carrier.Release();
            flower.transform.position=old;flower.GetComponent<Rigidbody>().position=old;Physics.SyncTransforms();
            flower.AmbientEmissionAllowed=true;
            var previous=flower.LastShot;
            typeof(BubbleFlower).GetMethod("TickCreature",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(flower,new object[]{25f});
            Check((flower.LastShot!=previous)==automatic,"Ground emission respects the quiet/automatic setting: "+automatic);
            if(automatic && flower.LastShot!=null)Object.Destroy(flower.LastShot.gameObject);
        }
    }
    private static void Check(bool pass,string label){if(!pass)throw new InvalidOperationException(label);results.Add("PASS: "+label);}
    private static void CapturePlayViews()
    {
        var target=new RenderTexture(1600,1200,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try
        {
            foreach(string name in new[]{"04_FlowerMeadow","09_RiftInterior"})
            {
                var camera=All<Camera>().First(c=>c.name==name);var before=camera.targetTexture;
                try
                {
                    camera.targetTexture=target;camera.aspect=4f/3f;camera.Render();camera.Render();RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();
                    File.WriteAllBytes("ArtCaptures/LeafMap/Play_"+name.Substring(3)+".png",texture.EncodeToPNG());
                }
                finally {camera.targetTexture=before;}
            }
        }
        finally {RenderTexture.active=old;target.Release();Object.Destroy(target);Object.Destroy(texture);}
    }
    private static IEnumerable<T> All<T>() where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(false));
    private static T Find<T>() where T:Component => All<T>().FirstOrDefault();
    private static void Finish(Exception error)
    {
        SessionState.SetBool(Running,false);
        Directory.CreateDirectory("ArtCaptures/LeafMap");
        File.WriteAllText("ArtCaptures/LeafMap/PlayValidation.txt",(error==null?"PASS":"FAIL: "+error)+
            (SessionState.GetBool(Quick,false)?"\nMode: tutorial geometry, selective emission, carried shooting, planting and scene collision (short check).":"\nMode: full ecology observation.")+"\n"+string.Join("\n",results));
        if(error!=null)Debug.LogException(error);
        // Release food before hierarchy deactivation; teardown must not reparent an inactive squirrel's children.
        if(scene.IsValid() && scene.isLoaded)foreach(var food in All<SquirrelFood>().ToArray())if(food.Owner!=null)food.Release(food.Owner);
        EditorApplication.ExitPlaymode();
    }
}
