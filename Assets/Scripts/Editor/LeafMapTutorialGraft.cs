using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Transfers the saved tutorial cave, including sculpting, into the leaf's underground stem.</summary>
public static class LeafMapTutorialGraft
{
    // The grown leaf raises the cave into its highland (LeafMapTerrain.CaveLift).
    public static Vector3 Offset=>new Vector3(112,8+LeafMapTerrain.CaveLift,-61);
    private static Vector3 Lift=>Vector3.up*LeafMapTerrain.CaveLift;
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private const string Group="01 Tutorial — original YoYoTutorialFall cave";
    private const string Stamp="Assets/Generated/LeafMap/TutorialGraft.txt";

    public static void Inspect()
    {
        var leaf=SceneManager.GetActiveScene();
        var source=SceneManager.GetSceneByPath(YoYoTutorialFallSetup.ScenePath);
        bool opened=!source.IsValid() || !source.isLoaded;
        if(opened)source=EditorSceneManager.OpenScene(YoYoTutorialFallSetup.ScenePath,OpenSceneMode.Additive);
        try
        {
            var lines=new StringBuilder();
            foreach(var go in source.GetRootGameObjects())Describe(go.transform,lines,0);
            File.WriteAllText("Temp/LeafMap.tutorial.txt",lines.ToString());
        }
        finally {if(opened)EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(leaf);}
    }
    private static void Describe(Transform t,StringBuilder lines,int depth)
    {
        if(depth>2)return;
        lines.AppendLine(new string(' ',depth*2)+t.name+" | "+t.position+" | "+t.gameObject.activeSelf+" | "+string.Join(",",t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name)));
        foreach(Transform child in t)Describe(child,lines,depth+1);
    }

    /// <summary>The layout with a grid big enough for the leaf at its current scale.</summary>
    public static string WithWorldSize(string layout)
    {
        Vector2 size=LeafMapTerrain.WorldSizeFor(LeafMapTerrain.Scale);
        return System.Text.RegularExpressions.Regex.Replace(layout,"\"worldSize\":\\{.*?\\}",
            "\"worldSize\":{\"x\":"+size.x.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+",\"y\":"+size.y.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"}");
    }

    public static void Apply(ProceduralWorld target,Transform map)
    {
        string layout=WithWorldSize(target.SculptData.Layout);
        if(layout!=target.SculptData.Layout)
        {
            PrepareSampler(target);var oldMin=target.GridMinimum;var edits=target.SculptData.Edits.ToArray();
            target.SculptData.SetLayout(layout,Array.Empty<float>());PrepareSampler(target);
            var shift=Vector3Int.RoundToInt((oldMin-target.GridMinimum)/target.CellSize);
            target.SculptData.ClearEdits();foreach(var pair in edits)target.SculptData.SetEdit(pair.Key+shift,pair.Value);
        }
        string signature="YoYo cave graft v4 / "+File.GetLastWriteTimeUtc(YoYoTutorialFallSetup.ScenePath).Ticks+" / "+File.GetLastWriteTimeUtc("Assets/Terrain/Sculpted/Scene_YoYoTutorialFall_Terrain.asset").Ticks
            +(LeafMapTerrain.Scale>1f?" / scale "+LeafMapTerrain.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+" / lift "+LeafMapTerrain.CaveLift:"");
        if(map.Find(Group)!=null && File.Exists(Stamp) && File.ReadAllText(Stamp)==signature)return;
        var leaf=target.gameObject.scene;
        var source=SceneManager.GetSceneByPath(YoYoTutorialFallSetup.ScenePath);
        bool opened=!source.IsValid() || !source.isLoaded;
        if(opened)source=EditorSceneManager.OpenScene(YoYoTutorialFallSetup.ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(leaf);
        try
        {
            var donor=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>(true)).Single();
            PrepareSampler(donor);PrepareSampler(target);
            var field=typeof(ProceduralWorld).GetField("field",Private).GetValue(target);
            var height=(Func<float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float>),field,"SurfaceHeight");
            var ceiling=(Func<float,float>)Delegate.CreateDelegate(typeof(Func<float,float>),field,"CeilingHeight");
            var floor=(Func<float,float,float,float>)Delegate.CreateDelegate(typeof(Func<float,float,float,float>),field,"InnerGroundHeight");
            var density=(Func<Vector3,float,float,float,float>)Delegate.CreateDelegate(typeof(Func<Vector3,float,float,float,float>),field,"Density");
            Vector3 min=target.GridMinimum;
            float cell=target.CellSize;
            Vector3 lower=new Vector3(-40,-42,-214)+Lift,upper=new Vector3(46,45,-86)+Lift;
            Vector3Int lo=Vector3Int.FloorToInt((lower-min)/cell),hi=Vector3Int.CeilToInt((upper-min)/cell);
            int samples=0;
            // Earlier grafts may sit at another height: clear every column of the cave box first.
            foreach(var earlier in target.SculptData.Edits.Keys.ToArray())
                if(earlier.x>=lo.x && earlier.x<=hi.x && earlier.z>=lo.z && earlier.z<=hi.z)target.SculptData.SetEdit(earlier,0f);
            for(int z=lo.z;z<=hi.z;z++)for(int x=lo.x;x<=hi.x;x++)
            {
                Vector3 column=min+new Vector3(x*cell,0,z*cell);
                float edge=Mathf.Min(Mathf.Min(column.x-lower.x,upper.x-column.x),Mathf.Min(column.z-lower.z,upper.z-column.z));
                if(edge<=0)continue;
                float weight=Mathf.SmoothStep(0,1,edge/6);
                float h=height(column.x,column.z),c=ceiling(h),f=floor(column.x,column.z,h);
                for(int y=lo.y;y<=hi.y;y++)
                {
                    var key=new Vector3Int(x,y,z);Vector3 p=min+(Vector3)key*cell;
                    float baseValue=density(p,h,c,f),original=donor.SampleDensity(p-Offset,false);
                    float fade=Mathf.SmoothStep(0,1,Mathf.Min(p.y-lower.y,upper.y-p.y)/5)*weight;
                    float result=Mathf.Lerp(baseValue,original,fade);
                    result=Mathf.Min(result,LeafMapTerrain.Footprint(p.x,p.z));
                    // Away from any surface only the sign matters, so far samples keep the base value (fewer saved edits).
                    if((result>2.5f && baseValue>2.5f)||(result< -2.5f && baseValue< -2.5f))result=baseValue;
                    target.SculptData.SetEdit(key,result-baseValue);samples++;
                }
            }
            EditorUtility.SetDirty(target.SculptData);
            var old=map.Find("01 Tutorial cavern — stem and first reveal");if(old!=null)old.gameObject.SetActive(false);
            old=map.Find(Group);if(old!=null)Object.DestroyImmediate(old.gameObject);
            var group=new GameObject(Group).transform;group.SetParent(map,false);
            var sourceIntro=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name.StartsWith("01 P and BS"));
            var intro=Object.Instantiate(sourceIntro.gameObject,group).transform;intro.position=sourceIntro.position+Offset;
            intro.name="Original underground spawn and BounceShroom practice";
            var player=leaf.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>(true)).Single();
            var sourcePlayer=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>(true)).Single();
            var playerRoot=player.transform.root;var sourceRoot=sourcePlayer.transform.root;
            playerRoot.SetPositionAndRotation(sourceRoot.position+Offset,sourceRoot.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(playerRoot);
            var hud=leaf.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<InteractionPromptHUD>(true)).FirstOrDefault();
            if(hud!=null)
            {
                var sourceTrigger=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<InteractionHintsTrigger>(true)).First();
                var trigger=Object.Instantiate(sourceTrigger.gameObject,group);trigger.name="Original tutorial exit — hide hints";trigger.transform.position=sourceTrigger.transform.position+Offset;
                var settings=new SerializedObject(trigger.GetComponent<InteractionHintsTrigger>());settings.FindProperty("hud").objectReferenceValue=hud;settings.ApplyModifiedPropertiesWithoutUndo();
                settings=new SerializedObject(hud);settings.FindProperty("hintsVisible").boolValue=true;settings.ApplyModifiedPropertiesWithoutUndo();
            }
            Lamp(group,new Vector3(0,-15,-198)+Lift,new Color(.36f,.65f,1),12,15);
            Lamp(group,new Vector3(-8,-12,-171)+Lift,new Color(.5f,.8f,.72f),16,22);
            Lamp(group,new Vector3(22,-1,-133)+Lift,new Color(.4f,.65f,.9f),10,16);
            Lamp(group,new Vector3(0,13,-97)+Lift,new Color(.7f,.85f,1),5,15);
            AssetDatabase.SaveAssetIfDirty(target.SculptData);
            File.WriteAllText(Stamp,signature);
            File.WriteAllText("ArtCaptures/LeafMap/TutorialGraft.txt","Source: "+YoYoTutorialFallSetup.ScenePath+"\nOffset: "+Offset+"\nResampled density points: "+samples+"\nPractice creatures: "+intro.GetComponentsInChildren<Creature>(true).Length+"\nPlayer: "+playerRoot.position);
        }
        finally {if(opened)EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(leaf);}
    }
    private static void PrepareSampler(ProceduralWorld world)
    {
        typeof(ProceduralWorld).GetMethod("UseSavedLayout",Private).Invoke(world,null);
        typeof(ProceduralWorld).GetMethod("PrepareGrid",Private).Invoke(world,new object[]{world.SculptData.Headroom});
        typeof(ProceduralWorld).GetMethod("LoadSculpt",Private).Invoke(world,null);
    }
    private static void Lamp(Transform parent,Vector3 at,Color color,float power,float range)
    {
        var go=new GameObject("Tutorial cave bounce light");go.transform.SetParent(parent,false);go.transform.position=at;
        var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.intensity=power;light.range=range;
    }
}
