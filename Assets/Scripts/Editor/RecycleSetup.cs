using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable asset migration. Retains the original colony prefab and script GUIDs.</summary>
public static class RecycleSetup
{
    public const string FlowerPath = "Assets/Prefabs/Creatures/RecycleFlower.prefab";
    const string Folder = "Assets/Generated/Recycle";

    [MenuItem("Tools/Creatures/Recycle/Create Or Update Fungal Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before asset migration.");
        EnsureFolder(Folder); EnsureFolder("Assets/Resources");
        Material organic = MaterialAt(Folder + "/RecycleOrganic.mat", "Transmutation/Recycle Organic");
        MaterialAt("Assets/Resources/SurfaceGoo.mat", "Transmutation/Surface Goo");
        var goo = AssetDatabase.LoadAssetAtPath<GooCreature>(GooCreatureSetup.PrefabPath);
        if (goo == null) throw new InvalidOperationException("Create Goo assets first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CompostCreatureSetup.PrefabPath) == null)
        {
            var created = new GameObject("Recycle");
            try
            { created.AddComponent<CompostCreature>(); PrefabUtility.SaveAsPrefabAsset(created, CompostCreatureSetup.PrefabPath); }
            finally { Object.DestroyImmediate(created); }
        }
        Mesh flowerMesh;
        var builder = new GameObject("Flower model builder");
        try
        {
            var visual = builder.AddComponent<RecycleFlowerVisual>();
            flowerMesh = SaveMesh(Folder + "/RecycleFlower.asset", visual.Build());
        }
        finally { Object.DestroyImmediate(builder); }
        bool flowerExists = AssetDatabase.LoadAssetAtPath<GameObject>(FlowerPath) != null;
        GameObject flowerRoot = flowerExists ? PrefabUtility.LoadPrefabContents(FlowerPath) : new GameObject("Recycle Flower");
        try
        {
            var flower = flowerRoot.GetComponent<RecycleFlower>(); if (flower == null) flower = flowerRoot.AddComponent<RecycleFlower>();
            var body = flowerRoot.GetComponent<Rigidbody>(); body.mass = 0.5f; body.isKinematic = false; body.useGravity = true;
            var capsule = flowerRoot.GetComponent<CapsuleCollider>(); capsule.radius = 0.11f; capsule.height = 0.82f; capsule.center = new Vector3(0f,0.38f,0f);
            Set(flower, "seedPrefab", AssetDatabase.LoadAssetAtPath<CompostCreature>(CompostCreatureSetup.PrefabPath));
            Set(flower, "gooPrefab", goo);
            var visual = flowerRoot.GetComponent<RecycleFlowerVisual>(); Set(visual, "organicMaterial", organic); Set(visual, "model", flowerMesh);
            visual.EnsureModel();
            PrefabUtility.SaveAsPrefabAsset(flowerRoot, FlowerPath);
        }
        finally { if (flowerExists) PrefabUtility.UnloadPrefabContents(flowerRoot); else Object.DestroyImmediate(flowerRoot); }

        GameObject root = PrefabUtility.LoadPrefabContents(CompostCreatureSetup.PrefabPath);
        try
        {
            var colony = root.GetComponent<CompostCreature>(); root.name = "Recycle";
            StripPlatform(root);
            Set(colony, "creatureName", "Recycle"); Set(colony, "gooPrefab", goo);
            Set(colony, "flowerPrefab", AssetDatabase.LoadAssetAtPath<RecycleFlower>(FlowerPath));
            var geometry = root.GetComponent<RecycleNetworkGeometry>(); if (geometry == null) geometry = root.AddComponent<RecycleNetworkGeometry>();
            Set(geometry, "strandMaterial", organic);
            root.GetComponent<Rigidbody>().isKinematic = true; root.GetComponent<Rigidbody>().useGravity = false;
            PrefabUtility.SaveAsPrefabAsset(root, CompostCreatureSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("Recycle assets updated: rooted fungal colony, portable procedural flower, and surface goo.");
    }

    [MenuItem("Tools/Creatures/Recycle/Migrate Recycles In Open Scenes")]
    public static void MigrateOpenScenes()
    {
        CreateAssets();
        foreach (CompostCreature colony in Object.FindObjectsByType<CompostCreature>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if (!colony.gameObject.scene.IsValid() || EditorUtility.IsPersistent(colony)) continue;
            Undo.RegisterFullObjectHierarchyUndo(colony.gameObject,"Migrate Recycle");
            StripPlatform(colony.gameObject);
            if (!colony.TryGetComponent<RecycleNetworkGeometry>(out var geometry)) geometry = Undo.AddComponent<RecycleNetworkGeometry>(colony.gameObject);
            Set(geometry,"strandMaterial",AssetDatabase.LoadAssetAtPath<Material>(Folder+"/RecycleOrganic.mat"));
            Set(colony,"flowerPrefab",AssetDatabase.LoadAssetAtPath<RecycleFlower>(FlowerPath));
            EditorSceneManager.MarkSceneDirty(colony.gameObject.scene);
        }
        BakeUnreadableSurfaces();
    }
    static void StripPlatform(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            if (CreatureBounds.BelongsTo(renderer.transform,root.transform)) renderer.enabled=false;
        foreach (var solid in root.GetComponentsInChildren<Collider>(true))
            if (CreatureBounds.BelongsTo(solid.transform,root.transform)) solid.enabled=false;
        if (root.TryGetComponent<CompostRideSurface>(out var ride)) ride.enabled=false;
    }
    static void BakeUnreadableSurfaces()
    {
        foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
        {
            Mesh mesh=collider.sharedMesh;
            if(mesh==null || mesh.isReadable || collider.GetComponentInParent<Creature>()!=null)continue;
            var proxy=collider.GetComponent<SurfaceMeshProxy>();if(proxy==null)proxy=Undo.AddComponent<SurfaceMeshProxy>(collider.gameObject);
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var points=new Unity.Collections.NativeArray<Vector3>(mesh.vertexCount,Unity.Collections.Allocator.Temp);
                try { data[0].GetVertices(points);proxy.vertices=points.ToArray(); }
                finally { points.Dispose(); }
                var indices=new System.Collections.Generic.List<int>();
                for(int i=0;i<data[0].subMeshCount;i++)
                {
                    var sub=data[0].GetSubMesh(i);
                    var values=new Unity.Collections.NativeArray<int>(sub.indexCount,Unity.Collections.Allocator.Temp);
                    try { data[0].GetIndices(values,i);indices.AddRange(values.ToArray()); }
                    finally { values.Dispose(); }
                }
                proxy.source=mesh;proxy.triangles=indices.ToArray();EditorUtility.SetDirty(proxy);
            }
        }
    }
    static Material MaterialAt(string path,string shaderName)
    {
        Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader=Shader.Find(shaderName);if(shader==null)throw new InvalidOperationException("Missing shader "+shaderName);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
        EditorUtility.SetDirty(material);return material;
    }
    static Mesh SaveMesh(string path,Mesh mesh)
    {
        Mesh saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;
    }
    static void Set(Object target,string field,Object value)
    {var data=new SerializedObject(target);data.FindProperty(field).objectReferenceValue=value;data.ApplyModifiedPropertiesWithoutUndo();}
    static void Set(Object target,string field,string value)
    {var data=new SerializedObject(target);data.FindProperty(field).stringValue=value;data.ApplyModifiedPropertiesWithoutUndo();}
    static void EnsureFolder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
}
