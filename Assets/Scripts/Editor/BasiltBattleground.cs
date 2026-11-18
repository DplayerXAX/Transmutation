using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class BasiltSceneSetup
{
    private static void BuildBattleApproaches(Transform parent)
    {
        var ramps=new GameObject("Broad basalt approaches").transform;ramps.SetParent(parent);
        Ramp(ramps,"South approach",new Vector3(-25,0,-175),new Vector3(-25,36,-54),24);
        Ramp(ramps,"East approach",new Vector3(125,0,-42),new Vector3(-4,36,-42),22);
    }
    private static void Ramp(Transform parent,string name,Vector3 start,Vector3 end,float width)
    {
        start.y=BasiltDensity.GroundHeight(start.x,start.z)-0.35f;
        var side=Vector3.Cross(Vector3.up,(end-start).normalized).normalized*width*0.5f;
        var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();
        for(int i=0;i<=24;i++)
        {
            var c=Vector3.Lerp(start,end,i/24f);c.y=Mathf.Lerp(start.y,end.y,Mathf.Clamp01(i/24f/0.65f));
            foreach(float sign in new[]{-1f,1f})
            {
                var p=c+side*sign;vertices.Add(p);uv.Add(new Vector2(sign,i));
                vertices.Add(new Vector3(p.x,BasiltDensity.GroundHeight(p.x,p.z)-2,p.z));uv.Add(new Vector2(sign,0));
            }
            if(i==0)continue;int a=(i-1)*4,b=i*4;
            indices.AddRange(new[]{a,b,a+2,a+2,b,b+2,a,a+1,b,a+1,b+1,b,a+2,b+2,a+3,a+3,b+2,b+3});
        }
        MeshObject(name,parent,vertices,indices,uv,basalt,true);
    }
    public static void CaptureBattleground(string file,Vector3 position,Vector3 target)
    {
        var go=new GameObject("Temporary battleground inspection camera");
        try
        {
            var camera=go.AddComponent<Camera>();camera.transform.position=position;
            camera.transform.LookAt(target);camera.fieldOfView=57;camera.nearClipPlane=0.15f;camera.farClipPlane=1500;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.065f,0.085f,0.08f);
            var rt=new RenderTexture(1600,1000,24);var old=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();
                System.IO.Directory.CreateDirectory("ArtCaptures/BasiltBattleground");
                System.IO.File.WriteAllBytes("ArtCaptures/BasiltBattleground/"+file+".png",texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally{RenderTexture.active=old;camera.targetTexture=null;Object.DestroyImmediate(rt);}
        }
        finally{Object.DestroyImmediate(go);}
    }
}

