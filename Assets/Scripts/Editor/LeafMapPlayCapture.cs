using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>
/// Plays the leaf map with the player at the foot of the bubble cascade, waits for the flowers to fill the
/// air, renders the cascade review views to ArtCaptures/LeafMap/Play_*.png and leaves Play Mode.
/// </summary>
[InitializeOnLoad]
public static class LeafMapPlayCapture
{
    private const string Key="LeafMap.PlayCapture";
    private const float Wait=40f;
    private static bool placed;

    static LeafMapPlayCapture(){EditorApplication.update+=Tick;}

    [MenuItem("Tools/Capstone/Leaf Map/Capture Bubble Cascade In Play Mode")]
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(Key,true);placed=false;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying)return;
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid())return;
        var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        if(world==null || !world.GenerationComplete)return;
        var carrier=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>()).FirstOrDefault();
        if(carrier==null)return;
        if(!placed)
        {
            // The player stands at the foot of the cascade, so the emitters near them are allowed to fire.
            Vector2 foot=LeafMapTerrain.ToWorld(new Vector2(-20,-66));
            carrier.PlayerBody.position=new Vector3(foot.x,LeafMapTerrain.Height(foot.x,foot.y)+2f,foot.y);
            carrier.transform.position=carrier.PlayerBody.position;Physics.SyncTransforms();
            placed=true;
            return;
        }
        if(Time.timeSinceLevelLoad<Wait)return;
        SessionState.SetBool(Key,false);
        var target=new RenderTexture(1600,1200,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        var texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);var old=RenderTexture.active;
        int bubbles=Object.FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None).Length;
        try
        {
            foreach(string name in new[]{"19_BubbleHops","13_TimeField"})
            {
                var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c.name==name);
                if(camera==null)continue;
                var before=camera.targetTexture;
                try
                {
                    camera.targetTexture=target;camera.aspect=4f/3f;camera.Render();camera.Render();RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,1600,1200),0,0);texture.Apply();
                    File.WriteAllBytes("ArtCaptures/LeafMap/Play_"+name.Substring(3)+".png",texture.EncodeToPNG());
                }
                finally {camera.targetTexture=before;}
            }
            File.WriteAllText("ArtCaptures/LeafMap/PlayCapture.txt","Bubbles alive after "+Wait+" s: "+bubbles);
        }
        finally {RenderTexture.active=old;target.Release();Object.Destroy(target);Object.Destroy(texture);EditorApplication.ExitPlaymode();}
    }
}
