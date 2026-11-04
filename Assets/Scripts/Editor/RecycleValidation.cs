using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Small deterministic fixtures. No real-time ageing waits, generation loops, or ecology soak tests.</summary>
public static class RecycleValidation
{
    static int checks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value,string message)
    { if(!value)throw new InvalidOperationException("Recycle: "+message);checks++; }
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);
    static void Age(Creature target,float seconds) => typeof(Creature).GetMethod("SetRemainingAge",Private).Invoke(target,new object[]{seconds});
    static bool Feed(RecycleFlower flower,Creature target,Vector3 point) => (bool)typeof(RecycleFlower).GetMethod("TryFeedOrJolt",Private).Invoke(flower,new object[]{target,point});
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
            var outside=MeshObject("Convex ledge",convex,new[]{0,2,1,0,3,2,1,4,2,2,4,5},offset+Vector3.forward*8);
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
            var flower=Object.Instantiate(AssetDatabase.LoadAssetAtPath<RecycleFlower>(RecycleSetup.FlowerPath),centre+Vector3.up*3,Quaternion.identity,stage.transform);
            flower.ConfigureAttached(colony,start);Set(flower,"bloom",1f);flower.Detach();
            Check(flower.StoredCharges==0 && colony.StoredCharges==25 && Mathf.Approximately(flower.Age,120f),"detachment leaves all colony points and starts a two-minute life");
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
            Check(!Feed(flower,live,live.transform.position) && !live.IsDead && live.Health==live.MaxHealth && live.GetComponent<Rigidbody>().linearVelocity.y>0f,"living target is jolted without damage or consumption");
            Age(flower,0f);Check(flower.IsDead && !flower.CanUseWhileCarried,"lifespan expiration stops flower use");

            // Rebind against a new mesh revision without rebuilding a level or waiting on erosion simulation.
            Mesh replacement=Object.Instantiate(right.GetComponent<MeshCollider>().sharedMesh);createdMeshes.Add(replacement);
            right.GetComponent<MeshCollider>().sharedMesh=replacement;
            Check(!turned.Resolve(out _,out _),"anchors reject an obsolete terrain mesh revision");
            watch.Stop();return "PASS: "+checks+" focused geometry/lifecycle checks in "+watch.ElapsedMilliseconds+" ms (age accelerated).";
        }
        finally
        {
            Object.DestroyImmediate(stage);
            foreach(Mesh mesh in createdMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);
            SceneManager.UnloadSceneAsync(fixture);
        }
    }
}
