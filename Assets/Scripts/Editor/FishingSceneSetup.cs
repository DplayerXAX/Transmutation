using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using Object = UnityEngine.Object;

public static class FishingSceneSetup
{
    public const string ScenePath = "Assets/Scenes/Scene-Fishing.unity";
    const string Folder = "Assets/Prefabs/Creatures/Fishing";
    public const string PolePath = Folder + "/FishingPoleCreature.prefab";
    public const string FishPath = Folder + "/SeaAngelCreature.prefab";
    static Material Material(string name, Color color, bool transparent = false)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", .65f);
        if (transparent)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_ZWrite", 0); m.SetFloat("_Cull", 0);
            m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * .3f);
        }
        AssetDatabase.CreateAsset(m, path); return m;
    }
    static Transform Part(Transform parent, string name, Vector3 point, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.layer = 9;
        go.transform.SetParent(parent, false); go.transform.localPosition = point; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = mat; return go.transform;
    }
    static void Identity(Creature c, string name)
    {
        var s = new SerializedObject(c); s.FindProperty("creatureName").stringValue = name;
        s.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.Light;
        s.ApplyModifiedPropertiesWithoutUndo();
    }
    [MenuItem("Tools/Creatures/Fishing/Create Assets")]
    public static void CreateAssets()
    {
        System.IO.Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var skin = Material("Angel Membrane", new Color(.3f,.6f,1,.32f), true);
        var core = Material("Angel Core", new Color(1,.2f,.5f,.78f), true);
        var poleSkin = Material("Pole Skin", new Color(.18f,.28f,.24f));
        var light = Material("Pole Vein", Color.cyan);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FishPath) == null)
        {
            var root = new GameObject("Sea Angel"); root.layer = 9;
            root.AddComponent<Rigidbody>().useGravity = false;
            var collider = root.AddComponent<CapsuleCollider>(); collider.height = 1; collider.radius = .16f;
            var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
            Part(visual,"Body",Vector3.zero,new Vector3(.27f,.8f,.22f),skin);
            Part(visual,"Tail",new Vector3(0,-.36f,0),new Vector3(.12f,.3f,.1f),skin);
            Part(visual,"Head",new Vector3(0,.35f,0),new Vector3(.28f,.28f,.25f),skin);
            Part(visual,"Left Wing",new Vector3(-.3f,.15f,0),new Vector3(.6f,.22f,.06f),skin);
            Part(visual,"Right Wing",new Vector3(.3f,.15f,0),new Vector3(.6f,.22f,.06f),skin);
            Part(visual,"Left Horn",new Vector3(-.09f,.54f,0),new Vector3(.05f,.2f,.05f),skin);
            Part(visual,"Right Horn",new Vector3(.09f,.54f,0),new Vector3(.05f,.2f,.05f),skin);
            Part(visual,"Core",new Vector3(0,.1f,0),new Vector3(.1f,.45f,.1f),core);
            var fish = root.AddComponent<SeaAngelCreature>(); Identity(fish,"Sea Angel");
            PrefabUtility.SaveAsPrefabAsset(root,FishPath); Object.DestroyImmediate(root);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PolePath) == null)
        {
            var root = new GameObject("Fishing Pole Creature"); root.layer = 9;
            root.AddComponent<Rigidbody>().mass = .7f;
            var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0,0,.3f); collider.size = new Vector3(.3f,.35f,1.05f);
            for (int i=0;i<8;i++) Part(root.transform,"Curved Body",new Vector3(0,.08f*Mathf.Sin(i*.4f),i*.12f),new Vector3(.17f-i*.013f,.17f-i*.013f,.2f),poleSkin);
            Part(root.transform,"Left Fin",new Vector3(-.12f,0,.25f),new Vector3(.25f,.04f,.22f),poleSkin);
            Part(root.transform,"Right Fin",new Vector3(.12f,0,.25f),new Vector3(.25f,.04f,.22f),poleSkin);
            Part(root.transform,"Eye",new Vector3(0,.09f,.3f),Vector3.one*.09f,light);
            var pole = root.AddComponent<FishingPoleCreature>(); Identity(pole,"Fishing Pole Creature");
            var v = root.AddComponent<FishingPoleVisuals>();
            v.tip = new GameObject("Filament Tip").transform; v.tip.SetParent(root.transform,false); v.tip.localPosition = new Vector3(0,.03f,.94f);
            v.vein = Part(root.transform,"Progress Vein",new Vector3(0,.19f,.4f),new Vector3(.065f,.045f,.05f),light); v.veinRenderer = v.vein.GetComponent<Renderer>();
            PrefabUtility.SaveAsPrefabAsset(root,PolePath); Object.DestroyImmediate(root);
        }
        SeaAngelMeshBuilder.Apply();
        UpgradePole();
        AssetDatabase.SaveAssets();
    }
    public static void UpgradePole()
    {
        var root = PrefabUtility.LoadPrefabContents(PolePath);
        try
        {
            var visuals = root.GetComponent<FishingPoleVisuals>();
            var obsolete = root.transform.Find("Counter Pull Organ");
            if (obsolete != null) Object.DestroyImmediate(obsolete.gameObject);
            var pivot = root.transform.Find("Pull Bend");
            if (pivot == null)
            {
                pivot = new GameObject("Pull Bend").transform; pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(0, 0, .18f);
                var children = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in root.transform)
                    if (child == visuals.tip || child == visuals.vein || (child.GetComponent<Renderer>() != null && child.localPosition.z >= .2f)) children.Add(child);
                foreach (var child in children) child.SetParent(pivot, true);
            }
            visuals.bendPivot = pivot;
            var tuning = new SerializedObject(root.GetComponent<FishingPoleCreature>());
            if (tuning.FindProperty("fishingFeelVersion").intValue < 1)
            {
                tuning.FindProperty("drainRate").floatValue = .35f;
                tuning.FindProperty("fishingFeelVersion").intValue = 1;
                tuning.ApplyModifiedPropertiesWithoutUndo();
                var presentation = new SerializedObject(visuals);
                presentation.FindProperty("sidewaysBend").floatValue = 28;
                presentation.FindProperty("verticalBend").floatValue = 20;
                presentation.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, PolePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    [MenuItem("Tools/Creatures/Fishing/Create Standalone Scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        CreateAssets();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            if (!SceneManager.GetSceneByPath(ScenePath).isLoaded) EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(ScenePath)); return;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.18f,.21f,.3f);
        RenderSettings.skybox = null;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Fishing Platform"; floor.layer = 7;
        floor.transform.position = new Vector3(0,-.5f,4); floor.transform.localScale = new Vector3(24,1,24);
        floor.GetComponent<Renderer>().sharedMaterial = Material("Platform",new Color(.035f,.055f,.08f));
        var sun = new GameObject("Soft Light").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.2f; sun.transform.rotation=Quaternion.Euler(45,-30,0);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerObjects.prefab"),scene);
        player.transform.position = new Vector3(0,1.5f,-5);
        foreach (var t in player.GetComponentsInChildren<Transform>(true)) if (t.name == "Orientation" || t.name == "CameraHolder") t.rotation = Quaternion.identity;
        foreach (var cam in player.GetComponentsInChildren<Camera>(true)) { cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(.008f,.012f,.03f); }
        var pole = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PolePath),scene); pole.transform.position=new Vector3(0,.7f,-3);
        for (int i=0;i<8;i++)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FishPath),scene);
            go.name="Dimensional Sea Angel " +(i+1); go.transform.position=new Vector3((i%4-1.5f)*1.7f,1.5f+(i%3)*.45f,3+i/4*2);
            go.GetComponent<SeaAngelCreature>().Configure(Mathf.Lerp(.4f,1,i/7f),117+i*79);
        }
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
    }
}



