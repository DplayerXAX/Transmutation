using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Completes the Daniel experiment in place, preserving unrelated scene content.</summary>
public static class DanielExperimentFinish
{
    private const string ArtFolder="Assets/Materials/ColorExperiment";
    [MenuItem("Tools/Capstone/Daniel/Complete Experiment")]
    public static void Build()
    {
        if(Application.isPlaying || SceneManager.GetActiveScene().path!="Assets/Scenes/Scene_Daniel.unity")
            throw new InvalidOperationException("Open Daniel in Edit Mode.");
        var root=GameObject.Find("Daniel Color Experiment").transform;
        if(root.Find("Experiment authored v2")!=null) return;
        if(!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Materials","ColorExperiment");
        ApplyPalette();
        var world=Object.FindFirstObjectByType<ProceduralWorld>();
        // Recreate the complete chunk dictionary after a script reload before sculpting.
        world.Generate(true);
        var cave=root.Find("Opening Cave");
        var basin=root.Find("Asura Basin");
        Vector3 spawn=cave.position;
        Vector3 forward=cave.forward, left=Vector3.Cross(Vector3.up,forward);
        basin.position=spawn-forward*12+left*90;
        Quaternion facing=Quaternion.LookRotation(forward);
        basin.rotation=facing;
        Vector3 basinPoint=basin.position;
        Undo.RecordObject(world.SculptData,"Sculpt Daniel cave and basin");
        world.BeginSculptStroke();
        try
        {
            for(int i=0;i<3;i++) world.Sculpt(TerrainSculptTool.Flatten,basinPoint,43,1,basinPoint.y);
            Vector3 smooth=Object.FindFirstObjectByType<HangingSpire>().transform.position+left*18;
            world.Sculpt(TerrainSculptTool.Smooth,smooth,18,.65f,smooth.y);
            Vector3 main=spawn+forward*53;
            float mainHeight=spawn.y+4;
            for(int i=0;i<3;i++) world.Sculpt(TerrainSculptTool.Flatten,main,22,1,mainHeight);
            // Carve the starting passage so the shell is not buried in generated terrain.
            for(int i=0;i<=5;i++) world.Sculpt(TerrainSculptTool.Carve,spawn+forward*(i*5)+Vector3.up*7,8,18,0);
            Vector3 exit=cave.TransformPoint(new Vector3(0,8.5f,34));
            Vector3 arrival=main; arrival.y=mainHeight;
            for(int i=0;i<=6;i++)
            {
                Vector3 p=Vector3.Lerp(exit,arrival,i/6f);
                world.Sculpt(TerrainSculptTool.Flatten,p,7,1,p.y);
            }
            Vector3 gate=basinPoint-left*32;
            for(int i=0;i<=12;i++)
            {
                Vector3 p=Vector3.Lerp(arrival,gate,i/12f);
                world.Sculpt(TerrainSculptTool.Flatten,p,9,1,p.y);
                world.Sculpt(TerrainSculptTool.Carve,p+Vector3.up*6,5,8,0);
            }
        }
        finally { world.EndSculptStroke(); EditorUtility.SetDirty(world.SculptData); }
        Remove(root.Find("Connection to Asura basin"));
        Remove(basin.Find("Basin floor"));
        SetBox(cave,"Left cave wall",new Vector3(-9,7,14),new Vector3(2,18,36));
        SetBox(cave,"Right cave wall",new Vector3(9,7,14),new Vector3(2,18,36));
        SetBox(cave,"Cave roof",new Vector3(0,16,14),new Vector3(20,2,36));
        SetBox(cave,"Cave back",new Vector3(0,7,-4),new Vector3(20,18,2));
        SetBox(cave,"Mushroom exit ledge",new Vector3(0,3.25f,31),new Vector3(18,10.5f,6));
        var stone=Material("CaveStone",new Color(.18f,.17f,.21f));
        foreach(var r in cave.GetComponentsInChildren<MeshRenderer>())
            if(r.GetComponentInParent<BounceShroom>()==null) r.sharedMaterial=stone;
        for(int side=-1;side<=1;side+=2) for(int i=0;i<6;i++)
        {
            var rock=GameObject.CreatePrimitive(PrimitiveType.Sphere);rock.name="Cave rock buttress";
            rock.layer=7;rock.transform.SetParent(cave,false);
            rock.transform.localPosition=new Vector3(side*8.3f,4+(i%2)*2,1+i*5);
            rock.transform.localScale=new Vector3(3.4f,10+(i%3)*2,6);
            rock.GetComponent<Renderer>().sharedMaterial=stone;
        }
        Lamp(cave,"Pearl cave fill",new Vector3(0,4,3),new Color(.8f,.76f,.9f),1.6f,18);
        Lamp(cave,"Mushroom glow",new Vector3(0,5,23),new Color(1,.35f,.4f),2.2f,18);
        Lamp(cave,"Exit light",new Vector3(0,13,30),new Color(.8f,.95f,.85f),3,22);
        foreach(var shroom in cave.GetComponentsInChildren<BounceShroom>())
            shroom.transform.localPosition=new Vector3(shroom.transform.localPosition.x,.4f,25);
        var player=Object.FindFirstObjectByType<SmoothFirstPersonController>();
        var orientation=player.transform.Find("Orientation");
        if(orientation!=null) { Undo.RecordObject(orientation,"Face cave exit");orientation.rotation=facing;PrefabUtility.RecordPrefabInstancePropertyModifications(orientation); }
        Set(world,"placePlayerOnSurface",false);
        var entrance=basin.Find("Encounter Entrance"); entrance.position=basinPoint-left*32;
        // Keep the authored actors above the sculpted floor; the giant has the eastern half.
        var units=basin.GetComponentsInChildren<Creature>();
        int melee=0,ranged=0,general=0,launcher=0;
        foreach(var unit in units)
        {
            Vector3 p;
            if(unit is MeleeAsura) { int i=melee++;p=new Vector3(-20+(i%6)*5,.15f,-13+(i/6)*8); }
            else if(unit is RangedAsura) { int i=ranged++;p=new Vector3(-20+(i%3)*8,.15f,8+(i/3)*8); }
            else if(unit is GeneralAsura) { int i=general++;p=new Vector3(-18+i*9,.15f,25); }
            else { int i=launcher++;p=basin.InverseTransformPoint(entrance.position)+new Vector3((i-1)*3,.6f,0); }
            unit.transform.localPosition=p;
        }
        BuildEcology(root,world,spawn+forward*45+left*12+Vector3.up*4,facing);
        BuildGiant(root,world,basin);
        BuildRain(root,world,basin);
        new GameObject("Experiment authored v2").transform.SetParent(root,false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
    }
    private static void BuildGiant(Transform root,ProceduralWorld world,Transform basin)
    {
        OreColossusSetup.CreateAssets();
        var anchor=new GameObject("Crystal Giant Roaming Anchor").transform;anchor.SetParent(root,false);
        anchor.position=basin.TransformPoint(new Vector3(19,0,7));anchor.rotation=basin.rotation;
        var go=new GameObject("Walking Crystal Colossus");go.transform.SetParent(root,false);go.transform.position=anchor.position;
        var giant=go.AddComponent<OreColossus>();
        Set(giant,"world",world);Set(giant,"roamingAnchor",anchor);Set(giant,"authoredRoamRadius",14f);
        string folder="Assets/Materials/Creatures/OreColossus/MAT_OreColossus_";
        foreach(string name in new[]{"Rock","Ore","Heart","Geode"})
            Set(giant,char.ToLowerInvariant(name[0])+name.Substring(1)+"Material",AssetDatabase.LoadAssetAtPath<Material>(folder+name+".mat"));
        Set(giant,"larvaPrefab",AssetDatabase.LoadAssetAtPath<GeodeLarva>(OreColossusSetup.LarvaPath));
        Set(giant,"initialPolarity",(int)CreaturePolarity.None);Set(giant,"ageRate",0f);Set(giant,"age",100000f);
    }
    private static void BuildRain(Transform root,ProceduralWorld world,Transform basin)
    {
        var go=new GameObject("Basin Gameplay Rain");go.transform.SetParent(root,false);
        var fluid=go.AddComponent<ErosiveFluidSystem>();fluid.SetWorld(world);Set(fluid,"maximumDroplets",512);
        string folder="Assets/Materials/Fluid/";
        Set(fluid,"dropletMaterial",AssetDatabase.LoadAssetAtPath<Material>(folder+"MAT_ErosiveWater.mat"));
        Set(fluid,"debrisMaterial",AssetDatabase.LoadAssetAtPath<Material>(folder+"MAT_ErosionDebris.mat"));
        Set(fluid,"rippleMaterial",AssetDatabase.LoadAssetAtPath<Material>(folder+"MAT_ErosionRipple.mat"));
        var rainObject=new GameObject("Intermittent Basin Storm");rainObject.transform.SetParent(go.transform,false);
        var source=rainObject.AddComponent<ErosiveFluidSource>();source.SetFluid(fluid);
        Set(source,"lifetime",30f);Set(source,"dropletRadius",.12f);Set(source,"erosionRate",.3f);Set(source,"erosionRadius",.6f);
        var rain=rainObject.AddComponent<ErosiveRain>();Set(rain,"region",basin);Set(rain,"regionRadius",32f);
        Set(rain,"firstStormDelay",35f);Set(rain,"clearDuration",new Vector2(30,60));Set(rain,"dropsPerSecond",12f);
    }
    private static void BuildEcology(Transform root,ProceduralWorld world,Vector3 position,Quaternion rotation)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Systems/SquirrelSystemManager.prefab");
        if(source==null) throw new InvalidOperationException("Missing squirrel system prefab.");
        var go=(GameObject)PrefabUtility.InstantiatePrefab(source,root);go.name="Starting Basin Squirrel Grove";go.transform.position=position;go.transform.rotation=rotation;
        var manager=go.GetComponent<SquirrelSystemManager>();Set(manager,"autoDiscoverScene",false);Set(manager,"population",3);Set(manager,"initialFoodCount",9);
        Set(manager,"foodPerCycle",1);Set(manager,"maxUnstoredFood",12);Set(manager,"territoryRadius",9f);
        var trees=new SquirrelTree[3];
        for(int i=0;i<3;i++)
        {
            var tree=GameObject.CreatePrimitive(PrimitiveType.Cylinder);tree.name="Organic grove tree";tree.layer=7;tree.transform.SetParent(go.transform,false);
            tree.transform.localPosition=new Vector3((i-1)*7,2.5f,i%2*5);tree.transform.localScale=new Vector3(.8f,2.5f,.8f);
            tree.GetComponent<Renderer>().sharedMaterial=Material("OrganicBark",CreatureArtPalette.Organic*.4f);
            trees[i]=tree.AddComponent<SquirrelTree>();
            var canopy=GameObject.CreatePrimitive(PrimitiveType.Sphere);canopy.name="Coral canopy";Object.DestroyImmediate(canopy.GetComponent<Collider>());
            canopy.transform.SetParent(tree.transform,false);canopy.transform.localPosition=Vector3.up;canopy.transform.localScale=new Vector3(5,1.2f,5);
            canopy.GetComponent<Renderer>().sharedMaterial=Material("OrganicCanopy",CreatureArtPalette.Organic);
        }
        var settings=new SerializedObject(manager);var anchors=settings.FindProperty("trees");anchors.arraySize=3;
        for(int i=0;i<3;i++) anchors.GetArrayElementAtIndex(i).objectReferenceValue=trees[i];settings.ApplyModifiedPropertiesWithoutUndo();
        var colonyObject=new GameObject("Starting Basin Pearl Bees");colonyObject.transform.SetParent(root,false);colonyObject.transform.position=position+rotation*new Vector3(12,0,4);
        var colony=colonyObject.AddComponent<GlowBeeColony>();Set(colony,"nestCount",1);Set(colony,"beesPerNest",4);Set(colony,"flowersPerNest",5);Set(colony,"spawnSpider",false);
        var anchor=new GameObject("Pearl Hive Anchor").transform;anchor.SetParent(colonyObject.transform,false);
        settings=new SerializedObject(colony);anchors=settings.FindProperty("nestAnchors");anchors.arraySize=1;anchors.GetArrayElementAtIndex(0).objectReferenceValue=anchor;settings.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void ApplyPalette()
    {
        string marker=ArtFolder+"/PaletteApplied.txt";
        if(AssetDatabase.LoadAssetAtPath<TextAsset>(marker)!=null) return;
        foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Materials","Assets/Prefabs/Creatures"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);string n=path.ToLowerInvariant();
            if(n.Contains("/asura/") || n.Contains("/colorExperiment/".ToLowerInvariant())) continue;
            bool rotate=new[]{"goo","fuzz","octopus","organic","compost","bird","skyweb","skyspider","growth","time","bonecomb","heat","fire","squirrel"}.Any(n.Contains);
            if(!rotate) continue;
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            Undo.RecordObject(material,"Rotate element palette");
            var shader=material.shader;
            for(int i=0;i<ShaderUtil.GetPropertyCount(shader);i++)
                if(ShaderUtil.GetPropertyType(shader,i)==ShaderUtil.ShaderPropertyType.Color)
                { string property=ShaderUtil.GetPropertyName(shader,i);material.SetColor(property,CreatureArtPalette.Rotate(material.GetColor(property))); }
            EditorUtility.SetDirty(material);
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Creatures/Asura"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var prefab=PrefabUtility.LoadPrefabContents(path);
            if(prefab.GetComponent<AsuraRingVisual>()==null) prefab.AddComponent<AsuraRingVisual>();
            PrefabUtility.SaveAsPrefabAsset(prefab,path);PrefabUtility.UnloadPrefabContents(prefab);
        }
        // The marker is an ordinary imported text asset, keeping this migration repeatable.
        System.IO.File.WriteAllText(ArtFolder+"/PaletteApplied.txt","Organic red; Time green; Heat blue. Palette v1.");AssetDatabase.ImportAsset(ArtFolder+"/PaletteApplied.txt");
    }
    private static Material Material(string name,Color color)
    {
        string path=ArtFolder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m!=null) return m;m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=color;AssetDatabase.CreateAsset(m,path);return m;
    }
    private static void SetBox(Transform parent,string name,Vector3 position,Vector3 scale) { var t=parent.Find(name);t.localPosition=position;t.localScale=scale; }
    private static void Remove(Transform target) { if(target!=null) Object.DestroyImmediate(target.gameObject); }
    private static void Lamp(Transform parent,string name,Vector3 position,Color color,float intensity,float range)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;}
    private static void Set(Object target,string field,object value)
    {
        var so=new SerializedObject(target);var p=so.FindProperty(field);if(p==null) throw new InvalidOperationException(target.GetType().Name+"."+field);
        if(value is Object obj) p.objectReferenceValue=obj;else if(value==null)p.objectReferenceValue=null;else if(value is bool b)p.boolValue=b;
        else if(value is int i)p.intValue=i;else if(value is float f)p.floatValue=f;else if(value is Vector2 v)p.vector2Value=v;
        so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
