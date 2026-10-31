using System.Collections.Generic;
using UnityEngine;

/// <summary>Combined, collider-free meshes for simple creature art.</summary>
public sealed class CreatureSculpture
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Color> colors = new List<Color>();
    private static Material material;
    public void Orb(Vector3 center, Vector3 size, Color color)
    {
        const int sides = 12, rows = 8;
        int start = vertices.Count;
        for (int y=0;y<=rows;y++) for (int x=0;x<=sides;x++)
        {
            float v=y*Mathf.PI/rows, u=x*Mathf.PI*2/sides;
            vertices.Add(center+Vector3.Scale(new Vector3(Mathf.Sin(v)*Mathf.Cos(u),Mathf.Cos(v),Mathf.Sin(v)*Mathf.Sin(u)),size));
            colors.Add(color);
        }
        for (int y=0;y<rows;y++) for (int x=0;x<sides;x++)
        {
            int a=start+y*(sides+1)+x,b=a+sides+1;
            triangles.Add(a); triangles.Add(a+1); triangles.Add(b);
            triangles.Add(a+1); triangles.Add(b+1); triangles.Add(b);
        }
    }
    public void Tube(Vector3 from, Vector3 to, float radius, Color color)
    {
        const int sides=6;
        Vector3 direction=(to-from).normalized;
        if(direction.sqrMagnitude<.1f) return;
        Vector3 right=Vector3.Cross(direction,Mathf.Abs(direction.y)>.9f?Vector3.forward:Vector3.up).normalized;
        Vector3 up=Vector3.Cross(direction,right); int start=vertices.Count;
        for(int j=0;j<2;j++) for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.PI*2/sides;
            vertices.Add((j==0?from:to)+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius); colors.Add(color);
        }
        for(int i=0;i<sides;i++)
        {
            int a=start+i,b=start+(i+1)%sides;
            triangles.Add(a);triangles.Add(b);triangles.Add(a+sides);
            triangles.Add(b);triangles.Add(b+sides);triangles.Add(a+sides);
        }
    }
    public void Ring(Vector3 center, Quaternion rotation, float radius, float thickness, Color color)
    {
        for(int i=0;i<40;i++)
        {
            float a=i*Mathf.PI*2/40,b=(i+1)*Mathf.PI*2/40;
            Tube(center+rotation*new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius,
                center+rotation*new Vector3(Mathf.Cos(b),Mathf.Sin(b),0)*radius,thickness,color);
        }
    }
    public GameObject Attach(Transform parent, string name)
    {
        var mesh=new Mesh { name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        if(material==null) material=Resources.Load<Material>("CreatureArtSurface");
        if(material==null) material=new Material(Shader.Find("Capstone/Creature/Sculpture")) {name="Creature Sculpture (shared)",enableInstancing=true};
        go.GetComponent<MeshRenderer>().sharedMaterial=material; return go;
    }
}
