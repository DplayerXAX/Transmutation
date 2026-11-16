using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Run in an isolated project with -executeMethod SilkForestValidation.RunBatch.</summary>
public static class SilkForestValidation
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; Debug.Log("SILK CHECK PASS: " + message);
    }
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CheckMembraneGeometry();
            var test = new GameObject("Parameter checks").AddComponent<SilkForestManager>();
            SilkForestSetup.AssignMaterials(test);
            test.forestSize = new Vector2(12, 12); test.treeDensity = 4; test.minimumSpacing = 2;
            test.crownCountRange = new Vector2Int(3, 3); test.trunkCrownCountRange = new Vector2Int(2, 2);
            test.heightRange = new Vector2(10, 10); test.vineDensity = 0;
            test.Regenerate();
            Check(test.GeneratedTreeCount == 6, "Density produces six trees in 144 square metres.");
            Check(test.GeneratedCrownCount == 30, "Exactly three upper crowns and two stem buds per tree.");
            Check(test.GeneratedVineCount == 0, "Zero vine density creates no connections.");
            var original = test.GeneratedRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            test.Regenerate();
            Check(original.SequenceEqual(test.GeneratedRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices), "Same seed reproduces exact geometry.");
            test.vineDensity = 1; test.maximumVineDistance = 50; test.yJunctionChance=0; test.Regenerate();
            Check(test.GeneratedYJunctionCount==0,"Zero Y chance produces pair connections only.");
            Check(test.GeneratedVineCount > 0 && test.GeneratedVineCount <= test.GeneratedTreeCount * test.maximumVinesPerTree / 2,
                "Dense vines connect trees while respecting the per-tree cap.");
            test.yJunctionChance=1;test.Regenerate();
            Check(test.GeneratedYJunctionCount>0,"Eligible triples produce shared Y junctions.");
            Check(test.GeneratedRoot.GetComponentsInChildren<MeshFilter>().Count(f=>f.gameObject.name.StartsWith("Y junction"))==test.GeneratedYJunctionCount,
                "Each Y is saved as one connected three-arm mesh.");
            foreach (var filter in test.GeneratedRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Check(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)) &&
                    mesh.normals.All(n=>float.IsFinite(n.x)&&float.IsFinite(n.y)&&float.IsFinite(n.z)&&n.sqrMagnitude>0.5f) && mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), "Finite mesh and valid indices: " + mesh.name);
            }
            test.treeDensity = 0; test.Regenerate();
            Check(test.GeneratedTreeCount == 0 && test.GeneratedCrownCount == 0, "Zero density clears all trees and crowns.");
            test.treeDensity = 10; test.forestSize = Vector2.zero; test.minimumSpacing = 100; test.Regenerate();
            Check(test.GeneratedTreeCount <= 1, "Impossible spacing and tiny bounds terminate without overlap.");
            test.forestSize = new Vector2(12, 12); test.minimumSpacing = 2; test.treeDensity = 2;
            test.Regenerate();
            var autosaveScene = test.gameObject.scene;
            EditorSceneManager.SaveScene(autosaveScene, "Assets/SaveHookCheck.unity");
            Check(test.GeneratedRoot.GetComponentsInChildren<MeshFilter>().All(f => EditorUtility.IsPersistent(f.sharedMesh)),
                "Saving a regenerated scene automatically persists its meshes.");
            Object.DestroyImmediate(test.gameObject);
            var forest = SilkForestSetup.BuildDemo();
            Check(forest.GeneratedTreeCount == Mathf.RoundToInt(forest.forestSize.x*forest.forestSize.y*forest.treeDensity/100), "Demo respects the saved density setting.");
            int expectedCrowns = forest.GeneratedCrownCount, expectedVines = forest.GeneratedVineCount;
            int vertices = forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);
            Check(vertices > 10000, "Real geometry is generated, including perforated crowns.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SilkForestSetup.PrefabPath);
            Check(prefab != null && prefab.GetComponentsInChildren<MeshFilter>().All(f => EditorUtility.IsPersistent(f.sharedMesh)), "Saved prefab references persistent baked meshes.");
            var sharedMeshes = prefab.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
            var instance = Object.Instantiate(prefab).GetComponent<SilkForestManager>();
            instance.treeDensity = 0; instance.Regenerate();
            Check(sharedMeshes.All(m => m != null && m.vertexCount > 0), "Regenerating one prefab instance preserves shared baked meshes.");
            Object.DestroyImmediate(instance.gameObject);
            EditorSceneManager.OpenScene(SilkForestSetup.ScenePath);
            forest = Object.FindFirstObjectByType<SilkForestManager>();
            Check(forest.GeneratedCrownCount == expectedCrowns && forest.GeneratedVineCount == expectedVines &&
                forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), "Scene reload preserves the baked forest.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Capture(forest);
            foreach (string shaderName in new[] { "Capstone/Silk Forest/Pearlescent Silk", "Capstone/Silk Forest/Thin Film" })
            {
                Shader shader = Shader.Find(shaderName);
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compiles: " + shaderName);
            }
            string report = $"PASS: {checks} checks\nTrees: {forest.GeneratedTreeCount}\nCrowns: {expectedCrowns}\nVine connections: {expectedVines}\nY junctions: {forest.GeneratedYJunctionCount}\nVertices: {vertices}\n";
            Directory.CreateDirectory("Validation"); File.WriteAllText("Validation/silk-forest-results.txt", report);
            Debug.Log(report); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void RunMaterialBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(SilkForestSetup.ScenePath);
            var forest=Object.FindFirstObjectByType<SilkForestManager>();
            SilkForestSetup.AssignMaterials(forest);
            Check(forest.silkMaterial==forest.strandMaterial,"Crown and trunk use the same pearlescent material asset.");
            Check(forest.silkMaterial.GetFloat("_SilkStyle")==0,"Earlier pearlescent crown shading is restored.");
            Check(forest.silkMaterial.GetColor("_BaseColor")==forest.strandMaterial.GetColor("_BaseColor"),"Crown and trunk share the same silver-white base colour.");
            Check(forest.silkMaterial.GetColor("_ShadowColor")==forest.strandMaterial.GetColor("_ShadowColor"),"Crown and trunk share shadow tones.");
            foreach(string property in new[]{"_SilkSheen","_FibreStrength","_VertexTint","_Iridescence","_Smoothness","_Emission"})
                Check(forest.silkMaterial.GetFloat(property)==forest.strandMaterial.GetFloat(property),"Matching silk response: "+property);
            Check(forest.silkMaterial.shader==forest.strandMaterial.shader&&!ShaderUtil.ShaderHasError(forest.silkMaterial.shader),"Shared silk shader compiles.");
            AssetDatabase.SaveAssets();
            Capture(forest);
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/silk-material-results.txt",$"PASS: {checks} material checks\nShared earlier silver-blue pearlescent crown material for crown and trunk.\nExisting baked geometry preserved.\n");
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }

    private static float SurfaceArea(SilkForestGeometry.MeshBuffer data)
    {
        float area=0;
        for(int i=0;i<data.indices.Count;i+=3)
        {
            var a=data.vertices[data.indices[i]];var b=data.vertices[data.indices[i+1]];var c=data.vertices[data.indices[i+2]];
            area+=Vector3.Cross(b-a,c-a).magnitude*0.5f;
        }
        return area;
    }

    private static void CheckMembraneGeometry()
    {
        Vector3 loopStart=Vector3.up,loopEnd=Vector3.up*4;
        Check(Vector3.Distance(SilkForestGeometry.ReturningStrandPoint(loopStart,loopEnd,Vector3.right,0.8f,0),loopStart)<0.0001f&&
            Vector3.Distance(SilkForestGeometry.ReturningStrandPoint(loopStart,loopEnd,Vector3.right,0.8f,1),loopEnd)<0.0001f,
            "Returning side strands meet the trunk at both ends.");
        Check(SilkForestGeometry.ReturningStrandPoint(loopStart,loopEnd,Vector3.right,0.8f,0.5f).x>0.79f,
            "Returning strands bow away from the trunk only in the middle.");
        var fan=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.SilkBundle(fan,t=>Vector3.up*t*2,0.1f,0.12f,0.25f,0.014f,0.3f,new System.Random(5),0.94f,
            0.85f,65,0.72f,0.32f,0,9,true);
        var tips=fan.vertices.Where(v=>v.y>1.999f).ToArray();
        Check(tips.Max(v=>v.x)-tips.Min(v=>v.x)>0.3f&&tips.Max(v=>v.z)-tips.Min(v=>v.z)>0.2f,
            "Loose root fibre tips stay spread apart instead of reconverging into a knot.");
        var looseBundle=new SilkForestGeometry.MeshBuffer();var denseBundle=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.SilkBundle(looseBundle,t=>Vector3.up*t*8,0.16f,0.17f,0.4f,0.014f,0.3f,new System.Random(18),0.94f);
        SilkForestGeometry.SilkBundle(denseBundle,t=>Vector3.up*t*8,0.16f,0.17f,0.4f,0.014f,0.3f,new System.Random(18),0.05f);
        Check(SurfaceArea(looseBundle)<SurfaceArea(denseBundle)*0.6f,"Strand openness separates fibres and reduces opaque area.");
        var middleStrands=looseBundle.vertices.Where(v=>v.y>3&&v.y<5).ToArray();
        Check(middleStrands.Max(v=>v.x)-middleStrands.Min(v=>v.x)>0.08f&&middleStrands.Max(v=>v.z)-middleStrands.Min(v=>v.z)>0.08f,
            "Silk ribbons occupy a spatial bundle visible from multiple directions.");
        var bundleMesh=looseBundle.CreateMesh("Ribbon bundle normals");
        Check(bundleMesh.normals.All(n=>float.IsFinite(n.x)&&float.IsFinite(n.y)&&float.IsFinite(n.z)&&n.sqrMagnitude>0.5f),
            "Closed flattened strands have finite surface normals and capped tips.");
        Object.DestroyImmediate(bundleMesh);
        Vector3 junction=Vector3.up*0.9f;
        var roots=SilkForestGeometry.RootLayout(Vector3.zero,junction,5,new Vector2(0.7f,1.3f),1,new System.Random(81));
        Check(roots.Select(r=>r.start).Distinct().Count()==roots.Length,"Root fibres originate from separate trunk positions instead of one knot.");
        Check(roots.Max(r=>r.start.y)-roots.Min(r=>r.start.y)>0.2f,"Root release heights are staggered along the trunk.");
        Check(roots.Count(r=>r.grounded)>=2,"Forked roots retain at least two grounded anchors.");
        Check(roots.Any(r=>!r.grounded&&r.end.y>0.1f),"Some root tips curl above ground.");
        Check(roots.All(r=>r.start.y>0.8f),"Roots split from an elevated trunk junction.");
        Check(roots.All(r=>SilkForestGeometry.ForkPoint(r,0.5f).y>0.2f),"Root arms arch above ground between their endpoints.");
        Check(roots.Select(r=>new Vector2(r.end.x,r.end.z)).Distinct().Count()==5,"Roots land at separate asymmetric positions.");
        var forkGeometry=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.ForkedRoots(forkGeometry,roots,0.16f,0.014f,0.95f,0.85f,65,0.72f,0.32f,new System.Random(5));
        Check(forkGeometry.vertices.Count>1000,"Root forks contain substantial perforated membrane geometry.");
        Vector2 opening=new Vector2(0.85f,0.3f);
        var vertical=Enumerable.Range(0,48).Select(i=>SilkForestGeometry.HoleOutline(i*Mathf.PI/24,opening,Mathf.PI/2,0,Vector2.one)).ToArray();
        Check(vertical.Max(p=>p.y)-vertical.Min(p=>p.y)>(vertical.Max(p=>p.x)-vertical.Min(p=>p.x))*2,
            "Hole generator supports genuinely vertical ellipses.");
        var diagonal=Enumerable.Range(0,48).Select(i=>SilkForestGeometry.HoleOutline(i*Mathf.PI/24,opening,Mathf.PI/4,0,Vector2.one)).ToArray();
        Check(diagonal.Sum(p=>p.x*p.y)>1,"Hole generator supports diagonal major axes.");
        float side=SilkForestGeometry.HoleOutline(0,Vector2.one,0,1,Vector2.one).magnitude;
        float corner=SilkForestGeometry.HoleOutline(Mathf.PI/3,Vector2.one,0,1,Vector2.one).magnitude;
        Check(corner>side*1.7f,"Rounded triangular pores have three distinct corners.");
        var closedRoot=new SilkForestGeometry.MeshBuffer();var openRoot=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.WebColumn(closedRoot,t=>Vector3.up*t*8,0.16f,0.7f,0.6f,0.014f,0.4f,new System.Random(9),0.94f,0.85f,65,0.72f,0.32f,0);
        SilkForestGeometry.WebColumn(openRoot,t=>Vector3.up*t*8,0.16f,0.7f,0.6f,0.014f,0.4f,new System.Random(9),0.94f,0.85f,65,0.72f,0.32f,1);
        float RootArea(SilkForestGeometry.MeshBuffer data)
        {
            float area=0;
            for(int i=0;i<data.indices.Count;i+=3)
            {
                Vector3 a=data.vertices[data.indices[i]],b=data.vertices[data.indices[i+1]],c=data.vertices[data.indices[i+2]];
                if((a.y+b.y+c.y)/3<1.4f)area+=Vector3.Cross(b-a,c-a).magnitude*0.5f;
            }
            return area;
        }
        Check(RootArea(openRoot)<RootArea(closedRoot)*0.85f,"Root openness removes substantial solid skirt area.");
        var paired=SilkForestGeometry.StemPoreLayout(15,new System.Random(42),0.72f);
        Check(paired.Any(c=>c.minimum.x>0),"Trunk pore layout contains side-by-side small holes.");
        Check(paired.Any(c=>c.maximum.x==1&&c.minimum.x==0&&!c.attachment),"Paired holes mix with larger single holes.");
        Check(paired.Where(c=>c.minimum.x==0).Select(c=>c.maximum.y-c.minimum.y).Distinct().Count()>8,"Trunk pore rows have irregular heights.");
        Check(paired.Where(c=>c.minimum.x>0).Select(c=>c.minimum.x).Distinct().Count()>2,"Side-by-side splits shift across the membrane.");
        var singles=SilkForestGeometry.StemPoreLayout(15,new System.Random(42),0);
        Check(singles.All(c=>c.minimum.x==0&&c.maximum.x==1),"Zero paired-hole ratio produces single-hole patches.");
        bool translated=true;
        foreach(var field in typeof(SilkForestManager).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
        {
            if(field.GetCustomAttribute<HideInInspector>()!=null)continue;
            var label=field.GetCustomAttribute<InspectorNameAttribute>();var tip=field.GetCustomAttribute<TooltipAttribute>();
            if(label==null||tip==null||!System.Text.RegularExpressions.Regex.IsMatch(label.displayName,@"[\u4e00-\u9fff]")||
                !System.Text.RegularExpressions.Regex.IsMatch(tip.tooltip,@"[\u4e00-\u9fff]"))translated=false;
        }
        Check(translated,"All exposed manager parameters have Chinese labels and tooltips.");
        var profile=new SilkForestGeometry.ColumnProfile(new System.Random(9),0.85f,65);
        Vector2 ca=profile.Corner(0,0.5f),cb=profile.Corner(1,0.5f),cc=profile.Corner(2,0.5f),cd=profile.Corner(3,0.5f);
        Check(Mathf.Abs(Vector2.Dot((cb-ca).normalized,(cc-cb).normalized))>0.03f||Mathf.Abs((cb-ca).magnitude-(cc-cb).magnitude)>0.05f,
            "Trunk cross section has unequal edges or non-right angles.");
        Check((profile.Corner(0,0)-profile.Corner(0,1)).magnitude>0.1f,"Cross section changes continuously along height.");
        bool convex=true;
        for(int seed=0;seed<60;seed++)
        {
            var testProfile=new SilkForestGeometry.ColumnProfile(new System.Random(seed),1,120);
            for(int h=0;h<=20;h++)for(int c=0;c<4;c++)
            {
                Vector2 a=testProfile.Corner(c,h/20f),b=testProfile.Corner((c+1)%4,h/20f),d=testProfile.Corner((c+2)%4,h/20f);
                Vector2 u=b-a,v=d-b;if(u.x*v.y-u.y*v.x<=0.01f)convex=false;
            }
        }
        Check(convex,"Random deformed cross sections remain convex and do not fold over.");
        var noRim=new SilkForestGeometry.MeshBuffer();var raisedRim=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Crown(noRim,new SilkForestGeometry.MeshBuffer(),Vector3.zero,2,0,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,0,0.92f,0);
        SilkForestGeometry.Crown(raisedRim,new SilkForestGeometry.MeshBuffer(),Vector3.zero,2,0,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,0,0.92f,0.018f);
        Check(raisedRim.vertices.Max(v=>v.magnitude)>noRim.vertices.Max(v=>v.magnitude)+0.015f,"Crown pore edges have raised geometric hems.");
        Check(raisedRim.vertices.Count>noRim.vertices.Count,"Rims contain extra curved cross-section geometry.");
        var denseStem=new SilkForestGeometry.MeshBuffer();var openStem=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.WebColumn(denseStem,t=>Vector3.up*t*8,0.16f,0.7f,0.6f,0.014f,0.4f,new System.Random(9),0.1f);
        SilkForestGeometry.WebColumn(openStem,t=>Vector3.up*t*8,0.16f,0.7f,0.6f,0.014f,0.4f,new System.Random(9),0.98f);
        Check(SurfaceArea(openStem)<SurfaceArea(denseStem)*0.75f,$"Trunk slider materially increases open area with a fixed seed: solid area ratio {SurfaceArea(openStem)/SurfaceArea(denseStem):F3}.");
        var denseCrown=new SilkForestGeometry.MeshBuffer();var openCrown=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Crown(denseCrown,new SilkForestGeometry.MeshBuffer(),Vector3.zero,2,0.06f,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,0,0.1f);
        SilkForestGeometry.Crown(openCrown,new SilkForestGeometry.MeshBuffer(),Vector3.zero,2,0.06f,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,0,0.98f);
        Check(SurfaceArea(openCrown)<SurfaceArea(denseCrown)*0.75f,"Crown slider materially increases open area with a fixed seed.");
        Check(SilkForestGeometry.Opening(0.5f,0)==0,"Zero openness closes the pores.");
        var pore=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Pore(pore,uv=>new Vector3(uv.x,uv.y,0),uv=>Vector3.forward,
            Vector2.zero,Vector2.one,new Vector2(0.55f,0.7f),0.02f,Color.white,new System.Random(17));
        var mesh=pore.CreateMesh("Thick membrane topology check");
        Check(mesh.bounds.size.z>=0.019f,"Membranes have front/back thickness.");
        var obj=new GameObject("Pore collider check");var collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
        Check(!collider.Raycast(new Ray(new Vector3(0.5f,0.5f,-1),Vector3.forward),out _,2),"The membrane pore is a real open hole.");
        Check(collider.Raycast(new Ray(new Vector3(0.03f,0.5f,-1),Vector3.forward),out _,2),"Opaque tissue surrounds the hole.");
        Object.DestroyImmediate(obj);Object.DestroyImmediate(mesh);
        var column=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.WebColumn(column,t=>Vector3.up*t*8,0.16f,0.7f,0.6f,0.014f,0.4f,new System.Random(9));
        var waist=column.vertices.Where(v=>v.y>3.5f&&v.y<4.5f).ToArray();
        Check(waist.Any(v=>v.x>0.12f)&&waist.Any(v=>v.x< -0.12f)&&waist.Any(v=>v.z>0.12f)&&waist.Any(v=>v.z< -0.12f),
            "Four separate sides enclose a volumetric trunk at its waist.");
        Check(column.vertices.Where(v=>v.y<0.1f).Max(v=>Mathf.Abs(v.x))>0.4f,"Trunk base fans wider than its waist.");
        var shell=new SilkForestGeometry.MeshBuffer();var film=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Crown(shell,film,Vector3.zero,2,0.06f,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,0);
        Check(shell.vertices.All(v=>Mathf.Abs(v.magnitude-2)<0.07f),"Crown including raised rims stays within 3.5 percent of a sphere.");
        mesh=shell.CreateMesh("Crown contact check");obj=new GameObject("Solid lower crown cap");collider=obj.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
        Check(collider.Raycast(new Ray(Vector3.down*3,Vector3.up),out var contact,2)&&Mathf.Abs(contact.point.y+2)<0.05f,
            "Stalk attachment direction hits solid crown tissue.");
        Object.DestroyImmediate(obj);Object.DestroyImmediate(mesh);
        var inside=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Crown(inside,new SilkForestGeometry.MeshBuffer(),Vector3.zero,2,0.06f,0.24f,0.32f,0.2f,new System.Random(22),Vector3.down,3);
        Check(inside.vertices.Count>shell.vertices.Count+5000,"Interior contains substantial crossed cloth sheets.");
    }

    private static void Capture(SilkForestManager forest)
    {
        string output = "ArtCaptures/SilkForest"; Directory.CreateDirectory(output);
        Camera camera = Camera.main;
        camera.enabled = false;
        var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        camera.targetTexture = target;
        void Shot(string name)
        {
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            RenderTexture.active = previous; Object.DestroyImmediate(pixels);
        }
        Shot("ForestOverview");
        var junction=forest.GeneratedRoot.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.gameObject.name.StartsWith("Y junction"));
        Check(junction!=null,"Demo contains a visible Y connection for inspection.");
        foreach(var renderer in forest.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())renderer.enabled=renderer==junction;
        Bounds yBounds=junction.bounds;
        camera.transform.position=yBounds.center+new Vector3(0.2f,0.9f,-0.7f).normalized*yBounds.size.magnitude*1.15f;
        camera.transform.LookAt(yBounds.center);camera.fieldOfView=48;
        Shot("YJunctionDetail");
        foreach(var renderer in forest.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())renderer.enabled=true;
        for (int i = 1; i < forest.GeneratedRoot.childCount; i++) forest.GeneratedRoot.GetChild(i).gameObject.SetActive(false);
        // Aim at an actual generated tree, regardless of seed.
        var first = forest.GeneratedRoot.GetChild(0).GetComponentsInChildren<Renderer>();
        Bounds bounds = first[0].bounds;
        foreach (var renderer in first) bounds.Encapsulate(renderer.bounds);
        Vector3 canopy = new Vector3(bounds.center.x, bounds.max.y - 2, bounds.center.z);
        camera.transform.position = canopy + new Vector3(5, 1.4f, -7.7f);
        camera.transform.LookAt(canopy); camera.fieldOfView = 42;
        Shot("CanopyDetail");
        camera.transform.position=canopy+new Vector3(2,-2.8f,-4);
        camera.transform.LookAt(canopy-Vector3.up*1.1f);camera.fieldOfView=46;
        Shot("ConnectionDetail");
        // Geometry inspection fixtures use the same routines as the real trees.
        // Isolating them keeps stem buds from blocking the inspection camera.
        Vector3 fixtureOrigin=new Vector3(100,0,0);
        var columnData=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.SilkBundle(columnData,t=>fixtureOrigin+Vector3.up*(0.9f+t*7.1f)+Vector3.right*Mathf.Sin(t*3)*0.1f,
            0.16f,0.17f,0.55f,forest.trunkMembraneThickness,forest.tornWebAmount,new System.Random(9),forest.trunkOpenness,forest.trunkIrregularity,forest.trunkTwist,forest.trunkPairedHoleChance,forest.trunkTriangleHoleChance,forest.rootOpenness,9,false,true);
        var fixtureRoots=SilkForestGeometry.RootLayout(fixtureOrigin,fixtureOrigin+Vector3.up*0.9f,4,forest.rootSpreadRange,
            forest.rootCurlChance,new System.Random(32));
        SilkForestGeometry.ForkedRoots(columnData,fixtureRoots,0.16f,forest.trunkMembraneThickness,forest.trunkOpenness,
            forest.trunkIrregularity,forest.trunkTwist,forest.trunkPairedHoleChance,forest.trunkTriangleHoleChance,new System.Random(12));
        var fixture=new GameObject("Four-face trunk inspection");
        var fixtureMesh=columnData.CreateMesh("Four-face membrane stem");
        fixture.AddComponent<MeshFilter>().sharedMesh=fixtureMesh;
        fixture.AddComponent<MeshRenderer>().sharedMaterial=forest.strandMaterial;
        Vector3 trunkTarget=fixtureOrigin+new Vector3(0.08f,3.5f,0);
        camera.transform.position=trunkTarget+new Vector3(0,0,-1.5f);camera.transform.LookAt(trunkTarget);camera.fieldOfView=48;
        Shot("TrunkFront");
        camera.transform.position=trunkTarget+new Vector3(0.95f,0,-1.1f);camera.transform.LookAt(trunkTarget);
        Shot("TrunkQuarter");
        Vector3 rootTarget=fixtureOrigin+Vector3.up*0.6f;
        camera.transform.position=rootTarget+new Vector3(1.7f,0.8f,-2);camera.transform.LookAt(rootTarget);camera.fieldOfView=48;
        Shot("RootDetail");Object.DestroyImmediate(fixture);Object.DestroyImmediate(fixtureMesh);
        var bridgeData=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.WebBridge(bridgeData,fixtureOrigin+Vector3.left*2.5f,fixtureOrigin+Vector3.right*2.5f,0.55f,0.22f,new System.Random(77));
        fixture=new GameObject("Silk tension reference inspection");fixtureMesh=bridgeData.CreateMesh("Tensioned silk bundle");
        fixture.AddComponent<MeshFilter>().sharedMesh=fixtureMesh;fixture.AddComponent<MeshRenderer>().sharedMaterial=forest.strandMaterial;
        camera.transform.position=fixtureOrigin+new Vector3(0,0.7f,-5.8f);camera.transform.LookAt(fixtureOrigin);camera.fieldOfView=52;
        Shot("SilkStrandsDetail");Object.DestroyImmediate(fixture);Object.DestroyImmediate(fixtureMesh);
        // Show actual generated side forks without neighbouring trees blocking them.
        var actualTree=forest.GeneratedRoot.GetChild(0);
        var stemCollider=actualTree.GetComponent<CapsuleCollider>();
        Vector3 branchTarget=stemCollider.transform.TransformPoint(stemCollider.center)+Vector3.up*1.1f;
        camera.transform.position=branchTarget+new Vector3(5,0,-7);camera.transform.LookAt(branchTarget);camera.fieldOfView=48;
        Shot("BranchDetail");
        camera.transform.position=bounds.center+new Vector3(0.7f,0.12f,-1).normalized*bounds.size.y*1.25f;
        camera.transform.LookAt(bounds.center);camera.fieldOfView=48;
        Shot("TreeSilhouette");
        var shellData=new SilkForestGeometry.MeshBuffer();var fullData=new SilkForestGeometry.MeshBuffer();
        SilkForestGeometry.Crown(shellData,new SilkForestGeometry.MeshBuffer(),fixtureOrigin,1.6f,forest.crownAsymmetry,
            forest.crownWebThickness,forest.tornWebAmount,forest.membraneCoverage,new System.Random(51),Vector3.down,0,forest.crownOpenness,forest.crownRimHeight);
        SilkForestGeometry.Crown(fullData,new SilkForestGeometry.MeshBuffer(),fixtureOrigin,1.6f,forest.crownAsymmetry,
            forest.crownWebThickness,forest.tornWebAmount,forest.membraneCoverage,new System.Random(51),Vector3.down,forest.interiorSheetCount,forest.crownOpenness,forest.crownRimHeight);
        var triangles=fullData.indices.ToArray();fullData.indices.Clear();
        for(int i=0;i<triangles.Length;i+=3)
        {
            float z=(fullData.vertices[triangles[i]].z+fullData.vertices[triangles[i+1]].z+fullData.vertices[triangles[i+2]].z)/3;
            if(i>=shellData.indices.Count||z>0.12f)fullData.Triangle(triangles[i],triangles[i+1],triangles[i+2]);
        }
        fixture=new GameObject("Crown cutaway inspection");fixtureMesh=fullData.CreateMesh("Cutaway reveals interior curtains");
        fixture.AddComponent<MeshFilter>().sharedMesh=fixtureMesh;fixture.AddComponent<MeshRenderer>().sharedMaterial=forest.silkMaterial;
        camera.transform.position=fixtureOrigin+new Vector3(0.25f,0.4f,-5);camera.transform.LookAt(fixtureOrigin);camera.fieldOfView=48;
        Shot("CanopyInterior");Object.DestroyImmediate(fixture);Object.DestroyImmediate(fixtureMesh);
        for (int i = 1; i < forest.GeneratedRoot.childCount; i++) forest.GeneratedRoot.GetChild(i).gameObject.SetActive(true);
        camera.transform.position = new Vector3(7.5f, 3.1f, -18);
        camera.transform.LookAt(new Vector3(-1.5f, 6.4f, -1)); camera.fieldOfView = 61;
        Shot("PlayerScale");
        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
    }
}
