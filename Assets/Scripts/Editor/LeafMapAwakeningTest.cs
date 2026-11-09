using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>
/// Plays the bone mother's awakening in the leaf map: wakes her, records how far apart neighbouring
/// vertebrae get and how many end up inside rock, saves frames of the cutscene camera and leaves Play Mode.
/// Results: ArtCaptures/LeafMap/Awakening.txt and Awakening_*.png.
/// </summary>
[InitializeOnLoad]
public static class LeafMapAwakeningTest
{
    private const string Key="LeafMap.AwakeningTest";
    private static float woke=-1f,maxStretch;
    private static int buried,samples,shot;
    private static readonly float[] Shots={3f,7f,11f,15f};

    static LeafMapAwakeningTest(){EditorApplication.update+=Tick;}

    [MenuItem("Tools/Capstone/Leaf Map/Test Bone Mother Awakening In Play Mode")]
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(Key,true);woke=-1f;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying)return;
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid())return;
        var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        var mother=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BoneKiln>()).FirstOrDefault();
        if(world==null || !world.GenerationComplete || mother==null || mother.Root==null)return;
        var stations=(IList)typeof(BoneKiln).GetField("stations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mother);
        if(woke<0f)
        {
            if(Time.timeSinceLevelLoad<4f)return;
            var carrier=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>()).FirstOrDefault();
            if(carrier!=null)
            {
                Vector3 near=mother.Root.position+mother.ExitDirection*-22f;near.y=LeafMapTerrain.Height(near.x,near.z)+2f;
                carrier.PlayerBody.position=near;carrier.transform.position=near;Physics.SyncTransforms();
            }
            mother.Wake();woke=Time.time;maxStretch=0f;buried=samples=shot=0;
            return;
        }
        float since=Time.time-woke;
        // Stretch: the longest neighbouring gap relative to its resting spacing.
        var stationType=stations.Count>0?stations[0].GetType():null;
        FieldInfo sField=stationType?.GetField("s"),tField=stationType?.GetField("transform");
        for(int i=1;i<stations.Count;i++)
        {
            float rest=Mathf.Abs((float)sField.GetValue(stations[i])-(float)sField.GetValue(stations[i-1]));
            float gap=Vector3.Distance(((Transform)tField.GetValue(stations[i])).position,((Transform)tField.GetValue(stations[i-1])).position);
            if(rest>0.01f)maxStretch=Mathf.Max(maxStretch,gap/rest);
            var at=((Transform)tField.GetValue(stations[i])).position;samples++;
            foreach(var hit in Physics.OverlapSphere(at,0.3f,1<<7,QueryTriggerInteraction.Ignore))if(world.OwnsCollider(hit)){buried++;break;}
        }
        if(shot<Shots.Length && since>=Shots[shot])
        {
            var camera=Camera.allCameras.Where(c=>c.isActiveAndEnabled).OrderByDescending(c=>c.depth).FirstOrDefault();
            if(camera!=null)Save(camera,"ArtCaptures/LeafMap/Awakening_"+(shot+1)+".png");
            shot++;
        }
        if(since<Shots[Shots.Length-1]+1f && mother.Emerging)return;
        if(since<Shots[Shots.Length-1]+1f)return;
        File.WriteAllText("ArtCaptures/LeafMap/Awakening.txt",
            "Longest vertebra gap / resting gap during the awakening: "+maxStretch.ToString("0.00")+
            "\nVertebra samples inside the terrain: "+buried+" of "+samples+" ("+(100f*buried/Mathf.Max(1,samples)).ToString("0.0")+"%)"+
            "\nStill emerging after "+(Shots[Shots.Length-1]+1)+" s: "+mother.Emerging);
        SessionState.SetBool(Key,false);woke=-1f;EditorApplication.ExitPlaymode();
    }

    private static void Save(Camera camera,string path)
    {
        var target=new RenderTexture(1280,960,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        var texture=new Texture2D(1280,960,TextureFormat.RGB24,false);var old=RenderTexture.active;var before=camera.targetTexture;
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1280,960),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
        }
        finally {camera.targetTexture=before;RenderTexture.active=old;target.Release();Object.Destroy(target);Object.Destroy(texture);}
    }
}
