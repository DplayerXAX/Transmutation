using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated mesh, pose and render checks; never edits a gameplay scene.</summary>
public static class WebbedHandValidation
{
    [MenuItem("Tools/Player/Render Webbed Hand Preview")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null;
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQualityPipeline = QualitySettings.renderPipeline;
        UniversalRenderPipelineAsset pipeline = null;
        UniversalRendererData rendererData = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            var pipelineSettings = new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            var rig = new GameObject("Arm preview rig");
            var go = new GameObject("Reference hand");go.transform.SetParent(rig.transform,false);
            var hand = go.AddComponent<WebbedPlayerHand>(); hand.Initialize(1);
            if (go.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Finger ") && t.name.EndsWith("Joint 0")) != 3)
                throw new Exception("Expected three linked fingers.");
            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            const int profileVertices=49*17;
            var skeletonVertices=renderers.Single(r=>r.name=="Blue skeleton").sharedMesh.vertices;
            float headWidth=skeletonVertices.Skip(6*17).Take(17).Max(v=>Mathf.Abs(v.x));
            float shaftWidth=skeletonVertices.Skip(24*17).Take(17).Max(v=>Mathf.Abs(v.x));
            if(headWidth<shaftWidth*1.4f)throw new Exception("Bone heads are not wider than the shaft.");
            float wristEnd=skeletonVertices.Take(profileVertices).Max(v=>v.z);
            float palmStart=skeletonVertices.Skip(profileVertices).Take(profileVertices).Min(v=>v.z);
            if(palmStart-wristEnd<0.004f)throw new Exception("Wrist and palm bones have no visible gap.");
            float fingerEnd=skeletonVertices.Skip(2*profileVertices).Take(profileVertices).Max(v=>v.z);
            float nextStart=skeletonVertices.Skip(3*profileVertices).Take(profileVertices).Min(v=>v.z);
            if(nextStart-fingerEnd<0.005f)throw new Exception("Adjacent finger bones touch.");
            var vesselVertices=renderers.Single(r=>r.name=="Pink vessels").sharedMesh.vertices;
            if(vesselVertices.Take(17).Any(v=>Vector3.Distance(v,vesselVertices[0])>0.000001f) ||
                vesselVertices.Skip(profileVertices-17).Take(17).Any(v=>Vector3.Distance(v,vesselVertices[profileVertices-1])>0.000001f))
                throw new Exception("Vessel ends are not tapered to points.");
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterial.shader == null || ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))
                    throw new Exception("Hand shader did not compile.");
                if (renderer.sharedMesh.vertices.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)))
                    throw new Exception("Invalid generated mesh.");
                if (renderer.sharedMesh.boneWeights.Any(w => Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) > 0.0001f))
                    throw new Exception("Invalid skin weights.");
            }
            var skin = renderers.Single(r => r.name == "Webbed skin");
            var open = new Mesh(); skin.BakeMesh(open);
            hand.Pose(1, 1, 1);
            var closed = new Mesh(); skin.BakeMesh(closed);
            if (!open.vertices.Zip(closed.vertices, (a,b) => Vector3.Distance(a,b)).Any(d => d > 0.025f))
                throw new Exception("Grab pose did not deform the hand.");
            for (int j = 0; j < 3; j++)
            {
                Quaternion first = go.GetComponentsInChildren<Transform>().Single(t => t.name == "Finger 0 Joint " + j).localRotation;
                for (int f = 1; f < 3; f++)
                    if (Quaternion.Angle(first, go.GetComponentsInChildren<Transform>().Single(t => t.name == "Finger " + f + " Joint " + j).localRotation) > 0.01f)
                        throw new Exception("Webbed fingers do not curl together.");
            }
            Object.DestroyImmediate(open); Object.DestroyImmediate(closed);
            hand.Pose(0, 0, 1);
            // A forearm using the same scaled segment convention as FirstPersonBody.
            var arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            arm.transform.position = new Vector3(0,-0.005f,-0.145f);
            arm.transform.rotation = Quaternion.Euler(90,0,0);
            arm.transform.localScale = new Vector3(0.062f,0.14f,0.062f);
            hand.DressArm(arm.transform);
            hand.CreateArmSurface(rig.transform);
            hand.PoseArmSurface(new Vector3(0,0,-0.30f),new Vector3(0,0,-0.15f),Vector3.zero,0.062f);
            var armMesh = rig.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Continuous arm skin").sharedMesh;
            if(armMesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))
                throw new Exception("Invalid continuous arm surface.");
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0,0.65f,0.05f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0,0, -0.015f)-camera.transform.position,Vector3.forward);
            camera.orthographic = true; camera.orthographicSize = 0.31f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f,0.035f,0.055f);
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 5;
            var light = new GameObject("Preview light").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45,-25,0); light.intensity = 1.3f;
            target = new RenderTexture(1000,1000,24); camera.targetTexture = target;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,"../HandPreviews"));
            Directory.CreateDirectory(output);
            Render(camera,target,Path.Combine(output,"open.png"));
            hand.Pose(1,1,1); Render(camera,target,Path.Combine(output,"grip.png"));
            hand.Pose(0.45f,0.45f,1); Render(camera,target,Path.Combine(output,"relaxed.png"));
            var anatomy = rig.GetComponentsInChildren<Renderer>().Concat(arm.GetComponentsInChildren<Renderer>())
                .Select(r=>r.sharedMaterial).Where(m=>m!=null).Distinct().ToArray();
            foreach(var material in anatomy)
                if(material.HasProperty("_UseAnimationTime")){material.SetFloat("_UseAnimationTime",1);material.SetFloat("_AnimationTime",0);}
            hand.UpdateEmission(Color.black);
            light.enabled=false;
            string offPath=Path.Combine(output,"glow-off.png"),onPath=Path.Combine(output,"glow-on.png");
            Render(camera,target,offPath);
            hand.UpdateEmission(new Color(1.8f,3f,0.75f));
            if(hand.Skin.GetColor("_EmissionColor").maxColorComponent<1 || hand.Bone.GetColor("_EmissionColor").maxColorComponent<1)
                throw new Exception("Player fluorescence did not reach both skin and bones.");
            Render(camera,target,onPath);
            if(MeanBrightness(onPath)<MeanBrightness(offPath)+0.005f)
                throw new Exception("Fluorescence did not visibly brighten the arm in darkness.");
            hand.UpdateEmission(Color.black);
            if(hand.Bone.GetColor("_EmissionColor").maxColorComponent>0.001f || hand.Skin.GetColor("_EmissionColor").maxColorComponent>0.001f)
                throw new Exception("Fluorescence did not clear when luminosity ended.");
            light.enabled=true;
            for(int frame=0;frame<16;frame++)
            {
                foreach(var material in anatomy)if(material.HasProperty("_AnimationTime"))material.SetFloat("_AnimationTime",frame*0.5f);
                Render(camera,target,Path.Combine(output,"motion-"+frame.ToString("D2")+".png"));
            }
            if(ChangedPixels(Path.Combine(output,"motion-00.png"),Path.Combine(output,"motion-12.png"))<80)
                throw new Exception("Vessel flow and floating flecks did not visibly animate.");
            foreach(var material in anatomy)if(material.HasProperty("_UseAnimationTime"))material.SetFloat("_UseAnimationTime",0);
            arm.SetActive(false);
            hand.PoseArmSurface(new Vector3(-0.13f,0,-0.25f),new Vector3(0.10f,0,-0.14f),Vector3.zero,0.062f);
            Render(camera,target,Path.Combine(output,"bent-elbow.png"));
            rig.SetActive(false);arm.SetActive(false);
            ValidateLegs(camera,target,output);
            camera.targetTexture = null;
            Debug.Log("PASS: anatomy, skin weights, linked curl, grab deformation, skin/bone fluorescence in darkness, glow reset, animated interior, shaders and previews.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQualityPipeline;
            if(target != null) { RenderTexture.active=null; target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.CloseScene(scene,true);
            if(pipeline != null) Object.DestroyImmediate(pipeline);
            if(rendererData != null) Object.DestroyImmediate(rendererData);
        }
    }
    static void ValidateLegs(Camera camera,RenderTexture target,string output)
    {
        var roots=new GameObject[2];var feet=new WebbedPlayerHand[2];
        var uppers=new Transform[2];var lowers=new Transform[2];
        for(int i=0;i<2;i++)
        {
            float side=i==0?-1:1;
            roots[i]=new GameObject(i==0?"Left leg fixture":"Right leg fixture");
            var foot=new GameObject("Foot");foot.transform.SetParent(roots[i].transform,false);
            feet[i]=foot.AddComponent<WebbedPlayerHand>();feet[i].InitializeFoot(side);
            for(int segment=0;segment<2;segment++)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name=segment==0?"Thigh":"Shin";
                go.transform.SetParent(roots[i].transform,false);
                if(segment==0)uppers[i]=go.transform;else lowers[i]=go.transform;
                feet[i].DressArm(go.transform);
            }
            feet[i].CreateArmSurface(roots[i].transform);
            feet[i].SetFootVisible(false);
            if(foot.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled))throw new Exception("Skate shoe foot hiding failed.");
            feet[i].SetFootVisible(true);
            if(foot.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>!r.enabled))throw new Exception("Bare feet did not restore.");
        }
        camera.transform.position=new Vector3(1.15f,0.85f,1.75f);
        camera.transform.LookAt(new Vector3(0,0.52f,0.04f));camera.orthographicSize=0.65f;
        for(int frame=0;frame<16;frame++)
        {
            for(int i=0;i<2;i++)
            {
                float side=i==0?-1:1,phase=frame*Mathf.PI*2/16+(i==0?0:Mathf.PI);
                Vector3 hip=new Vector3(side*0.14f,1.02f,0);
                Vector3 ankle=new Vector3(side*0.14f,0.04f+Mathf.Max(0,Mathf.Cos(phase))*0.13f,Mathf.Sin(phase)*0.20f);
                Vector3 knee=Vector3.Lerp(hip,ankle,0.51f)+Vector3.forward*(0.09f+0.06f*Mathf.Max(0,Mathf.Cos(phase)));
                PosePreviewSegment(uppers[i],hip,knee,0.11f);PosePreviewSegment(lowers[i],knee,ankle,0.0935f);
                feet[i].transform.position=ankle+Vector3.forward*0.06f;
                feet[i].PoseArmSurface(hip,knee,ankle,0.11f);
                foreach(var r in roots[i].GetComponentsInChildren<Renderer>())
                {
                    if(r.sharedMaterial==null||!r.sharedMaterial.HasProperty("_AnimationTime"))continue;
                    var properties=new MaterialPropertyBlock();r.GetPropertyBlock(properties);
                    properties.SetFloat("_UseAnimationTime",1);properties.SetFloat("_AnimationTime",frame*0.2f);
                    r.SetPropertyBlock(properties);
                }
                var mesh=roots[i].GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Continuous leg skin").sharedMesh;
                if(mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new Exception("Walking knee produced invalid skin.");
            }
            Render(camera,target,Path.Combine(output,"legs-"+frame.ToString("D2")+".png"));
        }
        foreach(var foot in feet)foot.UpdateEmission(new Color(1.8f,3f,0.75f));
        Render(camera,target,Path.Combine(output,"legs-glow.png"));
        Debug.Log("PASS: leg and foot anatomy, knee deformation, finite walk poses, skate shoe visibility and fluorescence.");
    }
    static void PosePreviewSegment(Transform segment,Vector3 a,Vector3 b,float width)
    {
        segment.SetPositionAndRotation((a+b)*0.5f,Quaternion.FromToRotation(Vector3.up,(b-a).normalized));
        segment.localScale=new Vector3(width,Vector3.Distance(a,b)*0.5f,width);
    }
    static float MeanBrightness(string path)
    {
        var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(path));
        double sum=0;var pixels=image.GetPixels32();
        foreach(var p in pixels)sum+=(p.r+p.g+p.b)/(3.0*255);
        Object.DestroyImmediate(image);return (float)(sum/pixels.Length);
    }
    static int ChangedPixels(string a,string b)
    {
        var first=new Texture2D(2,2);var second=new Texture2D(2,2);
        first.LoadImage(File.ReadAllBytes(a));second.LoadImage(File.ReadAllBytes(b));
        var x=first.GetPixels32();var y=second.GetPixels32();int count=0;
        for(int i=0;i<x.Length;i++)if(Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b)>12)count++;
        Object.DestroyImmediate(first);Object.DestroyImmediate(second);return count;
    }
    static void Render(Camera camera, RenderTexture target, string path)
    {
        // Explicit snapshots allow several different poses to render during one editor frame.
        var snapshots = new List<GameObject>();
        var sources = camera.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>())
            .Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
        foreach(var source in sources)
        {
            var mesh = new Mesh(); source.BakeMesh(mesh);
            var snapshot = new GameObject("Baked preview",typeof(MeshFilter),typeof(MeshRenderer));
            snapshot.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            snapshot.GetComponent<MeshFilter>().sharedMesh=mesh;
            snapshot.GetComponent<MeshRenderer>().sharedMaterial=source.sharedMaterial;
            var properties=new MaterialPropertyBlock();source.GetPropertyBlock(properties);
            snapshot.GetComponent<MeshRenderer>().SetPropertyBlock(properties);
            source.enabled=false; snapshots.Add(snapshot);
        }
        camera.Render(); camera.Render(); RenderTexture.active = target;
        var texture = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply();
        File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture); RenderTexture.active=null;
        foreach(var snapshot in snapshots){Object.DestroyImmediate(snapshot.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(snapshot);}
        foreach(var source in sources)source.enabled=true;
    }
    public static void RunBatch()
    {
        try
        {
            if (!Application.dataPath.Replace('\\','/').Contains("/Temp/WebbedHandChecks/UnityProject/Assets"))
                throw new InvalidOperationException("RunBatch requires the disposable WebbedHandChecks project. Use the preview menu in the main project.");
            // A fresh disposable project starts with an unsaved scene; additive scene creation requires a saved one.
            var initial = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(initial, "Assets/WebbedHandValidationBase.unity");
            Run(); EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
