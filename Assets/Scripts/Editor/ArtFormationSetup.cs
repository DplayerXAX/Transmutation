using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds an isolated shader-by-palette comparison from the actual Art Reference assets.</summary>
public static class ArtFormationSetup
{
    public const string SourceScene = "Assets/Scenes/Scene_ArtReference.unity";
    public const string ScenePath = "Assets/Scenes/Art Formation.unity";
    public const string AssetRoot = "Assets/ArtFormation";
    private const float Column = 7.6f, Row = 8.4f;
    private static readonly string[] SpecimenNames = { "Blob", "Crystal", "Bone Dragon", "Leaf" };

    private sealed class SourceStyle
    {
        public Material material;
        public string category, label;
        public readonly List<string> sources = new List<string>();
    }

    [MenuItem("Tools/Capstone/Art Direction/Build Art Formation")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Build Art Formation in Edit mode.");
        var source = SceneManager.GetSceneByPath(SourceScene);
        bool openedSource = !source.IsValid() || !source.isLoaded;
        if (openedSource) source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
        var existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.IsValid() && existing.isDirty) throw new InvalidOperationException("Save changes to Art Formation before rebuilding it.");

        EnsureFolder(AssetRoot);
        EnsureFolder(AssetRoot + "/Shaders");
        EnsureFolder(AssetRoot + "/Meshes");
        var nodes = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var materialSources = nodes.Select(t => t.GetComponent<Renderer>()).Where(r => r)
            .SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith("Assets/", StringComparison.Ordinal))
            .Distinct().OrderBy(m => m.name, StringComparer.Ordinal).ToArray();
        var styles = CollectStyles(materialSources);
        var palette = Palette();
        var models = new[] {
            nodes.Where(t => t.name == "Wave Dome" && HierarchyPath(t).Contains("Poster: Heat"))
                .OrderByDescending(t => t.GetComponent<Renderer>().bounds.size.sqrMagnitude).First(),
            nodes.First(t => t.name == "Crystals" && t.parent.name == "Light (back right)"),
            nodes.Where(t => t.name == "Bone Child").OrderBy(t => CombinedBounds(t).size.sqrMagnitude).First(),
            nodes.First(t => t.name == "Leaf Membrane" && t.parent.name == "Leaf Growth")
        };
        var modelPaths = models.Select(HierarchyPath).ToArray();
        var meshes = models.Select((t,i) => BakeSpecimen(t, SpecimenNames[i], i)).ToArray();
        var groupMesh = BakeSampleGroup(meshes);
        WritePaletteInclude();
        var shaders = styles.Select(s => s.material.shader).Distinct().ToDictionary(s => s, MakePreviewShader);

        // Keep generated materials together as subassets, with stable names for inspection and reuse.
        string catalogPath = AssetRoot + "/ArtFormationCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<ArtFormationCatalog>(catalogPath);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<ArtFormationCatalog>(); AssetDatabase.CreateAsset(catalog, catalogPath); }
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(catalogPath).Where(o => o is Material)) Object.DestroyImmediate(sub, true);
        catalog.sourceScene = SourceScene;
        catalog.specimenSources = modelPaths;
        catalog.specimens = meshes;
        catalog.palette = palette;
        catalog.styles = new ArtFormationCatalog.Style[styles.Count];

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root = new GameObject("Art Formation — Shader × Color").transform;
        var samples = Group(root, "Sample Grid");
        var labels = Group(root, "Labels and Color Keys");
        var display = Group(root, "Neutral Display");
        var customRenderers = new List<Renderer>();
        TextMesh customLabel = null;
        Material plate = BoardMaterial(catalog, "Board / Charcoal", new Color(.095f,.105f,.12f));
        Material checker = BoardMaterial(catalog, "Board / Light checks", new Color(.36f,.38f,.4f));
        Material separator = BoardMaterial(catalog, "Board / Dividers", new Color(.035f,.04f,.05f));

        for (int r = 0; r < styles.Count; r++)
        {
            SourceStyle style = styles[r];
            var rowRoot = Group(samples, (r+1).ToString("D2") + " | " + style.category + " | " + style.label);
            rowRoot.localPosition = new Vector3(0,0,r*Row);
            var entry = new ArtFormationCatalog.Style { name=style.label, category=style.category,
                sourceShader=style.material.shader.name, sourceMaterials=style.sources.ToArray(), colors=new Material[palette.Length] };
            catalog.styles[r] = entry;
            for (int c = 0; c < palette.Length; c++)
            {
                var color = palette[c];
                var material = new Material(style.material) { name=(r+1).ToString("D2")+" "+style.label+" / "+color.name };
                material.shader = shaders[style.material.shader];
                NeutralizeColors(material, style.material);
                if (material.HasProperty("_AFPalette")) material.SetColor("_AFPalette", color.color);
                else if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color.color);
                material.enableInstancing = true;
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                AssetDatabase.AddObjectToAsset(material, catalog);
                entry.colors[c] = material;
                var cell = Group(rowRoot, color.name + " | Blob · Crystal · Bone Dragon · Leaf");
                cell.localPosition = new Vector3(c*Column,0,0);
                // One shared mesh contains all four specimens: one draw per treatment instead of four.
                cell.gameObject.AddComponent<MeshFilter>().sharedMesh=groupMesh;
                var renderer=cell.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=material;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off;
                renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                renderer.allowOcclusionWhenDynamic=false;
                if(color.family=="Custom") customRenderers.Add(renderer);
                Label(labels,(r+1).ToString("D2")+" / "+color.name,new Vector3(c*Column,.01f,r*Row-3.25f),.15f,new Color(.86f,.88f,.92f));
            }
            Label(labels,(r+1).ToString("D2")+"  "+style.label.Replace(" / ","\n")+"\n"+style.category,new Vector3(-4.2f,.02f,r*Row),.18f,Color.white,TextAnchor.MiddleRight);
            // Repeat the row key so zoomed-in views remain identifiable across the wide palette.
            for (int c=9;c<palette.Length;c+=9)
                Label(labels,(r+1).ToString("D2")+"  "+style.label,new Vector3(c*Column,.025f,r*Row+3.55f),.14f,new Color(.65f,.68f,.73f));
        }
        BuildSupports(display,styles.Count,palette.Length,plate,checker);
        for(int c=0;c<palette.Length;c++)
        {
            var color=palette[c];
            var key=BoardMaterial(catalog,"Key / "+color.name,color.color);
            var keyRenderer=Box(display,"Color key / "+color.name,new Vector3(c*Column,-.02f,-5.1f),new Vector3(6.9f,.08f,.5f),key);
            var keyLabel=Label(labels,color.name+"\n"+ColorUtility.ToHtmlStringRGB(color.color),new Vector3(c*Column,.02f,-6.6f),.27f,Color.white);
            if(color.family=="Custom") { customRenderers.Add(keyRenderer); customLabel=keyLabel; }
        }
        Label(labels,"ART FORMATION",new Vector3(-4f,.04f,-12f),.85f,Color.white,TextAnchor.MiddleLeft);
        Label(labels,styles.Count+" SURFACE STYLES  ×  "+palette.Length+" COLORS  ×  4 SHAPES\n"
            +"Rows: shader behavior and patterns   |   Columns: palette\n"
            +"Wheel: zoom   ·   Right/middle drag or WASD: pan   ·   Shift: faster\n"
            +"Home: whole board   ·   End: detail   ·   C: custom column   ·   Page Up/Down: row\n"
            +"Select the Art Formation root: Custom Color opens the color picker",
            new Vector3(42f,.04f,-11.8f),.26f,new Color(.78f,.81f,.87f),TextAnchor.MiddleLeft);
        Box(display,"Title rule",new Vector3((palette.Length-1)*Column*.5f,-.06f,-9f),new Vector3(palette.Length*Column,.06f,.08f),separator);
        SetupLighting(root,styles.Count,palette.Length);
        var tuner=root.gameObject.AddComponent<ArtFormationColorTuner>();
        tuner.customColor=palette.Last().color;
        tuner.Configure(customRenderers.ToArray(),customLabel);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        if (existing.IsValid() && existing.isLoaded) EditorSceneManager.CloseScene(existing,true);
        EditorSceneManager.SaveScene(scene,ScenePath);
        if (openedSource) EditorSceneManager.CloseScene(source,true);
        else if (!source.isDirty) EditorSceneManager.CloseScene(source,true);
        SceneManager.SetActiveScene(scene);
        var camera=root.GetComponentInChildren<Camera>();
        if(SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(new Vector3(8,0,8),Quaternion.Euler(65,0,0),34,false,true);
        Selection.activeGameObject=root.gameObject;
        WriteGuide(catalog);
        Debug.Log("Art Formation saved: "+styles.Count+" styles × "+palette.Length+" colors × 4 models.");
    }

    private static List<SourceStyle> CollectStyles(Material[] materials)
    {
        var map=new Dictionary<string,SourceStyle>();
        foreach(var material in materials)
        {
            var signature=new StringBuilder(material.shader.name);
            for(int i=0;i<material.shader.GetPropertyCount();i++)
            {
                string p=material.shader.GetPropertyName(i);
                var type=material.shader.GetPropertyType(i);
                if(type==ShaderPropertyType.Color || Regex.IsMatch(p,"Seed|^_Speed$|^_SwaySpeed$")) continue;
                signature.Append('|').Append(p).Append('=');
                if(type==ShaderPropertyType.Texture)
                    signature.Append(AssetDatabase.GetAssetPath(material.GetTexture(p))).Append(material.GetTextureScale(p)).Append(material.GetTextureOffset(p));
                else if(type==ShaderPropertyType.Vector) signature.Append(material.GetVector(p).ToString("R"));
                else signature.Append(material.GetFloat(p).ToString("R",CultureInfo.InvariantCulture));
            }
            signature.Append('|').Append(material.renderQueue).Append('|').Append(string.Join(",",material.shaderKeywords.OrderBy(k=>k)));
            string key=signature.ToString();
            if(!map.TryGetValue(key,out var style))
            {
                style=new SourceStyle { material=material, category=Category(material.shader.name), label=StyleName(material) };
                map.Add(key,style);
            }
            style.sources.Add(AssetDatabase.GetAssetPath(material));
        }
        return map.Values.OrderBy(s=>s.category,StringComparer.Ordinal).ThenBy(s=>s.label,StringComparer.Ordinal).ToList();
    }

    private static string Category(string shader)
    {
        if(shader.Contains("World/")) return "01  Stone and water";
        if(shader.Contains("PosterPattern")) return "02  Graphic patterns";
        if(shader.Contains("Landmarks/")) return "03  Bone and flora";
        if(shader.Contains("Realm/")) return "04  Living surfaces";
        if(shader.Contains("Crystal")) return "05  Crystal optics";
        if(shader.Contains("HabitatFX") || shader.Contains("HeatLens") || shader.Contains("HeatHaze")) return "07  Light and translucent effects";
        if(shader.Contains("Unlit")) return "00  Flat color baseline";
        return "06  Grain and distortion";
    }

    private static string StyleName(Material m)
    {
        var names=new Dictionary<string,string> {
            {"MAT_Landmark_Ink","Flat / no pattern"},{"HB_Ground","Terrain / contour lines"},{"HB_Inner","Terrain / cave contours"},
            {"MAT_Decor_InnerShell","Stone / inner shell"},{"MAT_Decor_Stone","Stone / hatch and dots"},{"MAT_ErosiveWater","Water / flow lines"},
            {"HB_Poster_BoneDots","Poster / dots"},{"HB_Poster_HeatWaves","Poster / waves"},{"HB_Poster_HeatSpots","Poster / spots"},
            {"HB_Poster_HeatRing","Poster / stripes"},{"HB_Poster_GrowthStripes","Poster / stripes"},{"HB_Poster_LightStars","Poster / stars"},
            {"HB_Poster_GrowthTerrazzo","Poster / terrazzo"},{"HB_BoneComb","Bone / translucent comb"},{"HB_Flora","Flora / vertex shading"},
            {"MAT_Landmark_BoneMarble","Bone / marbled bands"},{"HB_GreenKelp","Kelp / veins and beads"},{"HB_GrowthMushroom","Soft / mushroom gills"},
            {"MAT_Realm_Anemone","Root / grain"},{"MAT_Realm_Slime","Slime / spots and stripes"},{"MAT_Realm_Vein","Slime / luminous veins"},
            {"HB_MoltenVein","Slime / luminous veins"},{"HB_Bubble","Membrane / bubble"},{"HB_TimeRibbon","Membrane / ribbon"},
            {"MAT_Realm_Thread","Thread / moving beads"},{"FireFlower","Static / stretched waves"},{"FireBubble","Static / grain flashes"},
            {"HeatMap","Heat / tonal ramp"},{"GlitchedPool","Glitch / signal bands"},{"HB_HeatLens","Lens / refraction"},
            {"HB_HeatHaze_CraterHaze","Haze / broad dot shells"},{"HB_HeatHaze_FlowerHaze","Haze / fine dot shells"},
            {"HB_FX_GrowthPool","FX / growth ripples"},{"HB_FX_Leaf","FX / leaf membrane"}
        };
        if(names.TryGetValue(m.name,out string name)) return name;
        if(m.name.StartsWith("HB_Crystal_")) return "Crystal / "+m.name.Substring(11);
        if(m.name.StartsWith("HB_FX_Veil")) return "FX / flowing veil";
        if(m.name.StartsWith("HB_FX_Caustics")) return "FX / spectral caustics";
        if(m.name.StartsWith("HB_FX_Glow")) return "FX / soft glow";
        if(m.name.StartsWith("HB_FX_Halo")) return m.name.Contains("Apatite") ? "FX / broad halo" : "FX / fine halo";
        return m.name.Replace("MAT_","").Replace("HB_","").Replace('_',' ');
    }

    private static ArtFormationCatalog.Swatch[] Palette()
    {
        var list=new List<ArtFormationCatalog.Swatch>();
        Action<string,string,string> add=(family,name,hex)=> { ColorUtility.TryParseHtmlString("#"+hex,out Color c); list.Add(new ArtFormationCatalog.Swatch { family=family,name=name,color=c }); };
        add("Neutral","Gray","92969E");
        add("Red","Red","C71F38"); add("Orange","Orange","F58632"); add("Yellow","Yellow","F4D348");
        add("Green","Green","42D96E"); add("Teal","Teal","2CB49F"); add("Cyan","Cyan","36D5E7");
        add("Blue","Blue","1F5EFF"); add("Purple","Purple","A350DA"); add("Pink","Pink","E780AC");
        add("Brown","Brown","A2774A"); add("Neutral","White","F3F4F7"); add("Neutral","Black","10121A");
        add("Custom","Custom","C08BFF");
        return list.ToArray();
    }

    private static Mesh BakeSpecimen(Transform source,string name,int index)
    {
        var filters=source.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh && f.GetComponent<Renderer>()).ToArray();
        var combine=new List<CombineInstance>();
        foreach(var filter in filters)
            for(int s=0;s<filter.sharedMesh.subMeshCount;s++) combine.Add(new CombineInstance { mesh=filter.sharedMesh,subMeshIndex=s,transform=filter.transform.localToWorldMatrix });
        var mesh=new Mesh { name="AF "+name,indexFormat=IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray(),true,true);
        var vertices=mesh.vertices;
        var bounds=mesh.bounds;
        float scale=2.25f/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        Quaternion rotation=index==2 ? Quaternion.Euler(-8,-20,0) : Quaternion.identity;
        for(int i=0;i<vertices.Length;i++) vertices[i]=rotation*((vertices[i]-bounds.center)*scale);
        mesh.vertices=vertices;
        if(rotation!=Quaternion.identity) mesh.normals=mesh.normals.Select(n=>rotation*n).ToArray();
        mesh.RecalculateBounds(); bounds=mesh.bounds;
        for(int i=0;i<vertices.Length;i++) vertices[i].y-=bounds.min.y;
        mesh.vertices=vertices;
        mesh.RecalculateBounds();
        var uv=new Vector2[vertices.Length]; var uv1=new Vector2[vertices.Length]; var colors=new Color[vertices.Length];
        for(int i=0;i<vertices.Length;i++)
        {
            Vector3 p=vertices[i];
            uv[i]=new Vector2(Mathf.Atan2(p.z,p.x)/(Mathf.PI*2)+.5f,p.y/Mathf.Max(.1f,mesh.bounds.size.y));
            uv1[i]=new Vector2(uv[i].x,uv[i].y*2f);
            colors[i]=Color.white;
        }
        mesh.uv=uv; mesh.uv2=uv1; mesh.colors=colors;
        mesh.RecalculateTangents();
        string path=AssetRoot+"/Meshes/"+name.Replace(' ','_')+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        return SaveMesh(mesh,path);
    }

    private static void WritePaletteInclude()
    {
        File.WriteAllText(AssetRoot+"/Shaders/ArtFormationPalette.hlsl",
            "// Generated by ArtFormationSetup. Only the comparison board uses this palette mapping.\n"
            +"#ifndef ART_FORMATION_PALETTE_INCLUDED\n#define ART_FORMATION_PALETTE_INCLUDED\n"
            +"half4 ArtFormationColor(half4 source, half4 palette)\n{\n"
            +"    // Remove every source hue, including vertex colors and code-generated rainbows.\n"
            +"    // Keep the pattern's brightness, HDR range, alpha, and animation.\n"
            +"    half value = max(0.0h, dot(source.rgb, half3(0.2126h, 0.7152h, 0.0722h)));\n"
            +"    return half4(value * palette.rgb, source.a);\n}\n#endif\n");
        AssetDatabase.ImportAsset(AssetRoot+"/Shaders/ArtFormationPalette.hlsl",ImportAssetOptions.ForceSynchronousImport);
    }

    private static Mesh BakeSampleGroup(Mesh[] specimens)
    {
        var mesh=new Mesh { name="Blob + Crystal + Bone Dragon + Leaf",indexFormat=IndexFormat.UInt32 };
        mesh.CombineMeshes(specimens.Select((s,i)=>new CombineInstance { mesh=s,transform=Matrix4x4.Translate(new Vector3(i%2==0?-1.55f:1.55f,.12f,i<2?.95f:-1.65f)) }).ToArray(),true,true);
        return SaveMesh(mesh,AssetRoot+"/Meshes/Four_Samples.asset");
    }

    private static void BuildSupports(Transform parent,int rows,int columns,Material dark,Material light)
    {
        var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new[]{new List<int>(),new List<int>()};
        Action<Vector3,float,float,int> quad=(at,width,depth,slot)=> {
            int n=vertices.Count;
            vertices.Add(at+new Vector3(-width*.5f,0,-depth*.5f)); vertices.Add(at+new Vector3(-width*.5f,0,depth*.5f));
            vertices.Add(at+new Vector3(width*.5f,0,depth*.5f)); vertices.Add(at+new Vector3(width*.5f,0,-depth*.5f));
            uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});
            triangles[slot].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        };
        for(int r=0;r<rows;r++) for(int c=0;c<columns;c++)
        {
            quad(new Vector3(c*Column,-.03f,r*Row),7.1f,7.8f,0);
            quad(new Vector3(c*Column-1.75f,-.022f,r*Row+1.85f),3.45f,3.65f,1);
            quad(new Vector3(c*Column+1.75f,-.022f,r*Row-1.85f),3.45f,3.65f,1);
        }
        var mesh=new Mesh { name="Divided neutral specimen supports",indexFormat=IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.subMeshCount=2;
        mesh.SetTriangles(triangles[0],0); mesh.SetTriangles(triangles[1],1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        mesh=SaveMesh(mesh,AssetRoot+"/Meshes/Board_Supports.asset");
        var g=new GameObject("Divided Neutral Supports",typeof(MeshFilter),typeof(MeshRenderer)); g.transform.SetParent(parent,false);
        g.GetComponent<MeshFilter>().sharedMesh=mesh; g.GetComponent<MeshRenderer>().sharedMaterials=new[]{dark,light};
        g.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
    }

    private static Mesh SaveMesh(Mesh mesh,string path)
    {
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved)
        {
            saved.Clear(); saved.indexFormat=mesh.indexFormat;
            saved.vertices=mesh.vertices; saved.normals=mesh.normals; saved.tangents=mesh.tangents;
            saved.uv=mesh.uv; saved.uv2=mesh.uv2; saved.colors=mesh.colors;
            saved.subMeshCount=mesh.subMeshCount;
            for(int i=0;i<mesh.subMeshCount;i++) saved.SetIndices(mesh.GetIndices(i),mesh.GetTopology(i),i,false);
            saved.bounds=mesh.bounds; saved.UploadMeshData(false); EditorUtility.SetDirty(saved);
            Object.DestroyImmediate(mesh); return saved;
        }
        AssetDatabase.CreateAsset(mesh,path); return mesh;
    }

    private static Shader MakePreviewShader(Shader source)
    {
        string sourcePath=AssetDatabase.GetAssetPath(source);
        if(!sourcePath.StartsWith("Assets/",StringComparison.Ordinal)) return source;
        string text=File.ReadAllText(sourcePath);
        string name="Capstone/ArtFormation/"+source.name.Substring(source.name.IndexOf('/')+1);
        text=Regex.Replace(text,"Shader\\s+\"[^\"]+\"","Shader \""+name+"\"",RegexOptions.None,TimeSpan.FromSeconds(1));
        text=Regex.Replace(text,@"Properties\s*\{", "$0\n        _AFPalette(\"Board Palette\", Color) = (1,1,1,1)",RegexOptions.None,TimeSpan.FromSeconds(1));
        text=text.Replace("CBUFFER_START(UnityPerMaterial)","CBUFFER_START(UnityPerMaterial)\n                half4 _AFPalette;");
        string directory=Path.GetDirectoryName(sourcePath).Replace('\\','/');
        text=Regex.Replace(text,"#include\\s+\"([^\"]+)\"",match=> {
            string path=match.Groups[1].Value;
            return path.StartsWith("Assets/")||path.StartsWith("Packages/") ? match.Value : "#include \""+directory+"/"+path+"\"";
        });
        if(source.name=="Capstone/Effects/HeatHaze") text=SurfaceHazeAdapter(text);
        bool wrappedForward=false;
        text=Regex.Replace(text,@"HLSLPROGRAM(?<body>.*?)ENDHLSL",match=> {
            string body=match.Groups["body"].Value;
            // Palette mapping belongs only to the forward color pass, never depth, shadow or normal passes.
            if(wrappedForward || !Regex.IsMatch(body,@"#pragma\s+fragment\s+Frag\b")) return match.Value;
            wrappedForward=true;
            var signature=Regex.Match(body,@"half4\s+Frag\s*\(([^)]*)\)\s*:\s*SV_Target");
            if(!signature.Success && source.name=="Capstone/GlitchedPool")
                signature=Regex.Match(File.ReadAllText("Assets/Shaders/GlitchPattern.hlsl"),@"half4\s+Frag\s*\(([^)]*)\)\s*:\s*SV_Target");
            if(!signature.Success) throw new InvalidOperationException("Cannot adapt fragment of "+source.name);
            string args=string.Join(", ",signature.Groups[1].Value.Split(',').Select(p=>Regex.Match(p.Split(':')[0].Trim(),@"(\w+)$").Groups[1].Value));
            body=Regex.Replace(body,@"#pragma\s+fragment\s+Frag\b","#pragma fragment ArtFormationFrag");
            return "HLSLPROGRAM"+body+"\n#include \"Assets/ArtFormation/Shaders/ArtFormationPalette.hlsl\"\n"
                +"half4 ArtFormationFrag("+signature.Groups[1].Value+") : SV_Target\n{\n"
                +"    return ArtFormationColor(Frag("+args+"), _AFPalette);\n}\nENDHLSL";
        },RegexOptions.Singleline);
        string path=AssetRoot+"/Shaders/"+source.name.Replace("Capstone/","").Replace('/','_')+".shader";
        File.WriteAllText(path,"// Generated board-only copy of "+sourcePath+". Rebuild with ArtFormationSetup.\n"+text);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
        if(!shader) throw new InvalidOperationException("Shader import failed: "+path);
        return shader;
    }

    private static string SurfaceHazeAdapter(string text)
    {
        // The original vertex program expands billboard dot shells and cannot take a solid specimen.
        // Reuse its dot mask and colors on the specimen's surface instead of destroying its silhouette.
        text=text.Replace("float glitch : TEXCOORD1;","float glitch : TEXCOORD1;\n                float3 surfaceOS : TEXCOORD2;");
        text=ReplaceFunction(text,"Varyings Vert(Attributes input)",@"Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.corner = 0;
                o.surfaceOS = input.positionOS.xyz;
                float phase = sin(dot(input.positionOS.xyz, float3(2.7,3.1,1.9)) - _Time.y * 6.2832 / max(_Cycle,.1) + _Seed) * .5 + .5;
                half3 c = lerp(_DeepColor.rgb, _HeatColor.rgb, phase);
                c = lerp(c, _CrestColor.rgb, saturate(phase*2-1));
                o.color = half4(c, _Opacity * lerp(.35,1,phase) * _Intensity);
                o.glitch = sin(input.positionOS.y*11+floor(_Time.y*6)+_Seed) > .93 ? _Glitch : 0;
                return o;
            }");
        text=text.Replace("float dotShape = 1 - smoothstep(0.55, 1, length(input.corner));",@"float3 n = abs(normalize(cross(ddx(input.surfaceOS),ddy(input.surfaceOS))));
                float2 st = n.z > n.x && n.z > n.y ? input.surfaceOS.xy : (n.y > n.x ? input.surfaceOS.xz : input.surfaceOS.yz);
                float2 corner = frac(st * max(4.0, _Radius / max(.015,_DotSize) * .12)) * 2 - 1;
                float dotShape = 1 - smoothstep(.55,1,length(corner));");
        return text;
    }

    private static string ReplaceFunction(string source,string signature,string replacement)
    {
        int start=source.IndexOf(signature,StringComparison.Ordinal);
        if(start<0) throw new InvalidOperationException("Missing shader function: "+signature);
        int open=source.IndexOf('{',start),depth=1,end=open+1;
        while(depth>0 && end<source.Length) { if(source[end]=='{')depth++; if(source[end]=='}')depth--; end++; }
        return source.Substring(0,start)+replacement+source.Substring(end);
    }

    private static void NeutralizeColors(Material target,Material source)
    {
        for(int i=0;i<source.shader.GetPropertyCount();i++)
        {
            if(source.shader.GetPropertyType(i)!=ShaderPropertyType.Color) continue;
            string p=source.shader.GetPropertyName(i);
            Color color=source.GetColor(p);
            // Brightness is part of the pattern; RGB hue is supplied solely by the column.
            float value=Mathf.Max(color.r,Mathf.Max(color.g,color.b));
            target.SetColor(p,new Color(value,value,value,color.a));
        }
        // Source distance-haze settings must not hide samples when zooming out over a large board.
        if(target.HasProperty("_HazeStart")) target.SetFloat("_HazeStart",5000f);
        if(target.HasProperty("_HazeEnd")) target.SetFloat("_HazeEnd",10000f);
    }

    private static void SetupLighting(Transform root,int rows,int columns)
    {
        RenderSettings.fog=false; RenderSettings.skybox=null;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.72f,.75f,.8f);
        RenderSettings.ambientEquatorColor=new Color(.38f,.4f,.43f);
        RenderSettings.ambientGroundColor=new Color(.16f,.17f,.18f);
        var light=new GameObject("Neutral Key Light",typeof(Light)); light.transform.SetParent(root,false);
        light.transform.rotation=Quaternion.Euler(48,-35,0);
        light.GetComponent<Light>().type=LightType.Directional; light.GetComponent<Light>().intensity=1.2f;
        light.GetComponent<Light>().color=Color.white; light.GetComponent<Light>().shadows=LightShadows.None;
        RenderSettings.sun=light.GetComponent<Light>();
        var camera=new GameObject("Art Formation Camera",typeof(Camera),typeof(ArtFormationNavigator)); camera.transform.SetParent(root,false); camera.tag="MainCamera";
        var view=camera.GetComponent<Camera>(); view.fieldOfView=45; view.nearClipPlane=.1f; view.farClipPlane=1600;
        view.clearFlags=CameraClearFlags.SolidColor; view.backgroundColor=new Color(.025f,.03f,.042f); view.allowHDR=true; view.useOcclusionCulling=false;
        var data=camera.AddComponent<UniversalAdditionalCameraData>(); data.requiresColorOption=CameraOverrideOption.On;
        data.requiresDepthOption=CameraOverrideOption.On; data.renderPostProcessing=false;
        var navigator=camera.GetComponent<ArtFormationNavigator>();
        navigator.boardSize=new Vector2((columns-1)*Column,(rows-1)*Row);
        navigator.boardCenter=new Vector3(navigator.boardSize.x*.5f,0,navigator.boardSize.y*.5f);
        navigator.focus=new Vector3(8,0,8); navigator.distance=46;
        navigator.rowCount=rows; navigator.colorCount=columns; navigator.columnSpacing=Column; navigator.rowSpacing=Row;
        camera.transform.SetPositionAndRotation(navigator.focus+Quaternion.Euler(65,0,0)*(Vector3.back*navigator.distance),Quaternion.Euler(65,0,0));
    }

    private static Material BoardMaterial(ArtFormationCatalog catalog,string name,Color color)
    {
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name=name };
        material.SetColor("_BaseColor",color); material.enableInstancing=true;
        AssetDatabase.AddObjectToAsset(material,catalog); return material;
    }
    private static Transform Group(Transform parent,string name) { var t=new GameObject(name).transform; t.SetParent(parent,false); return t; }
    private static MeshRenderer Box(Transform parent,string name,Vector3 at,Vector3 size,Material material)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube); g.name=name; g.transform.SetParent(parent,false);
        g.transform.localPosition=at; g.transform.localScale=size; Object.DestroyImmediate(g.GetComponent<Collider>());
        var r=g.GetComponent<MeshRenderer>(); r.sharedMaterial=material; r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
        return r;
    }
    private static TextMesh Label(Transform parent,string text,Vector3 at,float size,Color color,TextAnchor anchor=TextAnchor.MiddleCenter)
    {
        var g=new GameObject(text.Replace('\n',' '),typeof(TextMesh)); g.transform.SetParent(parent,false);
        g.transform.localPosition=at; g.transform.localRotation=Quaternion.Euler(90,0,0);
        var label=g.GetComponent<TextMesh>(); label.text=text; label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize=48; label.characterSize=size; label.anchor=anchor; label.alignment=anchor==TextAnchor.MiddleLeft?TextAlignment.Left:anchor==TextAnchor.MiddleRight?TextAlignment.Right:TextAlignment.Center; label.color=color;
        g.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
        return label;
    }
    private static Bounds CombinedBounds(Transform root)
    {
        var renderers=root.GetComponentsInChildren<Renderer>(true); var bounds=renderers.First().bounds;
        foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
    }
    private static string HierarchyPath(Transform t) => t.parent ? HierarchyPath(t.parent)+"/"+t.name : t.name;
    private static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path)) return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    private static void WriteGuide(ArtFormationCatalog catalog)
    {
        var text=new StringBuilder("# Art Formation\n\nOpen `Assets/Scenes/Art Formation.unity`.\n\n");
        text.AppendLine("Each row is a distinct non-color surface treatment from Scene_ArtReference. Each column supplies one named color. Every cell contains the source blob, crystal cluster, smaller bone dragon, and leaf, normalized to a comparable size.");
        text.AppendLine("\nIn Play mode: mouse wheel zooms; right/middle drag or WASD/arrows pans at a fixed height and viewing angle; Shift moves faster; Home fits the whole board; End returns to the opening detail; C jumps to the custom color column; Page Up/Down moves one style row. Click the Game view to give it keyboard focus. The standard Scene view also works.");
        text.AppendLine("\nSelect the Art Formation root in the Hierarchy. In the Inspector, click Custom Color to open Unity's built-in color picker (hue, saturation, value, RGB or hex). It updates the Custom column across every row in Edit mode and Play mode. It uses per-renderer overrides and never changes source materials. Color edits made in Play mode reset when Play mode stops; tune in Edit mode and save the scene to keep a color.");
        text.AppendLine("\nSource scene, source materials, and source shader code are retained. Generated board-only shader copies remove source RGB hue at the final fragment output, preserving tonal patterns, alpha, animation and HDR intensity. Material color properties are neutralized first. Flat unlit is the color baseline. The shell-dot heat-haze vertex program is adapted to dots on each specimen surface, because its original billboard expansion would destroy the example models. Specimen UVs are standardized; original vertex tint is removed. Glass and lens rows require the camera's opaque color texture, enabled on the scene camera. Transparent/additive styles are shown against divided neutral supports.");
        text.AppendLine("\nStyles differing only in source RGB, random seed, or the generic Speed/SwaySpeed properties share a row. Other numeric settings, textures, shader keywords and render queues remain distinct. The list below preserves every source material mapping. Text-label font material is not a surface treatment.");
        text.AppendLine("\nRebuild from Tools > Capstone > Art Direction > Build Art Formation. This replaces the generated board and catalog.\n\n## Rows\n");
        for(int i=0;i<catalog.styles.Length;i++) { var s=catalog.styles[i]; text.AppendLine((i+1)+". **"+s.name+"** — "+s.sourceShader+"; sources: "+string.Join(", ",s.sourceMaterials)); }
        text.AppendLine("\n## Color columns\n");
        foreach(var s in catalog.palette) text.AppendLine("- "+s.name+" (`#"+ColorUtility.ToHtmlStringRGB(s.color)+"`) — "+s.family);
        text.AppendLine("\n## Specimen sources\n");
        for(int i=0;i<catalog.specimens.Length;i++) text.AppendLine("- "+SpecimenNames[i]+": "+catalog.specimenSources[i]);
        File.WriteAllText(AssetRoot+"/README.md",text.ToString());
        AssetDatabase.ImportAsset(AssetRoot+"/README.md");
    }
}
