using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class LeafGardenShowcaseSetup
{
    public const string ScenePath="Assets/Scenes/Scene_LeafGarden.unity";
    public const string Folder="Assets/VFX/LeafGarden";
    public const string PrefabPath="Assets/Prefabs/Creatures/LeafGarden.prefab";

    [MenuItem("Tools/Creatures/Leaf Garden/Create Reusable Prefab")]
    public static void CreateReusablePrefab() => Selection.activeObject=EnsurePrefab();

    public static GameObject EnsurePrefab()
    {
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(existing!=null) return existing;
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before creating the prefab.");
        Directory.CreateDirectory("Assets/Prefabs/Creatures"); AssetDatabase.Refresh();
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/GoldenLeaf.asset");
        var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/GoldenLeaves.mat");
        var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(Folder+"/LeafGarden.compute");
        if(mesh==null || material==null || compute==null) throw new InvalidOperationException("Create the Leaf Garden showcase assets first.");
        var root=new GameObject("LeafGarden"); root.SetActive(false);
        try
        {
            var garden=root.AddComponent<LeafGarden>();
            var settings=new SerializedObject(garden); settings.FindProperty("creatureName").stringValue="Leaf Garden";
            settings.FindProperty("initialPolarity").enumValueIndex=(int)CreaturePolarity.None;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var presentation=root.AddComponent<LeafGardenPresentation>();
            presentation.LeafMesh=mesh; presentation.LeafMaterial=material; presentation.Simulation=compute;
            root.AddComponent<LeafTornadoLaunchVolume>(); root.SetActive(true);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            if(prefab==null) throw new IOException("Could not save Leaf Garden prefab.");
            AssetDatabase.SaveAssets(); return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/Creatures/Leaf Garden/Add Garden to Current Scene")]
    public static void AddGardenToCurrentScene()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(EnsurePrefab(),SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(instance,"Add Leaf Garden"); Selection.activeGameObject=instance;
        EditorSceneManager.MarkSceneDirty(instance.scene);
    }
    [MenuItem("Tools/Creatures/Leaf Garden/Create and Open Showcase")]
    public static void CreateAndOpen()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Create(); EditorSceneManager.OpenScene(ScenePath);
    }
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var shader=Shader.Find("Transmutation/Golden Leaf Garden");
        if(shader==null) throw new InvalidOperationException("Leaf shader has not imported yet.");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/GoldenLeaf.asset");
        if(mesh==null) { mesh=BuildLeaf(); AssetDatabase.CreateAsset(mesh,Folder+"/GoldenLeaf.asset"); }
        var leafMat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/GoldenLeaves.mat");
        if(leafMat==null) { leafMat=new Material(shader) { name="Golden Leaves", enableInstancing=true }; AssetDatabase.CreateAsset(leafMat,Folder+"/GoldenLeaves.mat"); }
        var ground=Material("Earth",new Color(0.15f,0.18f,0.12f));
        var stone=Material("Stone",new Color(0.28f,0.31f,0.26f));
        var teal=Material("Wanderer",new Color(0.16f,0.5f,0.48f));
        var dark=Material("Eyes",new Color(0.025f,0.04f,0.03f));
        var propMat=Material("Seedpods",new Color(0.48f,0.22f,0.08f));
        Scene previous=SceneManager.GetActiveScene();
        bool previousClosed=false;
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(0.7f,0.73f,0.65f);
            RenderSettings.ambientEquatorColor=new Color(0.48f,0.47f,0.34f);
            RenderSettings.ambientGroundColor=new Color(0.22f,0.24f,0.18f);
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared;
            RenderSettings.fogColor=new Color(0.31f,0.39f,0.36f); RenderSettings.fogDensity=0.0025f;
            var light=new GameObject("Late afternoon sun").AddComponent<Light>();
            light.type=LightType.Directional; light.color=new Color(1,0.87f,0.62f); light.intensity=1.6f;
            light.transform.rotation=Quaternion.Euler(32,-35,0); light.shadows=LightShadows.Soft;
            var floor=Part(null,"Garden floor",PrimitiveType.Cube,new Vector3(0,-0.35f,0),new Vector3(112,0.7f,112),ground,true); floor.layer=7;
            for(int side=-1;side<=1;side+=2)
            {
                Part(null,"Garden edge",PrimitiveType.Cube,new Vector3(side*50,0.12f,0),new Vector3(0.6f,0.24f,100),stone,true).layer=7;
                Part(null,"Garden edge",PrimitiveType.Cube,new Vector3(0,0.12f,side*50),new Vector3(100,0.24f,0.6f),stone,true).layer=7;
            }
            var rand=new System.Random(51);
            for(int i=0;i<26;i++)
            {
                float a=i*Mathf.PI*2/26;
                var rock=Part(null,"Perimeter stone",PrimitiveType.Sphere,new Vector3(Mathf.Cos(a)*53,0.6f,Mathf.Sin(a)*53),new Vector3(1.7f+(float)rand.NextDouble()*3,1.3f+(float)rand.NextDouble()*2,2.3f),stone,true); rock.layer=7;
            }
            var root=new GameObject("Leaf Garden - collective creature"); root.SetActive(false);
            var garden=root.AddComponent<LeafGarden>();
            var so=new SerializedObject(garden); so.FindProperty("initialPolarity").enumValueIndex=0;
            so.FindProperty("creatureName").stringValue="Leaf Garden"; so.ApplyModifiedPropertiesWithoutUndo();
            var presentation=root.AddComponent<LeafGardenPresentation>();
            presentation.Simulation=AssetDatabase.LoadAssetAtPath<ComputeShader>(Folder+"/LeafGarden.compute");
            presentation.LeafMesh=mesh; presentation.LeafMaterial=leafMat;
            var launch=root.AddComponent<LeafTornadoLaunchVolume>(); root.SetActive(true);
            var show=new GameObject("Leaf Garden showcase").AddComponent<LeafGardenShowcase>();
            show.gameObject.AddComponent<LeafGardenBenchmark>();
            show.Garden=garden; show.Leaves=presentation; show.Launch=launch;
            var camera=new GameObject("Garden observation camera").AddComponent<Camera>();
            camera.tag="MainCamera"; camera.nearClipPlane=0.08f; camera.farClipPlane=400;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=RenderSettings.fogColor;
            camera.transform.position=new Vector3(85,46,-104); camera.transform.LookAt(new Vector3(0,30,0));
            camera.fieldOfView=62; show.Observer=camera; presentation.ViewCamera=camera;
            // Player scripts resolve Camera.main in Awake. Let them bind the real player camera
            // before the showcase switches to its observer in Start.
            camera.enabled=false;
            camera.gameObject.AddComponent<AudioListener>();
            var playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerObjects.prefab");
            if(playerPrefab==null) throw new InvalidOperationException("Project player prefab is missing.");
            var player=(GameObject)PrefabUtility.InstantiatePrefab(playerPrefab,scene);
            show.Player=player.GetComponentInChildren<SmoothFirstPersonController>();
            if(show.Player==null) throw new InvalidOperationException("Player prefab needs SmoothFirstPersonController.");
            var playerCamera=player.GetComponentInChildren<Camera>(true);
            // Bind camera references on this scene instance explicitly: showcase script order
            // must never redirect hands, climbing, or interactions to the observation camera.
            foreach(var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null) continue;
                var serialized=new SerializedObject(component); var property=serialized.GetIterator();
                while(property.NextVisible(true))
                    if(property.propertyType==SerializedPropertyType.ObjectReference && property.type.Contains("Camera"))
                        property.objectReferenceValue=playerCamera;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            show.Player.transform.position=new Vector3(0,1.3f,-18);
            foreach(var c in player.GetComponentsInChildren<Camera>(true)) { c.enabled=true; c.farClipPlane=250; }
            foreach(var listener in player.GetComponentsInChildren<AudioListener>(true)) listener.enabled=false;
            var bodies=new List<Rigidbody>();
            for(int i=0;i<3;i++)
            {
                var walker=new GameObject("Garden wanderer "+(i+1)); walker.SetActive(false);
                walker.transform.position=new Vector3(-20+i*17,0.68f,-3+i*3);
                var body=walker.AddComponent<Rigidbody>(); body.mass=1.5f; body.constraints=RigidbodyConstraints.FreezeRotation;
                body.interpolation=RigidbodyInterpolation.Interpolate;
                var collider=walker.AddComponent<CapsuleCollider>(); collider.radius=0.4f; collider.height=1.25f;
                var friction=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder+"/WalkerFriction.physicMaterial");
                if(friction==null)
                {
                    friction=new PhysicsMaterial("Garden walker friction") {staticFriction=0,dynamicFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum};
                    AssetDatabase.CreateAsset(friction,Folder+"/WalkerFriction.physicMaterial");
                }
                collider.sharedMaterial=friction;
                var colliderSettings=new SerializedObject(collider);
                colliderSettings.FindProperty("m_Material").objectReferenceValue=friction;
                colliderSettings.ApplyModifiedPropertiesWithoutUndo();
                var visual=Part(walker.transform,"Visual",PrimitiveType.Capsule,Vector3.zero,new Vector3(0.8f,0.63f,0.8f),teal,false);
                Part(visual.transform,"Eye",PrimitiveType.Sphere,new Vector3(0.45f,0.32f,-0.19f),Vector3.one*0.18f,dark,false);
                Part(visual.transform,"Eye",PrimitiveType.Sphere,new Vector3(0.45f,0.32f,0.19f),Vector3.one*0.18f,dark,false);
                var creature=walker.AddComponent<LeafGardenWalker>(); creature.Visual=visual.transform; creature.Speed=4+i*3;
                so=new SerializedObject(creature); so.FindProperty("initialPolarity").enumValueIndex=0; so.ApplyModifiedPropertiesWithoutUndo();
                walker.SetActive(true); bodies.Add(body);
            }
            for(int i=0;i<7;i++)
            {
                float a=i*2.39996f;
                var prop=Part(null,"Loose seedpod",PrimitiveType.Sphere,new Vector3(Mathf.Cos(a)*(1+i*0.5f),0.5f,Mathf.Sin(a)*(1+i*0.5f)),Vector3.one*(0.45f+i*0.06f),propMat,true);
                var body=prop.AddComponent<Rigidbody>(); body.mass=0.5f+i*0.25f; body.interpolation=RigidbodyInterpolation.Interpolate; bodies.Add(body);
            }
            show.DemoBodies=bodies.ToArray();
            // Unity refuses to save an additive scene over a path that is already loaded.
            // Unload only this generated showcase before replacing its asset, preserving other scenes.
            if(previous.IsValid() && previous.path==ScenePath)
            {
                previousClosed=EditorSceneManager.CloseScene(previous,true);
                if(!previousClosed) throw new InvalidOperationException("Could not unload the old Leaf Garden showcase.");
            }
            if(!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Could not save Leaf Garden scene.");
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if(!previousClosed)
            {
                EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
        }
    }
    private static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor",color); m.SetFloat("_Smoothness",0.1f); AssetDatabase.CreateAsset(m,path); }
        return m;
    }
    private static GameObject Part(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,bool solid)
    {
        var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material; if(!solid) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    public static Mesh BuildLeaf()
    {
        // Serrated outline around a raised midrib. Real geometry means no transparent overdraw.
        var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var indices=new List<int>();
        vertices.Add(new Vector3(0,0.025f,0)); uv.Add(new Vector2(0.5f,0.5f));
        const int ring=18;
        for(int i=0;i<ring;i++)
        {
            float a=i*Mathf.PI*2/ring;
            float width=(i%2==0?1:0.8f)*0.16f;
            var p=new Vector3(Mathf.Sin(a)*width,0,Mathf.Cos(a)*0.34f);
            vertices.Add(p); uv.Add(new Vector2(p.x/0.32f+0.5f,p.z/0.68f+0.5f));
        }
        for(int i=0;i<ring;i++) { indices.Add(0); indices.Add(1+i); indices.Add(1+(i+1)%ring); }
        var mesh=new Mesh {name="Golden folded serrated leaf"}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
}
