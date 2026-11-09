using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays the leaf map with the player standing in the lily field and checks that the lilies round them turn
/// into real flowers, that one can be picked up and shoots, how many bubbles the field areas keep in the air,
/// and the frame time. Results: ArtCaptures/LeafMap/LilyTest.txt.
/// </summary>
[InitializeOnLoad]
public static class LeafMapLilyTest
{
    private const string Key="LeafMap.LilyTest";
    private static float placedAt=-1f;
    private static int frames;
    private static double sum;
    private static string lines;

    static LeafMapLilyTest(){EditorApplication.update+=Tick;}

    [MenuItem("Tools/Capstone/Leaf Map/Test Lily Field In Play Mode")]
    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(Key,true);placedAt=-1f;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying)return;
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid())return;
        var roots=scene.GetRootGameObjects();
        var world=roots.SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        var carrier=roots.SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>()).FirstOrDefault();
        if(world==null || !world.GenerationComplete || carrier==null)return;
        if(placedAt<0f)
        {
            Vector2 spot=LeafMapTerrain.ToWorld(new Vector2(-18,-60));
            carrier.PlayerBody.position=new Vector3(spot.x,LeafMapTerrain.Height(spot.x,spot.y)+1.5f,spot.y);
            carrier.transform.position=carrier.PlayerBody.position;Physics.SyncTransforms();
            placedAt=Time.time;frames=0;sum=0;lines="";
            return;
        }
        float since=Time.time-placedAt;
        if(since>5f && since<25f){sum+=Time.unscaledDeltaTime*1000.0;frames++;}
        if(since<3f || lines.Length>0 && since<30f)return;
        if(lines.Length==0)
        {
            var field=roots.SelectMany(g=>g.GetComponentsInChildren<LilyField>()).FirstOrDefault();
            var liveFlowers=Object.FindObjectsByType<BubbleFlower>(FindObjectsSortMode.None).Where(f=>f.name.Contains("(live)")).ToArray();
            lines+="Instanced lilies: "+(field!=null?field.Count:0)+"\nReal flowers round the player after 3 s: "+liveFlowers.Length;
            var nearest=liveFlowers.OrderBy(f=>(f.transform.position-carrier.PlayerBody.position).sqrMagnitude).FirstOrDefault();
            if(nearest!=null)
            {
                bool carried=carrier.TryCarryFromInteraction(nearest);
                nearest.ReceiveInteraction(new CreatureInteraction(carrier.gameObject,Vector3.zero,Vector3.forward));
                lines+="\nPicked up a field lily: "+(carried && nearest.IsCarried)+"\nIt shot a bubble while carried: "+(nearest.LastShot!=null);
                carrier.Release();
            }
            return;
        }
        int bubbles=Object.FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None).Length;
        lines+="\nBubbles in the air after 30 s: "+bubbles+"\nAverage frame (editor, 5-25 s): "+(sum/Mathf.Max(1,frames)).ToString("0.0")+" ms";
        File.WriteAllText("ArtCaptures/LeafMap/LilyTest.txt",lines);
        SessionState.SetBool(Key,false);placedAt=-1f;EditorApplication.ExitPlaymode();
    }
}
