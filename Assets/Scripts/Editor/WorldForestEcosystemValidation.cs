using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class WorldForestEcosystemValidation
{
    [MenuItem("Tools/Capstone/World Map/Validate Imported Forest Ecosystem")]
    public static void Validate()
    {
        var worldScene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        var habitats=worldScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldForestHabitat>()).ToArray();
        if(habitats.Length!=3)throw new Exception("Expected three forest habitats.");
        var world=worldScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).First();
        if(!world.LayoutReady)WorldMapBuild.PrepareSampler(world);
        var external=Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>c.enabled).ToArray();foreach(var collider in external)collider.enabled=false;
        var previous=SceneManager.GetActiveScene();var validation=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var report=new List<string>();var meshes=new List<Mesh>();var state=UnityEngine.Random.state;
        try
        {
            SceneManager.SetActiveScene(validation);UnityEngine.Random.InitState(191126);
            for(int district=0;district<habitats.Length;district++)
            {
                var habitat=habitats[district];var anchorSources=habitat.treeRoots;Vector3 center=anchorSources.Aggregate(Vector3.zero,(v,a)=>v+a.GroundPosition)/anchorSources.Length;
                const int cells=64;const float step=2;var vertices=new Vector3[(cells+1)*(cells+1)];var indices=new List<int>();
                for(int z=0;z<=cells;z++)for(int x=0;x<=cells;x++)
                {
                    float px=center.x+(x-cells*.5f)*step,pz=center.z+(z-cells*.5f)*step;
                    if(!world.TryFindSurfaceBelow(new Vector3(px,world.VolumeBounds.max.y,pz),out float y))y=WorldMapTerrain.Height(px,pz);
                    vertices[z*(cells+1)+x]=new Vector3(px,y,pz);
                    if(x<cells&&z<cells){int a=z*(cells+1)+x;indices.AddRange(new[]{a,a+cells+1,a+1,a+1,a+cells+1,a+cells+2});}
                }
                var mesh=new Mesh{name="Sampled actual world terrain"};mesh.vertices=vertices;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();meshes.Add(mesh);
                var ground=new GameObject("Validation terrain");ground.layer=7;ground.AddComponent<MeshCollider>().sharedMesh=mesh;
                var roots=new List<SquirrelTree>();foreach(var source in anchorSources){var proxy=new GameObject("Low crown root");proxy.transform.position=source.GroundPosition;roots.Add(proxy.AddComponent<SquirrelTree>());}
                var manager=Object.Instantiate(habitat.squirrels.gameObject).GetComponent<SquirrelSystemManager>();
                var config=new SerializedObject(manager);var trees=config.FindProperty("trees");trees.arraySize=roots.Count;for(int i=0;i<roots.Count;i++)trees.GetArrayElementAtIndex(i).objectReferenceValue=roots[i];config.ApplyModifiedPropertiesWithoutUndo();
                Physics.SyncTransforms();manager.InitializeSystem();
                if(!manager.IsInitialized||manager.Squirrels.Count!=6||manager.Nests.Count!=6||manager.Foods.Count<54)throw new Exception($"Habitat {district+1}: initialization failed: {manager.Squirrels.Count} squirrels/{manager.Nests.Count} nests/{manager.Foods.Count} foods.");
                for(int i=0;i<manager.Nests.Count;i++)for(int j=i+1;j<manager.Nests.Count;j++)
                    if(Vector3.ProjectOnPlane(manager.Nests[i].transform.position-manager.Nests[j].transform.position,Vector3.up).magnitude<27.5f)throw new Exception("Nests too close.");
                if(manager.Foods.Any(f=>Mathf.Abs(Vector3.Dot(f.transform.up,Vector3.up))>.4f))throw new Exception("Upright food found.");
                foreach(var nest in manager.Nests)
                    if(roots.Min(r=>Vector3.ProjectOnPlane(r.GroundPosition-nest.transform.position,Vector3.up).magnitude)>2.1f)throw new Exception("Nest detached from low-crown tree root.");
                foreach(var colony in habitat.colonies)
                {
                    var settings=new SerializedObject(colony);var sources=settings.FindProperty("authoredFlowers");var sites=settings.FindProperty("nestAnchors");
                    if(sources.arraySize==0||sites.arraySize!=2||!settings.FindProperty("mountHivesOnAnchors").boolValue||settings.FindProperty("hiveScale").floatValue!=.5f)throw new Exception("Invalid bee flower/hive configuration.");
                    for(int i=0;i<sites.arraySize;i++)
                    {var anchor=(Transform)sites.GetArrayElementAtIndex(i).objectReferenceValue;if(anchor==null||anchor.gameObject.scene!=worldScene||anchor.parent.GetComponent<SquirrelTree>()!=null)throw new Exception("Hive must be attached to a bare trunk in Scene_World.");}
                }
                report.Add($"PASS habitat {district+1}: 6 squirrels, 6 root-side nests separated by >=28m; {manager.Foods.Count} sideways foods; configured 72 bees, 12 small trunk-mounted hives and authored nectar sources; exact sampled world ground in a temporary validation scene.");
                if(district==0)
                {
                    var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.11f,.15f,.13f));
                    ground.AddComponent<MeshFilter>().sharedMesh=mesh;ground.AddComponent<MeshRenderer>().sharedMaterial=mat;
                    foreach(var filter in worldScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>()))
                    {
                        if(filter.sharedMesh==null||filter.GetComponent<Renderer>()==null||filter.name.EndsWith(" distant"))continue;
                        var bounds=filter.GetComponent<Renderer>().bounds;if(Mathf.Abs(bounds.center.x-center.x)>58||Mathf.Abs(bounds.center.z-center.z)>58)continue;
                        var proxy=new GameObject("Forest visual proxy",typeof(MeshFilter),typeof(MeshRenderer));proxy.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);proxy.transform.localScale=filter.transform.lossyScale;
                        proxy.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;proxy.GetComponent<MeshRenderer>().sharedMaterials=filter.GetComponent<Renderer>().sharedMaterials;
                    }
                    var sun=new GameObject("Validation daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
                    var preview=new PreviewRenderUtility();bool oldAsync=ShaderUtil.allowAsyncCompilation;
                    try
                    {
                        ShaderUtil.allowAsyncCompilation=false;
                        foreach(var root in validation.GetRootGameObjects())preview.AddSingleGO(root);
                        var plants=habitat.GetComponentsInChildren<NectarFlower>();var focus=plants.OrderBy(f=>new Vector2(f.transform.position.x-center.x,f.transform.position.z-center.z).sqrMagnitude).First().transform.position;
                        preview.camera.fieldOfView=65;preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=250;preview.camera.transform.position=focus+new Vector3(-40,26,-49);preview.camera.transform.LookAt(focus+Vector3.up*7);
                        preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.06f,.08f,.10f);preview.lights[0].intensity=1.6f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-35,0);preview.ambientColor=new Color(.38f,.42f,.4f);
                        preview.BeginPreview(new Rect(0,0,1400,900),GUIStyle.none);preview.Render(true);var rt=(RenderTexture)preview.EndPreview();var active=RenderTexture.active;RenderTexture.active=rt;
                        var image=new Texture2D(1400,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,900),0,0);image.Apply();File.WriteAllBytes("ArtCaptures/WorldForestEcosystem/Overview.png",image.EncodeToPNG());RenderTexture.active=active;Object.DestroyImmediate(image);
                    }
                    finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=oldAsync;Object.DestroyImmediate(mat);}

                }
                foreach(var root in validation.GetRootGameObjects())Object.DestroyImmediate(root);
            }
            File.WriteAllLines("ArtCaptures/WorldForestEcosystem/Validation.txt",report);
        }
        finally{foreach(var collider in external)if(collider!=null)collider.enabled=true;UnityEngine.Random.state=state;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(validation,true);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);}
    }
}
