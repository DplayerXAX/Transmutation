using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Clean normal-speed camera capture; explicit invocation only.</summary>
public sealed class LeafGardenCapture : MonoBehaviour
{
    private bool recording;
    public void Capture(int frames) { if(!recording) StartCoroutine(Record(Mathf.Clamp(frames,1,180))); }
    private IEnumerator Record(int count)
    {
        recording=true; var show=GetComponent<LeafGardenShowcase>(); show.ShowControls=false;
        Camera cam=show.Leaves.ViewCamera;
        string folder=count==1?"ArtCaptures/LeafGarden":"Temp/LeafGardenCapture"; Directory.CreateDirectory(folder);
        string prefix=(show.Landscape?"Meadow_":"")+show.Garden.Phase+"_"+show.Garden.Height+"m_"+show.View;
        var target=new RenderTexture(960,540,24); target.Create();
        var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
        var previous=cam.targetTexture; cam.targetTexture=target;
        var times=new List<string> {"frame,game_time,realtime"};
        try
        {
            float next=Time.unscaledTime;
            for(int i=0;i<count;i++)
            {
                while(Time.unscaledTime<next) yield return null;
                yield return new WaitForEndOfFrame();
                var old=RenderTexture.active; RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,960,540),0,0); texture.Apply(); RenderTexture.active=old;
                File.WriteAllBytes(folder+"/"+prefix+"_"+i.ToString("D3")+".png",texture.EncodeToPNG());
                times.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1:F4},{2:F4}",i,Time.time,Time.realtimeSinceStartup));
                next=Time.unscaledTime+(count>60?0.25f:0.1f);
            }
            File.WriteAllText(folder+"/capture.txt",$"{prefix}: {count} frames, requested interval {(count>60?0.25f:0.1f)}s; GPU {SystemInfo.graphicsDeviceName}; {show.Leaves.Capacity} live leaves. Capture readback affects frame timing; do not benchmark these frames.");
            File.WriteAllLines(folder+"/"+prefix+"_times.csv",times);
        }
        finally { cam.targetTexture=previous; target.Release(); Destroy(target); Destroy(texture); recording=false; }
    }
}
