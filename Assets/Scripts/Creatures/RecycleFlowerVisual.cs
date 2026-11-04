using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Curved procedural petals with stable topology. GPU interpolation unfurls each whorl in sequence.</summary>
[DisallowMultipleComponent]
public sealed class RecycleFlowerVisual : MonoBehaviour
{
    [SerializeField] Material organicMaterial;
    [SerializeField] Mesh model;
    [SerializeField] Vector3 mouthLocal = new Vector3(0f, 0.78f, 0f);
    Mesh ownedMesh;
    Material ownedMaterial;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;
    float bloom = 1f, life = 1f, pulse, jolt;
    public Vector3 MouthPosition => transform.TransformPoint(mouthLocal);
    public Mesh Model => model;

    void Awake() => EnsureModel();
    public void EnsureModel()
    {
        Transform existing = transform.Find("Procedural Flower");
        GameObject child = existing != null ? existing.gameObject : new GameObject("Procedural Flower");
        child.transform.SetParent(transform, false);
        var filter = child.GetComponent<MeshFilter>(); if (filter == null) filter = child.AddComponent<MeshFilter>();
        meshRenderer = child.GetComponent<MeshRenderer>(); if (meshRenderer == null) meshRenderer = child.AddComponent<MeshRenderer>();
        if (model == null || Vector3.Dot(transform.up, Vector3.up) < 0.98f)
        { ownedMesh = Build(); filter.sharedMesh = ownedMesh; }
        else filter.sharedMesh = model;
        if (organicMaterial != null) meshRenderer.sharedMaterial = organicMaterial;
        else { ownedMaterial = new Material(Shader.Find("Transmutation/Recycle Organic")); meshRenderer.sharedMaterial = ownedMaterial; }
        properties = new MaterialPropertyBlock(); Apply();
    }
    public Mesh Build()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var colours = new List<Color>();
        var uv = new List<Vector2>(); var closed = new List<Vector4>(); var timing = new List<Vector4>(); var t = new List<int>();
        Vector3 up = transform.InverseTransformDirection(Vector3.up).normalized;
        Vector3 stalkOut = Vector3.up * 0.20f;
        Vector3 head = stalkOut + up * 0.47f;
        mouthLocal = head + up * 0.10f;
        Vector3 headRight = Vector3.Cross(up, Mathf.Abs(up.z) < 0.9f ? Vector3.forward : Vector3.right).normalized;
        Vector3 headForward = Vector3.Cross(headRight, up).normalized;
        // Stable parallel-transported frame for the stalk, including flowers emerging from a wall.
        Vector3 transported = headRight;
        const int rings = 16, sides = 8;
        for (int row = 0; row <= rings; row++)
        {
            float u = row / (float)rings;
            Vector3 p = Stalk(u), tangent = (Stalk(Mathf.Min(1f, u + 0.01f)) - Stalk(Mathf.Max(0f, u - 0.01f))).normalized;
            transported = Vector3.ProjectOnPlane(transported, tangent).normalized;
            if (transported.sqrMagnitude < 0.1f) transported = headRight;
            Vector3 binormal = Vector3.Cross(tangent, transported);
            for (int col = 0; col < sides; col++)
            {
                float angle = col * Mathf.PI * 2f / sides;
                Vector3 radial = transported * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                Vector3 point = p + radial * Mathf.Lerp(0.035f, 0.055f, u * u * u);
                Add(point, radial, new Color(0.035f, 0.19f, 0.16f, 0.08f), new Vector2(u, col / (float)sides),
                    point * 0.12f, 0f, 0f);
                if (row == rings) continue;
                int a = row * sides + col, b = row * sides + (col + 1) % sides;
                Quad(a, b, a + sides, b + sides);
            }
        }
        Vector3 Stalk(float u) => stalkOut * (u * (2f - u)) + up * (0.47f * u * u);

        for (int whorl = 0; whorl < 2; whorl++)
        for (int petal = 0; petal < 6; petal++)
        {
            float angle = (petal + whorl * 0.5f) * Mathf.PI / 3f;
            Vector3 radial = headRight * Mathf.Cos(angle) + headForward * Mathf.Sin(angle), across = Vector3.Cross(up, radial);
            int start = v.Count; const int rows = 18, cols = 10;
            for (int row = 0; row <= rows; row++)
            for (int col = 0; col <= cols; col++)
            {
                float u = row / (float)rows, w = col / (float)cols * 2f - 1f;
                Vector3 point = Petal(u,w), du = Petal(Mathf.Min(1f,u+0.005f),w)-Petal(Mathf.Max(0f,u-0.005f),w);
                Vector3 dw = Petal(u,Mathf.Min(1f,w+0.005f))-Petal(u,Mathf.Max(-1f,w-0.005f));
                Vector3 normal = Vector3.Cross(dw, du).normalized;
                Color colour = whorl == 0 ? Color.Lerp(new Color(0.025f,0.15f,0.25f,0.10f), new Color(0.06f,0.46f,0.48f,0.22f),u) :
                    Color.Lerp(new Color(0.17f,0.015f,0.13f,0.08f),new Color(0.43f,0.025f,0.29f,0.20f),u);
                if (Mathf.Abs(w) > 0.82f) colour = Color.Lerp(colour, new Color(0.70f,0.055f,0.19f,0.3f), 0.75f);
                Vector3 bud = head + radial * (0.045f + Mathf.Sin(u * Mathf.PI) * 0.055f) + up * (u * 0.33f) + across * (w * 0.025f * Mathf.Sin(u*Mathf.PI));
                Add(point, normal, colour, new Vector2(u,w), bud, 0.12f + whorl * 0.15f + petal * 0.016f, 1f);
                if (row < rows && col < cols)
                { int a = start + row * (cols + 1) + col; Quad(a,a+1,a+cols+1,a+cols+2); }
            }
            Vector3 Petal(float u,float w)
            {
                float width = Mathf.Pow(Mathf.Max(0f,Mathf.Sin(u*Mathf.PI)),0.72f) * (whorl == 0 ? 0.17f : 0.12f);
                width *= 1f + 0.055f * Mathf.Sin(u*45f+petal);
                float outward = whorl == 0 ? 0.045f+u*0.43f : 0.055f+u*0.28f;
                float height = whorl == 0 ? -0.03f + Mathf.Sin(u*Mathf.PI)*0.045f + Mathf.Pow(u,6)*0.10f :
                    0.02f+u*0.37f+Mathf.Sin(u*Mathf.PI)*0.08f-Mathf.Pow(u,6)*0.11f;
                height += w*w*0.06f*Mathf.Sin(u*Mathf.PI);
                return head + radial*outward + up*height + across*(w*width);
            }
        }
        // Long curved orange/gold stamens form the luminous core seen in the reference.
        for (int filament = 0; filament < 9; filament++)
        {
            float angle = filament * 2.399963f;
            Vector3 radial = headRight*Mathf.Cos(angle)+headForward*Mathf.Sin(angle);
            Vector3 frame = Vector3.Cross(up,radial);
            int start = v.Count; const int rows = 16, cols = 5;
            for (int row = 0; row <= rows; row++)
            for (int col = 0; col < cols; col++)
            {
                float u = row/(float)rows, turn = col*Mathf.PI*2f/cols;
                Vector3 p = head + up*(u*(0.35f+0.08f*Mathf.Sin(filament*2f))) + radial*(0.025f+u*u*0.085f) + frame*(Mathf.Sin(u*5f+filament)*0.025f*u);
                Vector3 radialNormal=radial*Mathf.Cos(turn)+frame*Mathf.Sin(turn);
                float radius = Mathf.Lerp(0.013f,0.003f,u);
                Add(p+radialNormal*radius,radialNormal,Color.Lerp(new Color(0.98f,0.09f,0.015f,0.85f),new Color(1f,0.7f,0.05f,1f),u),
                    new Vector2(u,col/(float)cols), head,0.43f+filament*0.012f,0f);
                if (row<rows) { int a=start+row*cols+col,b=start+row*cols+(col+1)%cols; Quad(a,b,a+cols,b+cols); }
            }
        }
        void Add(Vector3 p,Vector3 normal,Color colour,Vector2 tex,Vector3 bud,float delay,float petal)
        { v.Add(p); n.Add(normal); colours.Add(colour); uv.Add(tex); closed.Add(new Vector4(bud.x,bud.y,bud.z,delay)); timing.Add(new Vector4(1f,0f,petal,0f)); }
        void Quad(int a,int b,int c,int d) { t.Add(a);t.Add(c);t.Add(b);t.Add(b);t.Add(c);t.Add(d); }
        var result = new Mesh { name="Procedural Recycle Flower", indexFormat=IndexFormat.UInt32 };
        result.SetVertices(v);result.SetNormals(n);result.SetColors(colours);result.SetUVs(0,uv);result.SetUVs(1,closed);result.SetUVs(2,timing);
        result.SetTriangles(t,0);result.RecalculateBounds();
        Bounds bounds=result.bounds;bounds.Expand(0.15f);result.bounds=bounds;
        return result;
    }
    public void SetState(float growth,float remainingLife,float outputPulse)
    { bloom=growth;life=remainingLife;pulse=outputPulse;Apply(); }
    public void Jolt() { jolt=1f;Apply(); }
    void Update()
    {
        if (jolt<=0f) return;
        jolt=Mathf.MoveTowards(jolt,0f,Time.deltaTime*5f);Apply();
    }
    void Apply()
    {
        if (meshRenderer==null || properties==null) return;
        properties.SetFloat("_Bloom",bloom);properties.SetFloat("_Life",life);properties.SetFloat("_Pulse",pulse+jolt);
        properties.SetFloat("_Clock",Time.time);meshRenderer.SetPropertyBlock(properties);
    }
    void OnDestroy()
    {
        if (ownedMesh!=null) { if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh); }
        if (ownedMaterial!=null) { if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial); }
    }
}
