using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static partial class BasiltSceneSetup
{
    public const string JungleRoot="Basilt - Megavine jungle";
    [MenuItem("Tools/Capstone/Basilt/Build Megavine Jungle")]
    public static void UpgradeToJungle()
    {
        if(Application.isPlaying || SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open Basilt in Edit mode first.");
        var scene=SceneManager.GetActiveScene();
        var worlds=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).ToArray();
        if(worlds.Any(w=>w.SculptData!=null && w.SculptData.Edits.Count>0))throw new InvalidOperationException("Preserve sculpt edits before rebuilding this generated layout.");
        // Retain the fossil and player, replace only the environment authored by this generator.
        foreach(string name in new[]{"Vine side thorns - clear walking crowns","Human scale basalt fragments","Mesh basalt field - compare with terrain columns to the west",JungleRoot})
        {
            var old=GameObject.Find(name);if(old==null)continue;
            var meshes=old.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Where(m=>AssetDatabase.GetAssetPath(m)==geometryPath).Distinct().ToArray();
            Object.DestroyImmediate(old);foreach(var mesh in meshes)Object.DestroyImmediate(mesh,true);
        }
        basalt=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltColumns.mat");
        basalt.shader=Shader.Find("Capstone/World/BlackFlowContours");
        basalt.SetColor("_BackgroundColor",new Color(0.105f,0.115f,0.108f));
        basalt.SetColor("_LineColor",new Color(0.40f,0.43f,0.40f));basalt.SetColor("_DotColor",new Color(0.78f,0.80f,0.75f));
        basalt.SetFloat("_PatternScale",0.11f);basalt.SetFloat("_LineDensity",8);basalt.SetFloat("_LineWidth",0.007f);
        basalt.SetFloat("_DotDensity",0.17f);basalt.SetFloat("_FlowSpeed",0.004f);basalt.SetFloat("_FacetStrength",0.7f);EditorUtility.SetDirty(basalt);
        vine=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltVines.mat");
        vine.SetColor("_BaseColor",new Color(0.12f,0.24f,0.14f));vine.SetColor("_FiberColor",new Color(0.36f,0.49f,0.28f));
        vine.SetColor("_ShadowColor",new Color(0.025f,0.047f,0.029f));vine.SetFloat("_GrainScale",0.6f);EditorUtility.SetDirty(vine);
        var ground=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltGround.mat");
        ground.SetColor("_LineColor",new Color(0.18f,0.23f,0.19f));ground.SetColor("_LineColor2",new Color(0.32f,0.36f,0.30f));
        ground.SetFloat("_HatchStrength",0.55f);EditorUtility.SetDirty(ground);
        foreach(var w in worlds)
        {
            bool roots=w.name.Contains("vine");var s=new SerializedObject(w);
            s.FindProperty("worldSize").vector2Value=new Vector2(480,576);
            s.FindProperty("cellSize").floatValue=roots?1.75f:2;
            s.FindProperty("chunkCells").intValue=24;
            s.FindProperty("surfaceMaterial").objectReferenceValue=roots?vine:ground;
            s.FindProperty("innerMaterial").objectReferenceValue=roots?vine:ground;
            s.FindProperty("placePlayerOnSurface").boolValue=false;
            var recipe=s.FindProperty("generationRecipe");recipe.FindPropertyRelative("version").intValue=3;
            recipe.FindPropertyRelative("verticalBounds").vector2Value=roots?new Vector2(-24,100):new Vector2(-30,86);
            recipe.FindPropertyRelative("safeSpawn").vector3Value=new Vector3(-24,GroundHeightAtSpawn()+1.25f,-267);
            s.ApplyModifiedPropertiesWithoutUndo();
            w.SculptData.Freeze(w.CaptureLayout(out var checks),checks,"Basilt v3 / enclosing megavine jungle");EditorUtility.SetDirty(w.SculptData);
            string stem=roots?"BasiltRoots":"BasiltLand";
            File.WriteAllBytes(Folder+"/"+stem+"Chunks.bytes",w.BuildChunkCache());
            AssetDatabase.ImportAsset(Folder+"/"+stem+"Chunks.bytes",ImportAssetOptions.ForceUpdate);
            w.ChunkCache=AssetDatabase.LoadAssetAtPath<TextAsset>(Folder+"/"+stem+"Chunks.bytes");w.Generate(true);
        }
        meshIndex=40000;
        var environment=new GameObject(JungleRoot).transform;
        var columns=new GameObject("Individual basalt - enclosing walls and shared terraces").transform;columns.SetParent(environment);
        foreach(var c in new BasiltDensity(1147,false).Columns)
        {
            float bottom=BasiltDensity.GroundHeight(c.centre.x,c.centre.y);
            for(int j=0;j<6;j++)
            {float a=j*Mathf.PI/3;bottom=Mathf.Min(bottom,BasiltDensity.GroundHeight(c.centre.x+Mathf.Cos(a)*c.radius,c.centre.y+Mathf.Sin(a)*c.radius));}
            bottom-=2;Hex(columns,new Vector3(c.centre.x,bottom,c.centre.y),c.radius,Mathf.Max(2,c.top-bottom));
        }
        var canopy=new GameObject("Independent giant arches and canopy sweeps").transform;canopy.SetParent(environment);
        var thorns=new GameObject("Radial hooked thorns - all around the vines").transform;thorns.SetParent(environment);
        for(int i=0;i<BasiltDensity.Canopy.Length;i++)
        {
            var v=BasiltDensity.Canopy[i];var path=BasiltDensity.SamplePath(v.keys,18);var radii=VaryingRadii(path,v.radius,i);
            Tube((i>=32?"Ancient canopy sweep ":"Independent megavine arch ")+(i+1),canopy,path,radii,vine,true,18);
            ThornBelt(thorns,path,radii,i);
        }
        for(int i=0;i<BasiltDensity.Roads.Length;i++)
        {var p=BasiltDensity.SampleRoad(i);ThornBelt(thorns,p,Enumerable.Repeat(BasiltDensity.RoadRadii[i],p.Length).ToArray(),i+100);}
        DressJungleFloor(environment);
        var player=Object.FindFirstObjectByType<SmoothFirstPersonController>();
        Vector3 spawn=new Vector3(-24,GroundHeightAtSpawn()+1.25f,-267);
        player.transform.root.position+=spawn-player.transform.position;player.SetLook(0,-6);
        foreach(var c in player.transform.root.GetComponentsInChildren<Camera>(true))c.farClipPlane=1000;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogDensity=0.0018f;RenderSettings.fogColor=new Color(0.105f,0.15f,0.135f);
        RenderSettings.ambientSkyColor=new Color(0.42f,0.50f,0.44f);RenderSettings.ambientEquatorColor=new Color(0.25f,0.32f,0.27f);
        if(RenderSettings.sun!=null){RenderSettings.sun.intensity=1.9f;RenderSettings.sun.transform.rotation=Quaternion.Euler(28,-37,0);}
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        var view=SceneView.lastActiveSceneView;if(view!=null){view.sceneLighting=true;view.drawGizmos=false;view.LookAt(new Vector3(-30,31,-35),Quaternion.Euler(18,-15,0),180,false,true);view.Repaint();}
        Debug.Log("Basilt jungle rebuilt: 42 major vines, 480x576m rolling terrain, individual basalt colonnades, radial thorns.");
    }
    private static float GroundHeightAtSpawn()=>BasiltDensity.GroundHeight(-24,-267);
    private static float[] VaryingRadii(Vector3[] path,float r,int seed)
    {
        var result=new float[path.Length];
        for(int i=0;i<result.Length;i++)
        {float t=i/(float)(result.Length-1);float taper=0.76f+0.24f*Mathf.Sin(Mathf.PI*t);result[i]=r*taper*(1+0.065f*Mathf.Sin(t*17+seed));}
        return result;
    }
    private static void ThornBelt(Transform parent,Vector3[] path,float[] radii,int seed)
    {
        var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();
        var random=new System.Random(seed*139+23);
        for(int i=3;i<path.Length-3;i+=5)
        {
            Vector3 forward=(path[i+1]-path[i-1]).normalized;
            Vector3 right=Vector3.Cross(Mathf.Abs(forward.y)>0.92f?Vector3.forward:Vector3.up,forward).normalized;
            Vector3 up=Vector3.Cross(forward,right);
            for(int j=0;j<5;j++)
            {
                float angle=j*Mathf.PI*2/5+i*0.72f+seed;
                Vector3 radial=(right*Mathf.Cos(angle)+up*Mathf.Sin(angle)).normalized;
                Vector3 at=path[i]+radial*(radii[i]-0.15f);
                if(at.y<BasiltDensity.GroundHeight(at.x,at.z)-0.3f)continue;
                float length=2.4f+radii[i]*0.38f+(float)random.NextDouble()*2;
                float width=0.8f+radii[i]*0.09f;
                Vector3 mid=at+radial*length*0.65f-forward*length*0.08f;
                Vector3 tip=at+radial*length-forward*length*0.55f;
                AppendThorn(vertices,indices,uv,at,mid,tip,width);
            }
        }
        if(vertices.Count>0)MeshObject("Radial thorn belt "+seed,parent,vertices,indices,uv,vine,true);
    }
    private static void AppendThorn(List<Vector3> v,List<int> t,List<Vector2> uv,Vector3 a,Vector3 b,Vector3 c,float r)
    {
        int first=v.Count;const int sides=6;
        Vector3 direction=(c-a).normalized,right=Vector3.Cross(Mathf.Abs(direction.y)>0.9f?Vector3.forward:Vector3.up,direction).normalized,up=Vector3.Cross(direction,right);
        for(int ring=0;ring<3;ring++)for(int j=0;j<sides;j++)
        {float angle=j*Mathf.PI*2/sides;Vector3 p=ring==0?a:ring==1?b:c;float radius=ring==0?r:ring==1?r*0.56f:0.015f;v.Add(p+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius);uv.Add(new Vector2(ring*0.5f,j/(float)sides));}
        for(int ring=0;ring<2;ring++)for(int j=0;j<sides;j++)
        {int x=first+ring*sides+j,y=first+ring*sides+(j+1)%sides;t.Add(x);t.Add(y);t.Add(x+sides);t.Add(y);t.Add(y+sides);t.Add(x+sides);}
    }
    private static void DressJungleFloor(Transform parent)
    {
        var fragments=new GameObject("Broken basalt and rootlets along the gullies").transform;fragments.SetParent(parent);
        var random=new System.Random(582);float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
        for(int i=0;i<160;i++)
        {
            float x=R(-208,208),z=R(-248,250);if(x>35 && x<130 && z>40 && z<165)continue;
            float y=BasiltDensity.GroundHeight(x,z),r=R(0.65f,2.7f);
            Hex(fragments,new Vector3(x,y-0.3f,z),r,R(0.6f,3.6f));
        }
        for(int i=0;i<28;i++)
        {
            float x=R(-175,150),z=R(-205,185),h=BasiltDensity.GroundHeight(x,z);
            if(x>35 && z>40)continue;
            var path=BasiltDensity.SamplePath(new[]{new Vector3(x-15,h-2,z-15),new Vector3(x-5,h+3,z-2),new Vector3(x+6,h+2,z+8),new Vector3(x+18,BasiltDensity.GroundHeight(x+18,z+15)-2,z+15)},8);
            Tube("Ground rootlet",fragments,path,null,vine,true,10,1.8f,0.3f);
        }
    }
}
