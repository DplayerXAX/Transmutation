using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>Reusable tapered membranes authored as mesh assets, not runtime allocations.</summary>
public static class SeaAngelMeshBuilder
{
    const string Folder="Assets/Prefabs/Creatures/Fishing/";
    public static void Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(FishingSceneSetup.FishPath);
        try
        {
            var visual=root.transform.Find("Visual");
            Set(visual.Find("Body"),Body(),Vector3.zero);
            Set(visual.Find("Left Wing"),Wing(-1),new Vector3(-.1f,.16f,0));
            Set(visual.Find("Right Wing"),Wing(1),new Vector3(.1f,.16f,0));
            var tail=visual.Find("Tail"); if(tail!=null) Object.DestroyImmediate(tail.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root,FishingSceneSetup.FishPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static Mesh Save(string name,List<Vector3> verts,List<int> triangles)
    {
        string path=Folder+name+".asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool fresh=mesh==null; if(fresh) mesh=new Mesh {name=name}; else mesh.Clear();
        mesh.SetVertices(verts); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if(fresh) AssetDatabase.CreateAsset(mesh,path); else EditorUtility.SetDirty(mesh); return mesh;
    }
    static void Set(Transform t,Mesh mesh,Vector3 pos)
    { t.localPosition=pos; t.localScale=Vector3.one; t.GetComponent<MeshFilter>().sharedMesh=mesh; }
    static Mesh Body()
    {
        var v=new List<Vector3>();var tri=new List<int>(); const int rings=22,sides=24;
        for(int y=0;y<=rings;y++)
        {
            float t=y/(float)rings; float radius=Mathf.Sin(Mathf.PI*t)*Mathf.Lerp(.018f,.17f,Mathf.SmoothStep(0,1,t));
            for(int a=0;a<sides;a++) { float angle=a*Mathf.PI*2/sides; v.Add(new Vector3(Mathf.Cos(angle)*radius,Mathf.Lerp(-.62f,.36f,t),Mathf.Sin(angle)*radius*.7f)); }
        }
        for(int y=0;y<rings;y++) for(int a=0;a<sides;a++) { int b=y*sides+a,c=y*sides+(a+1)%sides; tri.Add(b);tri.Add(c+sides);tri.Add(c);tri.Add(b);tri.Add(b+sides);tri.Add(c+sides); }
        return Save("Sea Angel Body",v,tri);
    }
    static Mesh Wing(int side)
    {
        var v=new List<Vector3>();var tri=new List<int>(); const int steps=16;
        for(int i=0;i<=steps;i++)
        {
            float t=i/(float)steps;float width=Mathf.Sin(Mathf.PI*t)*.21f;
            v.Add(new Vector3(side*t*.57f,Mathf.Sin(t*Mathf.PI)*.08f-width,Mathf.Sin(t*Mathf.PI)*.035f));
            v.Add(new Vector3(side*t*.57f,Mathf.Sin(t*Mathf.PI)*.08f+width,Mathf.Sin(t*Mathf.PI)*.035f));
        }
        for(int i=0;i<steps;i++) {int b=i*2;tri.Add(b);tri.Add(b+1);tri.Add(b+2);tri.Add(b+1);tri.Add(b+3);tri.Add(b+2);}
        return Save(side<0?"Sea Angel Left Wing":"Sea Angel Right Wing",v,tri);
    }
}
