using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Saved terrain and placement revision: isolated first encounters, traversable ridges and a delayed basin reveal.</summary>
public static class YoYoTutorialFallFlow
{
    private const string Revision="EncounterFlow.v2";
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static readonly Vector3[] ExitRoute={
        new Vector3(-112f,-26f,-92f),new Vector3(-97f,-20f,-83f),new Vector3(-82f,-14f,-72f),
        new Vector3(-91f,-8f,-57f),new Vector3(-106f,-2f,-43f),new Vector3(-112f,2f,-33f),
    };
    public static readonly Vector3[] FirstEncounters={
        new Vector3(-112f,2f,-33f),new Vector3(-98f,2.5f,-38f),new Vector3(-80f,3f,-43f),
        new Vector3(-61f,3f,-45f),new Vector3(-42f,4f,-31f),new Vector3(-34f,6f,-13f),
        new Vector3(-41f,8f,-1f),new Vector3(-53f,8f,1f),new Vector3(-69f,8f,-5f),
        new Vector3(-86f,8f,0f),new Vector3(-99f,9f,11f),new Vector3(-109f,9f,18f),
    };
    public static readonly Vector3[] RidgeCrossing={
        new Vector3(-103f,9f,26f),new Vector3(-94f,11f,38f),new Vector3(-86f,13f,48f),
        new Vector3(-64f,13f,55f),new Vector3(-39f,14f,43f),new Vector3(-12f,18f,36f),
        new Vector3(13f,17f,32f),new Vector3(39f,9f,27f),new Vector3(62f,3f,20f),
    };

    public static float Height(float x,float z)
    {
        float baseHeight=2f+(Mathf.PerlinNoise(x*0.032f+17f,z*0.032f+43f)-0.5f)*1.4f;
        float oval=new Vector2(x/174f,(z-9f)/153f).magnitude;
        float foot=0.865f+(Mathf.PerlinNoise(x*0.025f+51f,z*0.025f+8f)-0.5f)*0.035f;
        float h=baseHeight+Mathf.SmoothStep(0f,1f,(oval-foot)/0.09f)*55f;
        // Broad slopes remain traversable; their crests still hide the next low encounter pocket.
        h=Mathf.Max(h,baseHeight+Hill(x,z,-14f,-55f,26f,35f,24f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,10f,19f,33f,52f,28f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,15f,66f,32f,24f,19f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,-74f,-18f,24f,14f,13f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,-82f,15f,14f,23f,12f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,-117f,25f,20f,22f,25f));
        h=Mathf.Max(h,baseHeight+Hill(x,z,-73f,73f,31f,28f,14f));
        h=Path(h,x,z,FirstEncounters,4.5f,7f);
        h=Path(h,x,z,RidgeCrossing,5f,9f);
        h=Path(h,x,z,new[]{ExitRoute[ExitRoute.Length-2],ExitRoute[ExitRoute.Length-1]},4.8f,5f);
        h=Clearing(h,x,z,-108f,-34f,11f,2f);
        h=Clearing(h,x,z,-80f,-43f,15f,3f);
        h=Clearing(h,x,z,-52f,1f,14f,8f);
        h=Clearing(h,x,z,-85f,48f,5f,13f);
        h=Clearing(h,x,z,98f,-5f,36f,3f);
        h=Clearing(h,x,z,77f,66f,25f,4f);
        h=Clearing(h,x,z,111f,99f,25f,7f);
        return h;
    }

    public static float ExitRock(Vector3 p)
        => Rock(p,ExitRoute);

    private static readonly Vector3[] SteppedRoute={
        new Vector3(-112f,-26f,-94f),new Vector3(-97f,-20f,-83f),new Vector3(-82f,-14f,-72f),
        new Vector3(-91f,-8f,-57f),new Vector3(-106f,-2f,-43f),new Vector3(-112f,2f,-33f),
    };
    public static float SteppedExitRock(Vector3 p)=>Rock(p,SteppedRoute);
    private static float Rock(Vector3 p,Vector3[] nodes)
    {
        float rock=float.NegativeInfinity;
        for(int i=1;i<nodes.Length;i++)
        {
            float distance=Distance(p,nodes[i-1],nodes[i],out float t);
            float floor=Mathf.Lerp(nodes[i-1].y,nodes[i].y,t);
            rock=Mathf.Max(rock,Mathf.Min(11f-distance,Mathf.Min(p.y-floor+8f,floor+13f-p.y)));
        }
        return rock;
    }
    public static float ExitTunnel(Vector3 p)
    {
        float nearest=float.MaxValue,floor=0f;
        for(int i=1;i<ExitRoute.Length;i++)
        {
            float d=Distance(p,ExitRoute[i-1],ExitRoute[i],out float t);
            if(d<nearest){nearest=d;floor=Mathf.Lerp(ExitRoute[i-1].y,ExitRoute[i].y,t);}
        }
        return Mathf.Max(nearest-4.8f,Mathf.Max(floor-p.y,p.y-floor-7.5f));
    }
    // Previous capsule caps are retained only to migrate the saved v2 ramp without losing hand sculpting.
    public static float SteppedExitTunnel(Vector3 p)
    {
        float air=float.MaxValue;
        for(int i=1;i<SteppedRoute.Length;i++)
        {
            float distance=Distance(p,SteppedRoute[i-1],SteppedRoute[i],out float t);
            float floor=Mathf.Lerp(SteppedRoute[i-1].y,SteppedRoute[i].y,t);
            air=Mathf.Min(air,Mathf.Max(distance-4.8f,Mathf.Max(floor-p.y,p.y-floor-7.5f)));
        }
        return air;
    }
    public static float SpiderGrotto(Vector3 p)
    {
        Vector3 d=p-new Vector3(-117f,15f,25f);
        float room=Mathf.Max((new Vector3(d.x/14f,d.y/6f,d.z/14f).magnitude-1f)*6f,9f-p.y);
        var openings=new[]{new Vector3(-98f,9f,11f),new Vector3(-108f,9f,18f),new Vector3(-116f,9f,24f),new Vector3(-103f,9f,26f),new Vector3(-98f,10f,33f)};
        for(int i=1;i<openings.Length;i++)
        {
            float distance=Distance(p,openings[i-1],openings[i],out float t);
            float floor=Mathf.Lerp(openings[i-1].y,openings[i].y,t);
            room=Mathf.Min(room,Mathf.Max(distance-4.7f,Mathf.Max(floor-p.y,p.y-floor-8f)));
        }
        return room;
    }

    [MenuItem("Tools/Capstone/YoYo Tutorial Fall/4 Refine Encounter Flow And Slopes")]
    public static object Apply()
    {
        var world=World();
        var asset=new SerializedObject(world.SculptData);
        string source=asset.FindProperty("bakedFrom").stringValue;
        if(source.Contains(Revision))throw new InvalidOperationException("This revision is already applied. Sculpt the saved terrain for further changes.");
        // Play-mode transitions can leave preview counters intact while the private grid cache is empty.
        world.Generate(true);
        var field=typeof(ProceduralWorld).GetField("field",Private).GetValue(world);
        var height=(Func<float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float>),field,"SurfaceHeight");
        var ceiling=(Func<float,float>)Delegate.CreateDelegate(typeof(Func<float,float>),field,"CeilingHeight");
        var floor=(Func<float,float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float,float>),field,"InnerGroundHeight");
        var original=(Func<Vector3,float,float,float,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float,float,float,float>),field,"Density");
        var intro=(Func<Vector3,float,bool,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float,bool,float>),typeof(YoYoTutorialFallSetup).GetMethod("IntroCave",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(Vector3),typeof(float),typeof(bool)},null));
        var caveMethod=typeof(YoYoTutorialFallSetup).GetMethod("TentacleCave",BindingFlags.Static|BindingFlags.NonPublic);
        var branch=(Func<Vector3,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float>),caveMethod);
        var data=world.SculptData;
        Undo.RegisterCompleteObjectUndo(data,"Refine tutorial encounter flow");
        Vector3 minimum=world.GridMinimum;float cell=world.CellSize;
        Vector3Int count=Vector3Int.RoundToInt(world.VolumeBounds.size/cell);
        if(count.x<1||count.y<1||count.z<1)throw new InvalidOperationException("Terrain grid is not ready for sculpting.");
        int changed=0,retainedHandEdits=0;
        for(int z=0;z<=count.z;z++)for(int x=0;x<=count.x;x++)
        {
            Vector3 column=minimum+new Vector3(x*cell,0f,z*cell);
            if(Mathf.Abs(column.x)>185f||column.z< -164f||column.z>180f)continue;
            Vector3 local=column-world.transform.position;
            float bh=height(local.x,local.z),bc=ceiling(bh),bf=floor(local.x,local.z,bh);
            float oldH=YoYoTutorialFallSetup.LegacySketchHeight(column.x,column.z),newH=Height(column.x,column.z);
            for(int y=0;y<=count.y;y++)
            {
                var key=new Vector3Int(x,y,z);Vector3 p=minimum+(Vector3)key*cell;if(p.y>80f)continue;
                float baseline=original(p-world.transform.position,bh,bc,bf);
                float oldTarget=Profile(p,oldH,false,intro,branch),newTarget=Profile(p,newH,true,intro,branch);
                float oldEdit=StoredDelta(baseline,oldTarget),newEdit=StoredDelta(baseline,newTarget);
                data.Edits.TryGetValue(key,out float actual);
                float residual=actual-oldEdit;
                if(Mathf.Abs(residual)>0.002f)retainedHandEdits++;
                float replacement=newEdit+residual;
                if(Mathf.Abs(replacement-actual)<0.001f)continue;
                data.SetEdit(key,replacement);changed++;
            }
        }
        if(changed==0)throw new InvalidOperationException("No terrain samples changed; the revision was not marked as applied.");
        asset.Update();asset.FindProperty("bakedFrom").stringValue=source+" / "+Revision;asset.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);
        world.Generate(true);
        ArrangePlacements();
        EditorSceneManager.SaveScene(world.gameObject.scene);AssetDatabase.SaveAssets();
        return new { changed,retainedHandEdits,exitMaximumDegrees=ExitRoute.Zip(ExitRoute.Skip(1),(a,b)=>Mathf.Atan2(Mathf.Abs(b.y-a.y),new Vector2(b.x-a.x,b.z-a.z).magnitude)*Mathf.Rad2Deg).Max() };
    }

    private static float Profile(Vector3 p,float h,bool revised,Func<Vector3,float,bool,float> intro,Func<Vector3,float> branch)
    {
        float c=-10f-h*0.65f,f=-34f-h*0.7f+Mathf.PerlinNoise(p.x*0.04f+91f,p.z*0.04f+33f)-0.5f;
        float density=Mathf.Max(Mathf.Min(h-p.y,p.y-c),f-p.y);
        if(revised?(p.x< -63f&&p.z< -18f):(p.x< -75f&&p.z< -40f))density=intro(p,density,revised);
        if(p.x> -107f&&p.x< -43f&&p.z>41f&&p.z<105f)density=Mathf.Min(density,branch(p));
        if(revised&&p.x> -143f&&p.x< -87f&&p.z> -1f&&p.z<49f)density=Mathf.Min(density,SpiderGrotto(p));
        if(Mathf.Abs(p.x-98f)<12f&&Mathf.Abs(p.z+4f)<18f&&p.y>f+2f)
        {float dx=p.x-98f,dz=p.z+4f+p.y*0.07f;density=Mathf.Min(density,Mathf.Sqrt(dx*dx+dz*dz)-7f*(1f+0.18f*Mathf.Sin(p.y*0.35f+98f)));}
        return density;
    }
    private static float StoredDelta(float baseline,float target)=>((baseline>5f&&target>5f)||(baseline< -5f&&target< -5f)||Mathf.Abs(target-baseline)<0.002f)?0f:target-baseline;

    public static void ArrangePlacements()
    {
        var world=World();
        if(world.GetComponent<WorldEdgeWalls>()==null)world.gameObject.AddComponent<WorldEdgeWalls>();
        RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_FractalCalm.mat");
        MoveSurface("Compo — recycling creature",-80f,-43f,0.2f);
        MoveSurface("Bones for compost",-83f,-40f,0.3f);
        MoveSurface("Food — recycling hollow",-77f,-45f,0.5f);
        MoveSurface("BF — terrace flower 1",-54f,1f,0.1f);
        MoveSurface("BF — terrace flower 2",-47f,5f,0.1f);
        Move("SS — western web builder",new Vector3(-116f,12f,23f));
        MoveSurface("Fuzz — first glen",72f,37f,0.5f);
        var extraFuzz=GameObject.Find("Fuzz — first glen");
        var final=GameObject.Find("07 Final basin — spires, DB and mixed creatures");
        if(extraFuzz!=null){if(final!=null)extraFuzz.transform.SetParent(final.transform,true);extraFuzz.name="Fuzz — final grove 3";}
        var compostGroup=GameObject.Find("05 Compo — recycling hollow");if(compostGroup!=null)compostGroup.name="03 Compo — first encounter hollow";
        var spiderGroup=GameObject.Find("03 SS — high western spider shelf");if(spiderGroup!=null)spiderGroup.name="05 SS — western web grotto";
        Move("Leave Start Cave — hide hints",new Vector3(-109f,6f,-35f));
        var exit=GameObject.Find("Leave Start Cave — hide hints");if(exit!=null)exit.GetComponent<BoxCollider>().size=new Vector3(18f,14f,5f);
        // Settle the other existing surface placements onto the new terrain, without moving underground residents.
        foreach(var creature in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
        {
            if(creature.gameObject.scene!=world.gameObject.scene||creature.name.StartsWith("BS — cave")||creature.name.StartsWith("Tentacle — cave")||creature.name=="SS — western web builder")continue;
            var p=creature.transform.position;
            if(creature.name.StartsWith("BF — terrace")||creature.name=="Compo — recycling creature"||creature.name=="Bones for compost"||creature.name=="Food — recycling hollow")continue;
            float lift=creature is GooCreature?0.45f:creature is FireSource?0.7f:creature is SkySpider?3f:creature is FuzzCreature?0.5f:0.15f;
            creature.transform.position=YoYoTutorialFallSetup.Surface(p.x,p.z,lift);
            EditorUtility.SetDirty(creature.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(creature.transform);
        }
        var floating=GameObject.Find("Spire B — high tower")??GameObject.Find("Spire B — floating high tower");
        if(floating!=null){floating.name="Spire B — floating high tower";floating.transform.position=YoYoTutorialFallSetup.Surface(111f,103f)+Vector3.up*26f;EditorUtility.SetDirty(floating.transform);}
        var lower=GameObject.Find("Spire A — lower tower");if(lower!=null)lower.transform.position=YoYoTutorialFallSetup.Surface(76f,66f,-0.2f);
        ConfigureFlocks(world);
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
    }

    private static void ConfigureFlocks(ProceduralWorld world)
    {
        var manager=Object.FindFirstObjectByType<BirdFlockManager>();
        if(manager!=null){manager.SkyBounds=new Bounds(new Vector3(-1f,29f,22f),new Vector3(310f,58f,250f));EditorUtility.SetDirty(manager);}
        var points=new[]{new Vector2(-108f,-31f),new Vector2(-80f,-43f),new Vector2(-51f,2f),new Vector2(-112f,25f),new Vector2(-27f,40f),new Vector2(102f,-1f),new Vector2(95f,79f)};
        string[] labels={"entry","compost","flower","spider","ridge","dragon basin","spires"};
        var prefab=AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab");
        var existing=GameObject.Find("Birds — native spawn area");if(existing!=null)existing.name="Flock — dragon basin";
        for(int i=0;i<points.Length;i++)
        {
            string name="Flock — "+labels[i];var go=GameObject.Find(name);
            if(go==null)go=new GameObject(name);
            go.transform.position=YoYoTutorialFallSetup.Surface(points[i].x,points[i].y,17f);
            var area=go.GetComponent<BirdSpawnArea>()??go.AddComponent<BirdSpawnArea>();
            area.Configure(prefab,5,10f,8200+i);area.SetFlightSpeedOverride(true,new Vector2(3f,6f));EditorUtility.SetDirty(area);
        }
    }
    private static void MoveSurface(string name,float x,float z,float lift)=>Move(name,YoYoTutorialFallSetup.Surface(x,z,lift));
    private static void Move(string name,Vector3 position){var go=GameObject.Find(name);if(go==null)return;Undo.RecordObject(go.transform,"Reposition encounter");go.transform.position=position;EditorUtility.SetDirty(go.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);}
    private static ProceduralWorld World(){var w=Object.FindFirstObjectByType<ProceduralWorld>();if(EditorApplication.isPlayingOrWillChangePlaymode||w==null||w.gameObject.scene.path!=YoYoTutorialFallSetup.ScenePath)throw new InvalidOperationException("Open YoYo Tutorial Fall in Edit Mode.");return w;}
    private static float Hill(float x,float z,float cx,float cz,float rx,float rz,float h)=>h*Mathf.Exp(-1.5f*((x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz)));
    private static float Clearing(float h,float x,float z,float cx,float cz,float radius,float target)=>Mathf.Lerp(h,target,1f-Mathf.SmoothStep(0f,1f,(Vector2.Distance(new Vector2(x,z),new Vector2(cx,cz))-radius)/8f));
    private static float Distance(Vector3 p,Vector3 a,Vector3 b,out float t){var d=new Vector2(b.x-a.x,b.z-a.z);var offset=new Vector2(p.x-a.x,p.z-a.z);t=Mathf.Clamp01(Vector2.Dot(offset,d)/d.sqrMagnitude);return(offset-d*t).magnitude;}
    private static float Path(float h,float x,float z,Vector3[] nodes,float radius,float blend){float nearest=float.MaxValue,target=h;for(int i=1;i<nodes.Length;i++){float d=Distance(new Vector3(x,0f,z),nodes[i-1],nodes[i],out float t);if(d<nearest){nearest=d;target=Mathf.Lerp(nodes[i-1].y,nodes[i].y,t);}}return Mathf.Lerp(h,target,1f-Mathf.SmoothStep(0f,1f,(nearest-radius)/blend));}
}
