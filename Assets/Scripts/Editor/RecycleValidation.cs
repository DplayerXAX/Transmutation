using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Small deterministic fixtures. No real-time ageing waits, generation loops, or ecology soak tests.</summary>
public static class RecycleValidation
{
    public static string TraversalStatus { get; private set; } = "Not run";
    public static string PerformanceStatus { get; private set; } = "Not run";
    static int checks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value,string message)
    { if(!value)throw new InvalidOperationException("Recycle: "+message);checks++; }
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);
    static void Age(Creature target,float seconds) => typeof(Creature).GetMethod("SetRemainingAge",Private).Invoke(target,new object[]{seconds});
    static bool Feed(RecycleFlower flower,Creature target,Vector3 point) => (bool)typeof(RecycleFlower).GetMethod("TryFeedOrJolt",Private).Invoke(flower,new object[]{target,point});
    public static string ValidateCollapsedSurfaceSeams()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Run the seam fixture in Edit Mode.");
        Scene fixture=UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
        var meshes=new List<Mesh>();
        try
        {
            for(int i=0;i<2;i++)
            {
                var mesh=new Mesh{name="Surface seam fixture"};meshes.Add(mesh);
                mesh.vertices=new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
                mesh.triangles=i==0 ? new[]{0,2,1,0,3,2,0,2,2,2,0,0} : new[]{0,2,1,0,3,2,0,2,1};
                mesh.RecalculateBounds();
                var go=new GameObject("Seam fixture");var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
                if(!SurfaceGeometry.Acquire(collider,new Vector3(-0.5f,0,0.2f),Vector3.up,out var start))
                    throw new InvalidOperationException("Cannot acquire seam fixture.");
                bool crossed=SurfaceGeometry.Trace(start,Vector3.right,1f,out var end);
                if(i==0 && (!crossed || !end.Resolve(out var p,out _) || Vector3.Distance(p,new Vector3(0.5f,0,0.2f))>0.001f))
                    throw new InvalidOperationException("Collapsed triangles blocked a continuous surface.");
                if(i==1 && crossed)throw new InvalidOperationException("A genuinely ambiguous edge was crossed.");
            }
            return "PASS: collapsed triangles do not block growth; genuinely ambiguous edges still stop it.";
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(fixture,true);
            foreach(var mesh in meshes)Object.DestroyImmediate(mesh);
        }
    }
    public static string ValidateAssets()
    {
        checks=0;
        var colony=AssetDatabase.LoadAssetAtPath<CompostCreature>(CompostCreatureSetup.PrefabPath);
        var flower=AssetDatabase.LoadAssetAtPath<RecycleFlower>(RecycleSetup.FlowerPath);
        Check(colony!=null && flower!=null,"colony and flower assets exist");
        Check(!colony.CanBeCarried && !colony.CanBeCaptured,"network is rooted");
        Check(colony.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"old platform collision is disabled");
        Check(new SerializedObject(colony).FindProperty("flowerPrefab").objectReferenceValue==flower,"colony produces the portable flower");
        Check(flower.SeedPrefab==colony,"flower seeds the existing colony prefab");
        Check(flower.GetComponent<RecycleFlowerVisual>().Model.vertexCount>500,"flower has curved procedural geometry");
        Check(flower.GetComponent<RecycleFlowerVisual>().Model.normals.All(n=>n.sqrMagnitude>0.5f),"procedural flower normals are valid, including petal tips");
        foreach(string name in new[]{"Transmutation/Surface Goo","Transmutation/Recycle Organic"})
        {
            Shader shader=Shader.Find(name);Check(shader!=null,"shader exists: "+name);
            Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"shader compiles: "+name);
        }
        return "PASS: "+checks+" Recycle asset and shader checks.";
    }

    [MenuItem("Tools/Creatures/Recycle/Validate Focused Gameplay (Play Mode)")]
    public static void Validate() => UnityEngine.Debug.Log(Run());
    public static string Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        checks=0;var watch=Stopwatch.StartNew();
        Scene fixture=SceneManager.CreateScene("Recycle focused validation "+Guid.NewGuid(),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var stage=new GameObject("Disposable Recycle fixtures");SceneManager.MoveGameObjectToScene(stage,fixture);
        var createdMeshes=new List<Mesh>();
        var sceneExtras=new List<GameObject>();
        try
        {
            Vector3 offset=new Vector3(1500f,0f,1500f);
            GameObject MeshObject(string name,Vector3[] vertices,int[] triangles,Vector3 position)
            {
                var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();createdMeshes.Add(mesh);
                var go=new GameObject(name);go.layer=7;go.transform.SetParent(stage.transform);go.transform.position=position;go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
            }
            Vector3[] bend={new Vector3(-2,0,-2),new Vector3(0,0,-2),new Vector3(0,0,2),new Vector3(-2,0,2),new Vector3(0,2,-2),new Vector3(0,2,2)};
            var bent=MeshObject("Floor wall corner",bend,new[]{0,2,1,0,3,2,1,2,4,2,5,4},offset);
            Physics.SyncTransforms();
            Check(SurfaceGeometry.Acquire(bent.GetComponent<Collider>(),offset+new Vector3(-0.6f,0,0),Vector3.up,out var start),"acquire floor triangle");
            Check(SurfaceGeometry.Trace(start,Vector3.right,1.2f,out var turned) && turned.Resolve(out var end,out var normal) &&
                Vector3.Distance(end,offset+new Vector3(0,0.6f,0))<0.015f && Vector3.Dot(normal,Vector3.left)>0.99f,"trace follows a concave floor-to-wall corner");
            Vector3[] convex=(Vector3[])bend.Clone();convex[4].y=-2;convex[5].y=-2;
            var outside=MeshObject("Convex ledge",convex,new[]{0,2,1,0,3,2,1,2,4,2,5,4},offset+Vector3.forward*8);
            Check(SurfaceGeometry.Acquire(outside.GetComponent<Collider>(),outside.transform.position+new Vector3(-0.6f,0,0),Vector3.up,out start) &&
                SurfaceGeometry.Trace(start,Vector3.right,1.2f,out turned) && turned.Resolve(out end,out normal) &&
                Vector3.Distance(end,outside.transform.position+new Vector3(0,-0.6f,0))<0.015f,"trace wraps a convex ledge without a jump");

            Vector3[] gaps={new Vector3(-2,0,-1),new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(-2,0,1),
                new Vector3(0.1f,0,-1),new Vector3(2,0,-1),new Vector3(2,0,1),new Vector3(0.1f,0,1)};
            var gap=MeshObject("Disconnected surfaces",gaps,new[]{0,2,1,0,3,2,4,6,5,4,7,6},offset+Vector3.forward*16);
            Check(SurfaceGeometry.Acquire(gap.GetComponent<Collider>(),gap.transform.position+new Vector3(-0.2f,0,0),Vector3.up,out start) &&
                !SurfaceGeometry.Trace(start,Vector3.right,1f,out _),"surface trace stops at an air gap");
            var patch=SurfaceGeometry.BuildPatch(start,4f,7081);
            Check(patch.Count>0 && patch.All(p=>p.a.Resolve(out var a,out _) && a.x<=gap.transform.position.x+0.001f),"paint flood cannot reach a disconnected island");

            var owner=new GameObject("Inactive chunk owner");owner.transform.SetParent(stage.transform);owner.SetActive(false);
            var world=owner.AddComponent<ProceduralWorld>();
            var left=MeshObject("Left chunk",new[]{new Vector3(-2,0,-1),new Vector3(0,0,-1),new Vector3(0,0,1),new Vector3(-2,0,1)},new[]{0,2,1,0,3,2},offset+Vector3.forward*24);
            var right=MeshObject("Right chunk",new[]{new Vector3(0,0,-1),new Vector3(2,0,-1),new Vector3(2,0,1),new Vector3(0,0,1)},new[]{0,2,1,0,3,2},left.transform.position);
            left.AddComponent<ProceduralWorldChunk>().Initialize(world,Vector3Int.zero);right.AddComponent<ProceduralWorldChunk>().Initialize(world,Vector3Int.right);
            Physics.SyncTransforms();
            Check(SurfaceGeometry.Acquire(left.GetComponent<Collider>(),left.transform.position-Vector3.right*0.5f,Vector3.up,out start) &&
                SurfaceGeometry.Trace(start,Vector3.right,1f,out turned) && turned.Support==right.GetComponent<Collider>(),"trace crosses a matching terrain chunk seam");

            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Paint and network floor";floor.layer=7;floor.transform.SetParent(stage.transform);
            floor.transform.position=offset+new Vector3(12,-0.1f,0);floor.transform.localScale=new Vector3(20,0.2f,20);
            Physics.SyncTransforms();Vector3 centre=floor.transform.position+Vector3.up*0.1f;
            var paint=GooPaint.Splat(centre,Vector3.up,null,surface:floor.transform);
            Check(paint!=null && paint.TriangleCount>22,"goo builds an adaptive surface patch");Set(paint,"spread",1f);
            Check(GooPaint.Covers(centre,Vector3.up) && !GooPaint.Covers(centre+Vector3.up*0.2f,Vector3.up),"goo coverage is close to real geometry");
            Check(!GooPaint.Covers(centre,Vector3.down),"opposite-facing surfaces do not inherit paint");
            Vector3 old=centre;floor.transform.position+=Vector3.up*2;Physics.SyncTransforms();
            Check(!GooPaint.Covers(old) && GooPaint.Covers(old+Vector3.up*2,Vector3.up),"paint coverage follows a moving support");
            floor.transform.localScale=new Vector3(40f,0.2f,20f);Physics.SyncTransforms();
            Check(GooPaint.Covers(old+Vector3.up*2+Vector3.right*6,Vector3.up),"paint coverage scales with its visible support geometry");
            Check(paint.GetComponent<Collider>()==null,"paint adds no floating grip box");
            centre+=Vector3.up*2;
            Check(SurfaceGeometry.Acquire(floor.GetComponent<Collider>(),centre,Vector3.up,out start),"network has a surface seed");
            var networkObject=new GameObject("Network growth fixture");networkObject.transform.SetParent(stage.transform);networkObject.transform.position=centre;
            var network=networkObject.AddComponent<RecycleNetworkGeometry>();network.Initialize(start,7081,8f);
            for(int i=0;i<160;i++)network.Advance(0.1f);
            int before=network.NodeCount;network.AddGrowth(6);for(int i=0;i<160;i++)network.Advance(0.1f);
            Check(network.EstablishedLength>=7.5f && network.NodeCount>before,"feeding extends established procedural strands");

            var colonyPrefab=AssetDatabase.LoadAssetAtPath<CompostCreature>(CompostCreatureSetup.PrefabPath);
            var colony=Object.Instantiate(colonyPrefab,centre,Quaternion.identity,stage.transform);Set(colony,"storedCharges",25L);
            // Keep the mechanical fixture small while production scenes use the enlarged starting colony.
            Set(colony,"startingStrands",8f);Set(colony,"metresPerFlower",8f);
            colony.Geometry.Initialize(start,7081,8f);for(int i=0;i<160;i++)colony.Geometry.Advance(0.1f);
            var process=typeof(CompostCreature).GetMethod("AdvanceProcessing",Private);
            process.Invoke(colony,new object[]{0.01f});process.Invoke(colony,new object[]{5f});
            Check(colony.Flowers.Count==1 && colony.Flowers[0].CanBeCarried,"established colony visibly blooms its starter flower");
            var flower=colony.Flowers[0];flower.Detach();flower.transform.position=centre+Vector3.up*3;
            Check(flower.StoredCharges==0 && colony.StoredCharges==25 && Mathf.Approximately(flower.Age,120f),"detachment leaves all colony points and starts a two-minute life");
            process.Invoke(colony,new object[]{0.01f});
            Check(colony.StoredCharges==20 && colony.Flowers.Count==1 && !colony.Flowers[0].CanBeCarried,"stored points fund a visible replacement bud before goo production");
            Check(SurfaceGeometry.Acquire(bent.GetComponent<Collider>(),offset+new Vector3(0,1f,0),Vector3.left,out var wallSite),"acquire a vertical flower site");
            MethodInfo placement=typeof(CompostCreature).GetMethod("TryFlowerPose",Private);
            object[] placementArgs={wallSite,null,null,-1f};
            Check((bool)placement.Invoke(colony,placementArgs),"flower has an outward-facing clear wall pose");
            Pose wallPose=(Pose)placementArgs[1];float flowerScale=(float)placementArgs[2];
            Mesh flowerModel=AssetDatabase.LoadAssetAtPath<RecycleFlower>(RecycleSetup.FlowerPath).GetComponent<RecycleFlowerVisual>().Model;
            Check(Vector3.Dot(wallPose.rotation*Vector3.up,Vector3.left)>0.99f && flowerScale>=0.85f && flowerScale<=1.15f &&
                flowerModel.vertices.All(v=>Vector3.Dot(wallPose.position+wallPose.rotation*(v*flowerScale)-(offset+new Vector3(0,1f,0)),Vector3.left)>=-0.001f),
                "open wall flower stays outside the mounting wall with bounded size variation");
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.SetParent(stage.transform);blocker.layer=7;
            blocker.transform.position=wallPose.position+wallPose.rotation*new Vector3(0.38f*flowerScale,0.81f*flowerScale,0);
            blocker.transform.localScale=Vector3.one*0.10f;Physics.SyncTransforms();
            Check(!(bool)placement.Invoke(colony,new object[]{wallSite,null,null,-1f}),"open petals reject a nearby wall obstruction");Object.DestroyImmediate(blocker);
            Age(flower,80f);flower.Detach();Check(Mathf.Approximately(flower.Age,80f),"repeat detachment cannot reset lifespan");
            var corpseObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);corpseObject.transform.SetParent(stage.transform);corpseObject.transform.position=centre+Vector3.up*4;
            corpseObject.AddComponent<Rigidbody>().useGravity=false;
            var corpse=corpseObject.AddComponent<CreatureLifecycleValidationProbe>();corpse.Die();
            var second=Object.Instantiate(AssetDatabase.LoadAssetAtPath<RecycleFlower>(RecycleSetup.FlowerPath),centre+Vector3.up*5,Quaternion.identity,stage.transform);
            Check(Feed(flower,corpse,corpse.transform.position) && !Feed(second,corpse,corpse.transform.position),"one corpse has only one recycling owner");
            var digestion=corpse.GetComponent<CompostAbsorption>();digestion.CancelForPickup();
            Check(!digestion.HasClaim && corpse.transform.localScale==Vector3.one,"pickup cancellation restores the corpse");
            Check(Feed(flower,corpse,corpse.transform.position),"cancelled corpse can be fed again");
            typeof(RecycleFlower).GetMethod("TickCreature",Private).Invoke(flower,new object[]{5.1f});
            Check(!corpse.gameObject.activeSelf && flower.StoredCharges==6,"completed corpse awards nutrition exactly once");
            var liveObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);liveObject.transform.SetParent(stage.transform);liveObject.transform.position=centre+Vector3.up*6;
            liveObject.AddComponent<Rigidbody>().useGravity=false;var live=liveObject.AddComponent<CreatureLifecycleValidationProbe>();
            bool consumedLive=Feed(flower,live,live.transform.position);fixture.GetPhysicsScene().Simulate(0.02f);
            Check(!consumedLive && !live.IsDead && live.Health==live.MaxHealth && live.GetComponent<Rigidbody>().linearVelocity.y>0f,"living target is jolted without damage or consumption");
            var mealObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);mealObject.transform.SetParent(stage.transform);mealObject.transform.position=centre+Vector3.up*4.5f;
            mealObject.AddComponent<Rigidbody>().useGravity=false;var meal=mealObject.AddComponent<CreatureLifecycleValidationProbe>();meal.Die();
            Check(Feed(flower,meal,meal.transform.position),"portable flower accepts another corpse");
            typeof(RecycleFlower).GetMethod("TickCreature",Private).Invoke(flower,new object[]{5.1f});
            typeof(RecycleFlower).GetMethod("TickCreature",Private).Invoke(flower,new object[]{2.1f});
            int gooCount=Object.FindObjectsByType<GooCreature>(FindObjectsSortMode.None).Count(g=>g.gameObject.scene==fixture);
            Check(flower.StoredCharges==2 && gooCount==1,"portable flower spends ten points and drops one normal goo (points="+flower.StoredCharges+", drops="+gooCount+")");
            Age(flower,0f);Check(flower.IsDead && !flower.CanUseWhileCarried,"lifespan expiration stops flower use");

            var liveColony=Object.FindObjectsByType<CompostCreature>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.scene!=fixture && c.Geometry!=null && c.Geometry.Ready);
            Check(liveColony!=null && SurfaceGeometry.Probe(liveColony.gameObject.scene,liveColony.transform.position+Vector3.up*0.5f,Vector3.down,2f,true,out _),"loaded terrain is available for one contact-germination check");
            SurfaceGeometry.Probe(liveColony.gameObject.scene,liveColony.transform.position+Vector3.up*0.5f,Vector3.down,2f,true,out var seedAnchor);
            var seedFlower=Object.Instantiate(AssetDatabase.LoadAssetAtPath<RecycleFlower>(RecycleSetup.FlowerPath),liveColony.transform.position+Vector3.up*5,Quaternion.identity);
            SceneManager.MoveGameObjectToScene(seedFlower.gameObject,liveColony.gameObject.scene);sceneExtras.Add(seedFlower.gameObject);Age(seedFlower,0f);
            var prior=new HashSet<CompostCreature>(Object.FindObjectsByType<CompostCreature>(FindObjectsSortMode.None));
            var germination=seedFlower.GetComponent<RecycleGermination>();
            bool seeded=(bool)typeof(RecycleGermination).GetMethod("TryGerminate",Private).Invoke(germination,new object[]{seedAnchor});
            var newColonies=Object.FindObjectsByType<CompostCreature>(FindObjectsSortMode.None).Where(c=>!prior.Contains(c)).ToArray();
            foreach(var seededColony in newColonies)sceneExtras.Add(seededColony.gameObject);
            bool twice=(bool)typeof(RecycleGermination).GetMethod("TryGerminate",Private).Invoke(germination,new object[]{seedAnchor});
            Check(seeded && !twice && newColonies.Length==1 && newColonies[0].StoredCharges==0 && newColonies[0].Flowers.Count==0,"dead flower germinates once with no free points or flower");

            // Rebind against a new mesh revision without rebuilding a level or waiting on erosion simulation.
            Mesh replacement=Object.Instantiate(right.GetComponent<MeshCollider>().sharedMesh);createdMeshes.Add(replacement);
            right.GetComponent<MeshCollider>().sharedMesh=replacement;
            Check(!turned.Resolve(out _,out _),"anchors reject an obsolete terrain mesh revision");
            watch.Stop();return "PASS: "+checks+" focused geometry/lifecycle checks in "+watch.ElapsedMilliseconds+" ms (age accelerated).";
        }
        finally
        {
            foreach(GameObject extra in sceneExtras)if(extra!=null)Object.DestroyImmediate(extra);
            Object.DestroyImmediate(stage);
            foreach(Mesh mesh in createdMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);
            SceneManager.UnloadSceneAsync(fixture);
        }
    }

    public static async void StartGateCheck()
    {
        if(!Application.isPlaying){TraversalStatus="Enter Play Mode first";return;}
        var controller=Object.FindFirstObjectByType<SmoothFirstPersonController>();
        var climber=Object.FindFirstObjectByType<HandClimber>();
        var coating=controller.GetComponent<PlayerGooCoating>();var body=controller.GetComponent<Rigidbody>();
        var supply=typeof(PlayerGooCoating).GetMethod("SetRemaining",Private);
        Vector3 previous=body.position;float previousSupply=coating.RemainingSeconds;
        TraversalStatus="Waiting for authored drops (four-second limit)";
        try
        {
            float readyDeadline=Time.realtimeSinceStartup+4f;
            Func<bool> ready=()=>Object.FindObjectsByType<GooPaint>(FindObjectsSortMode.None).Count(p=>p.GetComponent<Renderer>().bounds.center.z>28f &&
                p.GetComponent<Renderer>().bounds.center.z<41f && p.transform.parent.GetComponent<ProceduralWorld>()!=null)>=2;
            while(!ready() && Time.realtimeSinceStartup<readyDeadline)await Task.Delay(16);
            if(!ready()){TraversalStatus="FAIL: authored gate drops have not both settled on terrain";return;}
            TraversalStatus="Running (eight-second limit)";
            supply.Invoke(coating,new object[]{0f});climber.StopForBubbleCapture();
            body.position=new Vector3(GooSceneSetup.CaveCentre(31.5f),GooSceneSetup.GateFloor(31.5f)+1.02f,31.5f);
            body.linearVelocity=Vector3.zero;controller.SetLook(0f,0f);climber.ScriptedInput=Vector2.up;
            float deadline=Time.realtimeSinceStartup+8f;bool climbed=false;
            while(Application.isPlaying && Time.realtimeSinceStartup<deadline)
            {
                climbed|=climber.IsClimbing;
                if(body.position.z>32.9f && body.position.y-1f>-9.5f)break;
                if(!climber.IsClimbing && !climber.IsMantling)body.linearVelocity=new Vector3(0f,body.linearVelocity.y,2.5f);
                await Task.Delay(16);
            }
            TraversalStatus=climbed && body.position.z>32.9f && body.position.y-1f>-9.5f && coating.RemainingSeconds==0f ?
                "PASS: actual painted gate crossed without personal goo (eight-second bounded check)." :
                "FAIL: gate at "+body.position+"; "+climber.LastEvent;
        }
        catch(Exception e){TraversalStatus="FAIL: "+e.Message;}
        finally
        {
            climber.ScriptedInput=null;climber.StopForBubbleCapture();body.position=previous;
            if(!body.isKinematic)body.linearVelocity=Vector3.zero;
            supply.Invoke(coating,new object[]{previousSupply});controller.SetLook(0f,0f);
        }
    }

    public static async void StartPerformanceCheck()
    {
        if(!Application.isPlaying){PerformanceStatus="Enter Play Mode first";return;}
        PerformanceStatus="Preparing eight developed colonies and paint capacity";
        Scene fixture=SceneManager.CreateScene("Recycle short performance sample",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var stage=new GameObject("Disposable surface workload");SceneManager.MoveGameObjectToScene(stage,fixture);
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(stage.transform);floor.layer=7;
            floor.transform.position=new Vector3(3000f,-0.1f,3000f);floor.transform.localScale=new Vector3(220f,0.2f,220f);Physics.SyncTransforms();
            var colonies=new List<RecycleNetworkGeometry>();
            MethodInfo step=typeof(RecycleNetworkGeometry).GetMethod("AddStep",Private);
            for(int i=0;i<8;i++)
            {
                Vector3 point=floor.transform.position+new Vector3((i-3.5f)*12f,0.1f,0f);
                SurfaceGeometry.Acquire(floor.GetComponent<Collider>(),point,Vector3.up,out var seed);
                var go=new GameObject("Developed geometry "+i);go.transform.SetParent(stage.transform);go.transform.position=point;
                var geometry=go.AddComponent<RecycleNetworkGeometry>();geometry.Initialize(seed,7081+i,8f);
                int parent=geometry.NodeCount-1;
                for(int j=0;j<900;j++)
                {
                    Vector3 heading=Quaternion.AngleAxis(j*9f,Vector3.up)*Vector3.forward;
                    int next=(int)step.Invoke(geometry,new object[]{parent,heading,0.16f});
                    if(next<0)break;parent=next;
                }
                Set(geometry,"credits",0f);Set(geometry,"clock",1000f);Set(geometry,"visualUntil",0f);Set(geometry,"dirty",true);
                geometry.Advance(0.016f);colonies.Add(geometry);
            }
            var points=new List<Vector3>();int existing=Object.FindObjectsByType<GooPaint>(FindObjectsSortMode.None).Length;
            for(int i=0;i<Mathf.Max(0,24-existing);i++)
            {
                Vector3 point=floor.transform.position+new Vector3((i%8-3.5f)*12f,0.1f,(i/8+1)*12f);
                var paint=GooPaint.Splat(point,Vector3.up,null,surface:floor.transform);
                if(paint!=null){Set(paint,"spread",1f);points.Add(point);}
            }
            var samples=new List<double>();double maxUpload=0;var timer=new Stopwatch();
            for(int frame=0;frame<90;frame++)
            {
                timer.Restart();foreach(var geometry in colonies)geometry.Advance(1f/60f);
                foreach(var point in points)GooPaint.Covers(point,Vector3.up);
                timer.Stop();samples.Add(timer.Elapsed.TotalMilliseconds);maxUpload=Math.Max(maxUpload,SurfaceWorkQueue.LastWorkMilliseconds);
                await Task.Delay(16);
            }
            samples.Sort();double p95=samples[(int)(samples.Count*0.95f)];
            PerformanceStatus="Eight colonies ("+colonies.Sum(c=>c.NodeCount)+" nodes), 24 total paint patches: steady CPU p95 "+p95.ToString("0.00")+
                " ms; largest sampled queued upload "+maxUpload.ToString("0.00")+" ms. Ninety-frame sample; construction excluded.";
        }
        catch(Exception e){PerformanceStatus="FAIL: "+e;}
        finally{Object.DestroyImmediate(stage);SceneManager.UnloadSceneAsync(fixture);}
    }
}
