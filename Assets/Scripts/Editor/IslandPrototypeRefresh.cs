using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class IslandPrototypeRefresh
{
    const string Folder = "Assets/Prefabs/Creatures/Fishing/";
    [MenuItem("Tools/Creatures/Fishing/Refresh Island Visuals Terrain And Test Creatures")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetSceneByPath(FishingSceneSetup.ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(FishingSceneSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root = PrefabUtility.LoadPrefabContents(Folder + "SeaAngelIsland.prefab");
        try
        {
            UpdateAppearance(root); AddHill(root);
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "SeaAngelIsland.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var frame = Object.FindFirstObjectByType<MovingFrame>();
        if (!frame || frame.gameObject.scene != scene) throw new System.InvalidOperationException("Build the island first.");
        Populate(frame);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }
    public static void UpdateAppearance(GameObject root)
    {
        var old = root.transform.Find("Sea Angel Body Visual");
        if (old) Object.DestroyImmediate(old.gameObject);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSceneSetup.FishPath).transform.Find("Visual");
        var visual = Object.Instantiate(source.gameObject, root.transform).transform;
        visual.name = "Sea Angel Body Visual";
        visual.localPosition = new Vector3(0,-18,0); visual.localRotation = Quaternion.Euler(90,0,0);
        visual.localScale = Vector3.one * 160; // Preserve every authored child proportion.
        var animation = root.GetComponent<SeaAngelIslandVisuals>();
        animation.leftWing = visual.Find("Left Wing"); animation.rightWing = visual.Find("Right Wing");
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
        {
            bool core = renderer.name.Contains("Core");
            string path = Folder + (core ? "Island Angel Core.mat" : "Island Angel Membrane.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Transmutation/Dimensional Angel")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_AngelColor", core ? new Color(1,.36f,.65f,.65f) : new Color(.35f,.8f,1,.32f));
            EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material; renderer.enabled = true; renderer.forceRenderingOff = false;
        }
        foreach (var wing in new[] { animation.leftWing, animation.rightWing })
        {
            var collision = wing.gameObject.AddComponent<BoxCollider>();
            collision.center = new Vector3(wing.name.StartsWith("Left") ? -.28f : .28f,.03f,0);
            collision.size = new Vector3(.55f,.2f,.03f);
        }
        var head = visual.Find("Head");
        if (head && !head.TryGetComponent<Collider>(out _)) head.gameObject.AddComponent<SphereCollider>();
        var shell = root.transform.Find("Body Collision");
        if (shell && shell.TryGetComponent<CapsuleCollider>(out var capsule))
        { capsule.center = new Vector3(0,-18,-20); capsule.radius = 18; capsule.height = 156; }
    }
    public static void AddHill(GameObject root)
    {
        var old = root.transform.Find("Rolling Mesh Terrain"); if (old) Object.DestroyImmediate(old.gameObject);
        const int nx = 20, nz = 32;
        var vertices = new Vector3[(nx+1)*(nz+1)]; var uv = new Vector2[vertices.Length]; var triangles = new int[nx*nz*6];
        for (int z=0;z<=nz;z++) for(int x=0;x<=nx;x++)
        {
            float u=x/(float)nx,v=z/(float)nz; int i=z*(nx+1)+x;
            float bump=Mathf.Sin(u*Mathf.PI)*Mathf.Sin(v*Mathf.PI);
            vertices[i]=new Vector3((u-.5f)*20,.025f+3.5f*bump*bump,(v-.5f)*32); uv[i]=new Vector2(u,v);
        }
        int index=0;
        for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
        {
            int i=z*(nx+1)+x;
            triangles[index++]=i; triangles[index++]=i+nx+1; triangles[index++]=i+1;
            triangles[index++]=i+1; triangles[index++]=i+nx+1; triangles[index++]=i+nx+2;
        }
        string path=Folder+"IslandRollingTerrain.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh=new Mesh{name="Baked rolling island terrain"}; AssetDatabase.CreateAsset(mesh,path); }
        mesh.Clear(); mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        var go=new GameObject("Rolling Mesh Terrain",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider),typeof(IslandTerrainSurface));
        go.layer=7; go.transform.SetParent(root.transform,false); go.transform.localPosition=new Vector3(-12,0,14);
        go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshCollider>().sharedMesh=mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"IslandBack.mat");
        root.GetComponent<MovingFrame>().terrainSurfaces=new Collider[]{go.GetComponent<MeshCollider>()};
    }
    static void Populate(MovingFrame frame)
    {
        foreach(var root in frame.gameObject.scene.GetRootGameObjects()) if(root.name=="Island Test Creatures") return;
        var group=new GameObject("Island Test Creatures");
        GameObject Spawn(string path,string name,Vector3 local,CreaturePolarity polarity=CreaturePolarity.None)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/"+path+".prefab"),frame.gameObject.scene);
            go.name=name; go.transform.SetParent(group.transform,true); go.transform.position=frame.transform.TransformPoint(local);
            if(go.TryGetComponent<Creature>(out var creature))
            {
                var data=new SerializedObject(creature); data.FindProperty("initialPolarity").enumValueIndex=(int)polarity; data.ApplyModifiedPropertiesWithoutUndo();
            }
            if(!go.TryGetComponent<MovementFrameMember>(out _))go.AddComponent<MovementFrameMember>();
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            return go;
        }
        Spawn("Asura/MeleeAsura","Light Melee Asura",new Vector3(-17,2,27),CreaturePolarity.Light);
        Spawn("Asura/MeleeAsura","Dark Melee Asura",new Vector3(17,2,27),CreaturePolarity.Dark);
        Spawn("Asura/RangedAsura","Light Ranged Asura",new Vector3(-17,2,20),CreaturePolarity.Light);
        Spawn("Asura/RangedAsura","Dark Ranged Asura",new Vector3(17,2,20),CreaturePolarity.Dark);
        Spawn("Asura/Chainsaw","Chainsaw — pickup",new Vector3(-3,1,-10));
        for(int i=0;i<3;i++) Spawn("TentacleCreature","Tentacle "+(i+1),new Vector3(-8+i*8,2,3+i*3),i%2==0?CreaturePolarity.Light:CreaturePolarity.Dark);
        for(int i=0;i<5;i++) Spawn("Birds/BirdCreature","Bird "+(i+1),new Vector3(-8+i*4,8+i%2*2,0),i%2==0?CreaturePolarity.Light:CreaturePolarity.Dark);
        Spawn("Bubbles/BubbleFlower","Bubble Flower",new Vector3(13,1,8));
        Spawn("Fire/Fire Flower","Fire Flower",new Vector3(15,1,14));
        Spawn("Compost/CompostCreature","Recycle Colony — on mesh hill",new Vector3(-12,3.6f,14));
        Spawn("RecycleFlower","Portable Recycle Flower",new Vector3(-8,4,18));
        Spawn("Traversal/GooCreature","Light Goo",new Vector3(8,1,-8),CreaturePolarity.Light);
        Spawn("Traversal/GooCreature","Dark Goo",new Vector3(12,1,-6),CreaturePolarity.Dark);
        Spawn("Traversal/BounceShroom","Bounce Shroom",new Vector3(-12,1,-9));
        Spawn("CeilingBatBugEgg","Surface Attachment Egg",new Vector3(-18,1,0));
        Spawn("Bubbles/Bubble","Dragon Fuel Bubble",new Vector3(4,1,-23));
        var showcase=group.AddComponent<IslandBoneShowcase>(); showcase.frame=frame;
        showcase.boneMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_Bone.mat");
        showcase.skinMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"Angel Membrane.mat");
    }
}
