using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Replaces only Scene_LeafGarden with a sculptable meadow and a walkable stone overlook.</summary>
public static class LeafGardenLandscapeSetup
{
    public const string Folder="Assets/VFX/LeafGarden/Landscape";
    [MenuItem("Tools/Creatures/Leaf Garden/Create Golden Meadow Landscape")]
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var previous=SceneManager.GetActiveScene();
        if(previous.isDirty && previous.path!=LeafGardenShowcaseSetup.ScenePath) throw new InvalidOperationException("Save unrelated scene edits first.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var terrainMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        if(terrainMaterial==null) throw new InvalidOperationException("The shared world terrain material is missing.");
        var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_Black.mat");
        if(sky==null) throw new InvalidOperationException("The selected sky material is missing.");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
        bool oldClosed=false;
        try
        {
            RenderSettings.skybox=sky; RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(0.68f,0.73f,0.73f); RenderSettings.ambientEquatorColor=new Color(0.52f,0.49f,0.37f);
            RenderSettings.ambientGroundColor=new Color(0.22f,0.2f,0.14f);
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared; RenderSettings.fogColor=new Color(0.73f,0.74f,0.66f); RenderSettings.fogDensity=0.0025f;
            var sun=new GameObject("Warm afternoon light").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.5f;
            sun.color=new Color(1,0.89f,0.7f); sun.shadows=LightShadows.Soft; sun.transform.rotation=Quaternion.Euler(33,-28,0); RenderSettings.sun=sun;
            var world=new GameObject("Golden Meadow - sculptable terrain").AddComponent<ProceduralWorld>();
            var settings=new SerializedObject(world);
            settings.FindProperty("worldSize").vector2Value=new Vector2(384,448);
            settings.FindProperty("cellSize").floatValue=2; settings.FindProperty("chunkCells").intValue=16;
            settings.FindProperty("seed").intValue=816;
            settings.FindProperty("surfaceMaterial").objectReferenceValue=terrainMaterial; settings.FindProperty("innerMaterial").objectReferenceValue=terrainMaterial;
            settings.FindProperty("placePlayerOnSurface").boolValue=false; settings.FindProperty("blockingRadius").floatValue=90;
            settings.FindProperty("meshesPerFrame").intValue=16;
            var recipe=settings.FindProperty("generationRecipe"); recipe.FindPropertyRelative("kind").enumValueIndex=(int)TerrainRecipeKind.GoldenMeadow;
            recipe.FindPropertyRelative("verticalBounds").vector2Value=new Vector2(-18,70);
            recipe.FindPropertyRelative("safeSpawn").vector3Value=new Vector3(0,39,-112); settings.ApplyModifiedPropertiesWithoutUndo();
            var terrain=AssetDatabase.LoadAssetAtPath<TerrainSculptData>(Folder+"/GoldenMeadowTerrain.asset");
            if(terrain==null)
            {
                terrain=ScriptableObject.CreateInstance<TerrainSculptData>();
                terrain.Freeze(world.CaptureLayout(out float[] checks),checks,"Golden Meadow overlook");
                AssetDatabase.CreateAsset(terrain,Folder+"/GoldenMeadowTerrain.asset");
            }
            settings.Update(); settings.FindProperty("sculptData").objectReferenceValue=terrain; settings.ApplyModifiedPropertiesWithoutUndo();
            string cachePath=Folder+"/GoldenMeadowChunks.bytes";
            if(!File.Exists(cachePath)) { File.WriteAllBytes(cachePath,world.BuildChunkCache()); AssetDatabase.ImportAsset(cachePath); }
            world.ChunkCache=AssetDatabase.LoadAssetAtPath<TextAsset>(cachePath); world.Generate(true);
            var surface=BakeSurface(world);
            var show=new GameObject("Leaf Garden meadow controls").AddComponent<LeafGardenShowcase>();
            show.Landscape=true; show.ShowControls=false; show.DemoBodies=new Rigidbody[0]; show.Gardens=new LeafGarden[3];
            var prefab=LeafGardenShowcaseSetup.EnsurePrefab();
            var prefabRoot=PrefabUtility.LoadPrefabContents(LeafGardenShowcaseSetup.PrefabPath);
            try
            {
                var defaults=prefabRoot.GetComponent<LeafGarden>();
                defaults.RandomizeEventSizes=true;defaults.RandomizeFirstDelay=true;
                defaults.RestSeconds=new Vector2(180,270);defaults.FirstDelaySeconds=new Vector2(60,300);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot,LeafGardenShowcaseSetup.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            prefab=AssetDatabase.LoadAssetAtPath<GameObject>(LeafGardenShowcaseSetup.PrefabPath);
            for(int i=0;i<3;i++)
            {
                var patch=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene); patch.name="Meadow leaf colony "+(i+1);
                Vector3 p=new Vector3((i-1)*100,0,0); p.y=surface.HeightAt(p); patch.transform.position=p;
                var garden=patch.GetComponent<LeafGarden>(); garden.Area=new Vector2(100,280); garden.Surface=surface; garden.Seed=8123+i*1437;
                garden.RandomizeEventSizes=true; garden.RandomizeFirstDelay=true; garden.RestSeconds=new Vector2(540,900);
                garden.FirstDelaySeconds=i==1?new Vector2(15,25):new Vector2(90,600);
                var leaves=patch.GetComponent<LeafGardenPresentation>(); leaves.LeafCount=100000; leaves.Coverage=0.95f; leaves.DetailDistance=48; leaves.DistantDensity=0.6f;
                show.Gardens[i]=garden;
            }
            show.Garden=show.Gardens[0]; show.Leaves=show.Garden.GetComponent<LeafGardenPresentation>(); show.Launch=show.Garden.GetComponent<LeafTornadoLaunchVolume>();
            var observer=new GameObject("Meadow observation camera").AddComponent<Camera>(); observer.tag="MainCamera";
            observer.clearFlags=CameraClearFlags.Skybox; observer.farClipPlane=750; observer.nearClipPlane=0.08f; observer.enabled=false;
            observer.transform.position=new Vector3(6,39.5f,-108); observer.transform.LookAt(new Vector3(8,9,35)); observer.gameObject.AddComponent<AudioListener>(); show.Observer=observer;
            var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerObjects.prefab"),scene);
            show.Player=player.GetComponentInChildren<SmoothFirstPersonController>();
            Vector3 spawn=new Vector3(0,0,-112); spawn.y=surface.HeightAt(spawn)+1.25f; show.PlayerSpawn=spawn; show.Player.transform.position=spawn;
            var playerCamera=player.GetComponentInChildren<Camera>(true);
            foreach(var camera in player.GetComponentsInChildren<Camera>(true)) { camera.enabled=true; camera.farClipPlane=750; camera.clearFlags=CameraClearFlags.Skybox; }
            foreach(var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null) continue;
                var serialized=new SerializedObject(component); var property=serialized.GetIterator();
                while(property.NextVisible(true)) if(property.propertyType==SerializedPropertyType.ObjectReference && property.type.Contains("Camera")) property.objectReferenceValue=playerCamera;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Dress(world,surface,terrainMaterial);
            var loaded=SceneManager.GetSceneByPath(LeafGardenShowcaseSetup.ScenePath);
            if(loaded.IsValid() && loaded!=scene) oldClosed=EditorSceneManager.CloseScene(loaded,true);
            if(!EditorSceneManager.SaveScene(scene,LeafGardenShowcaseSetup.ScenePath)) throw new IOException("Could not save the meadow scene.");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("ArtCaptures/LeafGarden");
            File.WriteAllText("ArtCaptures/LeafGarden/landscape-build.txt",$"Sculptable meadow 384x448m; 3 colonies, 100000 leaves each; shared world terrain and sky materials; bare cliff; ground map {surface.Map.width}x{surface.Map.height}; terrain heights {surface.HeightRange}; mesh cache {world.UsedChunkCache}; spawn {spawn}.");
        }
        catch { if(!oldClosed) { EditorSceneManager.CloseScene(scene,true); if(previous.IsValid()) SceneManager.SetActiveScene(previous); } throw; }
    }
    public static LeafGardenSurface BakeSurface(ProceduralWorld world)
    {
        const int resolution=513;
        var surface=AssetDatabase.LoadAssetAtPath<LeafGardenSurface>(Folder+"/MeadowLeafSurface.asset");
        if(surface==null) { surface=ScriptableObject.CreateInstance<LeafGardenSurface>(); AssetDatabase.CreateAsset(surface,Folder+"/MeadowLeafSurface.asset"); }
        var map=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/MeadowHeightNormals.asset");
        if(map==null) { map=new Texture2D(resolution,resolution,TextureFormat.RGBAFloat,false,true) { name="Meadow heights and normals",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp }; AssetDatabase.CreateAsset(map,Folder+"/MeadowHeightNormals.asset"); }
        var pixels=new Color[resolution*resolution]; var hits=new RaycastHit[12]; float min=100,max=-100;
        Physics.SyncTransforms();
        for(int z=0;z<resolution;z++) for(int x=0;x<resolution;x++)
        {
            Vector3 p=new Vector3(-192+x*384f/(resolution-1),120,-224+z*448f/(resolution-1));
            int count=Physics.RaycastNonAlloc(p,Vector3.down,hits,180,1<<7,QueryTriggerInteraction.Ignore);
            bool found=false; RaycastHit best=default;
            for(int i=0;i<count;i++) if(world.OwnsCollider(hits[i].collider) && (!found || hits[i].point.y>best.point.y)) {found=true;best=hits[i];}
            // Keep the overlook and its approach bare; A controls leaves independently of ground support.
            bool bareCliff=p.z<-80 && (Mathf.Abs(p.x)<36 || found && best.point.y>24);
            pixels[z*resolution+x]=found?new Color(best.point.y,best.normal.x,best.normal.z,bareCliff?0:1):new Color(0,0,0,0);
            if(found) { min=Mathf.Min(min,best.point.y);max=Mathf.Max(max,best.point.y); }
        }
        map.SetPixels(pixels);map.Apply(false,false);surface.Map=map;surface.WorldRect=new Vector4(-192,-224,384,448);surface.HeightRange=new Vector2(min,max);
        EditorUtility.SetDirty(map);EditorUtility.SetDirty(surface);return surface;
    }
    private static void Dress(ProceduralWorld world,LeafGardenSurface surface,Material material)
    {
        var random=new System.Random(12);
        for(int i=0;i<46;i++)
        {
            float x=((float)random.NextDouble()-0.5f)*340,z=((float)random.NextDouble()-0.5f)*350;
            if(Mathf.Abs(x)<30 && z<-80) continue;
            var rock=GameObject.CreatePrimitive(PrimitiveType.Cube);rock.name="Weathered meadow stone";rock.layer=7;
            rock.transform.position=new Vector3(x,surface.HeightAt(new Vector3(x,0,z))-0.2f,z);
            rock.transform.rotation=Quaternion.Euler((float)random.NextDouble()*28,(float)random.NextDouble()*180,(float)random.NextDouble()*24);
            float s=0.7f+(float)random.NextDouble()*2.4f;rock.transform.localScale=new Vector3(s*1.5f,s*0.65f,s);
            rock.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
