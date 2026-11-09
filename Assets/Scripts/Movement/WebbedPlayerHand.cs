using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Four-digit, webbed hand. Three fingers share a curl; the opposing thumb closes separately.</summary>
public sealed class WebbedPlayerHand : MonoBehaviour
{
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly List<Material> materials = new List<Material>();
    readonly List<Transform> bones = new List<Transform>();
    readonly HashSet<Renderer> anatomyRenderers = new HashSet<Renderer>();
    readonly Transform[,] fingers = new Transform[3, 3];
    Transform thumb;
    float curl, thumbCurl;
    float side;
    Mesh armSurface;
    Transform armFrame;
    Vector3[] armVertices, armNormals;
    const int ArmRings = 41, ArmSides = 20;
    public Material Skin { get; private set; }
    public Material Bone { get; private set; }
    public Material Vein { get; private set; }
    Material fleckMaterial;
    public bool OwnsRenderer(Renderer renderer) => anatomyRenderers.Contains(renderer);

    public void UpdateEmission(Color playerEmission)
    {
        float strength = Mathf.Max(playerEmission.r, Mathf.Max(playerEmission.g, playerEmission.b));
        Skin.SetColor("_EmissionColor", playerEmission * 0.65f);
        // Preserve the blue skeleton and pink vessels while following the player's brightness.
        Bone.SetColor("_EmissionColor", new Color(0.12f,0.55f,0.95f) * strength * 1.2f);
        Vein.SetColor("_EmissionColor", new Color(1f,0.22f,0.53f) * strength * 0.18f);
        fleckMaterial.SetColor("_EmissionColor", new Color(0.86f,1f,0.98f) * strength * 0.25f);
    }

    public void Initialize(float handedness)
    {
        side = handedness;
        var skinTemplate = Resources.Load<Material>("WebbedPlayerSkin");
        Skin = skinTemplate != null ? new Material(skinTemplate) :
            new Material(Shader.Find("Transmutation/Webbed Player Skin"));
        Skin.name = "Translucent teal skin"; materials.Add(Skin);
        var interiorShader = Shader.Find("Transmutation/Webbed Player Interior");
        Bone = MakeMaterial(interiorShader, "Blue internal bone", new Color(0.12f, 0.55f, 0.95f));
        Vein = MakeMaterial(interiorShader, "Interrupted pink vessels", new Color(1f, 0.22f, 0.53f));
        fleckMaterial = MakeMaterial(interiorShader, "Suspended white flecks", new Color(0.86f, 1f, 0.98f));
        Vein.SetFloat("_Effect",1); Vein.SetVector("_Drift", new Vector4(0.0008f,0.0005f,0.0006f,0));
        fleckMaterial.SetFloat("_Effect",2);
        Vein.SetFloat("_Phase",side*2f);fleckMaterial.SetFloat("_Phase",side*1.3f);
        bones.Add(transform);
        var skin = new Builder(); var skeleton = new Builder(); var vessels = new Builder(); var dots = new Builder();
        // Rounded flattened palm, tapering into the wrist. All dimensions are metres.
        skin.Ellipsoid(new Vector3(0, 0, 0.058f), new Vector3(0.052f, 0.021f, 0.073f), 0);
        skin.Tube(new Vector3(0, 0, -0.025f), new Vector3(0, 0, 0.045f), 0.025f, 0.018f, 0, 0);
        skeleton.BoneShape(new Vector3(0, 0, -0.013f), new Vector3(0, 0, 0.011f), 0.009f, 0.007f, 0);
        for (int f = 0; f < 3; f++)
        {
            float x = (f - 1) * 0.031f * side;
            float z = f == 1 ? 0.117f : 0.108f;
            float length = f == 1 ? 0.035f : 0.031f;
            int parent = 0;
            Vector3 start = new Vector3(x, 0, z);
            Vector3 metacarpalStart=new Vector3(x*0.4f,0,0.025f);
            Vector3 metacarpalEnd=start-Vector3.forward*0.006f;
            skeleton.BoneShape(metacarpalStart,metacarpalEnd,0.006f,0.005f,0);
            Vector3 vesselOffset=new Vector3(0.010f*side,0.006f,0);
            Vector3 vesselDirection=(metacarpalEnd-metacarpalStart).normalized;
            vessels.Vessel(metacarpalStart+vesselDirection*0.009f+vesselOffset,
                metacarpalEnd-vesselDirection*0.009f+vesselOffset,0.002f,0.0016f,0);
            for (int j = 0; j < 3; j++)
            {
                int index = AddBone("Finger " + f + " Joint " + j, start, parent);
                fingers[f, j] = bones[index];
                Vector3 end = start + Vector3.forward * length;
                float radius = 0.0155f - j * 0.002f;
                skin.Tube(start, end, radius, radius * 0.84f, index, index);
                skin.Ellipsoid(start, new Vector3(radius, radius * 0.84f, radius), index);
                if (j == 2) skin.Ellipsoid(end, new Vector3(radius, radius * 0.84f, radius), index);
                skeleton.BoneShape(start + Vector3.forward * 0.004f, end - Vector3.forward * 0.004f, 0.0048f, 0.0042f, index);
                vessels.Vessel(start + new Vector3(radius * 0.60f, 0.004f, 0.005f),
                    end + new Vector3(radius * 0.60f, 0.004f, -0.005f), 0.0016f, 0.0013f, index);
                Scatter(dots, start + Vector3.forward * length * 0.5f, new Vector3(radius * 0.8f, radius * 0.6f, length * 0.5f), index, 12, f * 83 + j * 19);
                parent = index; start = end; length *= 0.78f;
            }
        }
        // Closed, softly scalloped membranes, weighted between each adjacent pair of fingers.
        for (int f = 0; f < 2; f++)
            skin.Web(bones.IndexOf(fingers[f, 0]), bones.IndexOf(fingers[f + 1, 0]),
                fingers[f, 0].localToWorldMatrix.MultiplyPoint3x4(Vector3.zero),
                fingers[f + 1, 0].localToWorldMatrix.MultiplyPoint3x4(Vector3.zero), transform, 0.038f);
        int thumbIndex = AddBone("Opposing Thumb", new Vector3(-0.041f * side, 0, 0.04f), 0);
        thumb = bones[thumbIndex];
        Vector3 thumbStart = new Vector3(-0.041f * side, 0, 0.04f);
        Vector3 thumbEnd = new Vector3(-0.087f * side, 0, 0.102f);
        skin.Tube(thumbStart, thumbEnd, 0.018f, 0.015f, thumbIndex, thumbIndex);
        skin.Ellipsoid(thumbEnd, new Vector3(0.017f, 0.015f, 0.019f), thumbIndex);
        Vector3 thumbAxis=(thumbEnd-thumbStart).normalized;
        Vector3 thumbMiddle=Vector3.Lerp(thumbStart,thumbEnd,0.55f);
        skeleton.BoneShape(thumbStart+thumbAxis*0.004f,thumbMiddle-thumbAxis*0.003f,0.0055f,0.0045f,thumbIndex);
        skeleton.BoneShape(thumbMiddle+thumbAxis*0.003f,thumbEnd-thumbAxis*0.004f,0.005f,0.0042f,thumbIndex);
        vessels.Vessel(thumbStart+new Vector3(-0.006f*side,0.006f,0)+thumbAxis*0.009f,
            thumbEnd+new Vector3(-0.006f*side,0.006f,0)-thumbAxis*0.009f,0.0018f,0.0015f,thumbIndex);
        skin.Web(thumbIndex, bones.IndexOf(fingers[0, 0]), transform.TransformPoint(thumbStart),
            fingers[0, 0].position, transform, 0.019f);
        Scatter(dots, new Vector3(0, 0, 0.06f), new Vector3(0.044f, 0.015f, 0.056f), 0, 100, 712);
        Scatter(dots, (thumbStart + thumbEnd) * 0.5f, new Vector3(0.012f, 0.009f, 0.022f), thumbIndex, 20, 91);
        Render("Blue skeleton", skeleton, Bone);
        Render("Pink vessels", vessels, Vein);
        Render("White inclusions", dots, fleckMaterial);
        skin = new Builder();
        skin.HandSurface(side, bones, transform);
        Render("Webbed skin", skin, Skin);
    }

    int AddBone(string label, Vector3 position, int parent)
    {
        var bone = new GameObject(label).transform;
        bone.SetParent(bones[parent], false);
        bone.position = transform.TransformPoint(position);
        bones.Add(bone); return bones.Count - 1;
    }

    public void Pose(float fingerGrip, float opposingGrip, float deltaTime)
    {
        curl = Mathf.MoveTowards(curl, Mathf.Clamp01(fingerGrip), deltaTime * 5f);
        thumbCurl = Mathf.MoveTowards(thumbCurl, Mathf.Clamp01(opposingGrip), deltaTime * 4f);
        for (int f = 0; f < 3; f++)
            for (int j = 0; j < 3; j++)
                fingers[f, j].localRotation = Quaternion.Euler(Mathf.Lerp(3f, j == 0 ? 65f : 72f, curl), 0, 0);
        thumb.localRotation = Quaternion.Euler(35f * thumbCurl, 36f * side * thumbCurl, -25f * side * thumbCurl);
    }

    // Internal details inherit the IK segment transforms. The outer skin is one continuous mesh.
    public void DressArm(Transform segment)
    {
        segment.GetComponent<Renderer>().enabled = false;
        var boneBuilder = new Builder();
        boneBuilder.BoneShape(new Vector3(0,-0.90f,0),new Vector3(0,0.88f,0),0.16f,0.13f,0);
        var core = new GameObject("Internal blue bone",typeof(MeshFilter),typeof(MeshRenderer));
        core.transform.SetParent(segment,false);
        var boneMesh = boneBuilder.ToMesh(); meshes.Add(boneMesh);
        core.GetComponent<MeshFilter>().sharedMesh=boneMesh;
        core.GetComponent<MeshRenderer>().sharedMaterial = Bone;
        core.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        anatomyRenderers.Add(core.GetComponent<MeshRenderer>());
        for (int i = 0; i < 4; i++)
        {
            var vessel = new GameObject("Longitudinal tapered vessel",typeof(MeshFilter),typeof(MeshRenderer));
            vessel.transform.SetParent(segment, false);
            // Interrupted strands run lengthwise beside the bone, with a gap between each strand.
            float from=i<2?-0.78f:0.05f,to=i<2?-0.08f:0.73f;
            float x=i%2==0?0.24f:-0.24f;
            var vesselBuilder=new Builder();
            vesselBuilder.Vessel(new Vector3(x,from,0.10f),new Vector3(x,to,0.10f),0.034f,0.027f,0);
            var vesselMesh=vesselBuilder.ToMesh();meshes.Add(vesselMesh);
            var vesselBounds=vesselMesh.bounds;vesselBounds.Expand(0.12f);vesselMesh.bounds=vesselBounds;
            vessel.GetComponent<MeshFilter>().sharedMesh=vesselMesh;
            vessel.GetComponent<Renderer>().sharedMaterial = Vein;
            vessel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            anatomyRenderers.Add(vessel.GetComponent<Renderer>());
            var vesselProperties = new MaterialPropertyBlock();
            vesselProperties.SetVector("_Drift",new Vector4(0.018f,0.008f,0.018f,0));
            vesselProperties.SetFloat("_Phase",i*1.73f+side*2f);
            vessel.GetComponent<Renderer>().SetPropertyBlock(vesselProperties);
        }
        var dots = new Builder();
        Scatter(dots, Vector3.zero, new Vector3(0.30f, 0.78f, 0.30f), 0, 90, 121, 18f);
        Mesh mesh = dots.ToMesh(); meshes.Add(mesh);
        var go = new GameObject("Suspended flecks", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(segment, false); go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = fleckMaterial;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        anatomyRenderers.Add(go.GetComponent<MeshRenderer>());
        var fleckProperties = new MaterialPropertyBlock();
        fleckProperties.SetVector("_Drift",new Vector4(0.018f,0.009f,0.018f,0));
        go.GetComponent<MeshRenderer>().SetPropertyBlock(fleckProperties);
        // Vertex drift remains well inside the skin, and the culling bounds include its full range.
        var bounds=mesh.bounds;bounds.Expand(0.06f);mesh.bounds=bounds;
    }

    /// <summary>A single tapered sleeve, with shared rings through the elbow rather than separate joint balls.</summary>
    public void CreateArmSurface(Transform parent)
    {
        var go = new GameObject("Continuous arm skin",typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(parent,false);
        armFrame=go.transform;
        armSurface = new Mesh { name="Continuous anatomical arm" }; armSurface.MarkDynamic(); meshes.Add(armSurface);
        armVertices = new Vector3[ArmRings*ArmSides]; armNormals = new Vector3[armVertices.Length];
        var uv = new Vector2[armVertices.Length]; var triangles = new int[(ArmRings-1)*ArmSides*6];
        int index=0;
        for(int r=0;r<ArmRings;r++)for(int s=0;s<ArmSides;s++)
        {
            uv[r*ArmSides+s]=new Vector2(0.5f,Mathf.Lerp(0.8f,-0.076f,r/(float)(ArmRings-1)));
            if(r==ArmRings-1)continue;
            int a=r*ArmSides+s,b=r*ArmSides+(s+1)%ArmSides;
            triangles[index++]=a;triangles[index++]=b;triangles[index++]=a+ArmSides;
            triangles[index++]=b;triangles[index++]=b+ArmSides;triangles[index++]=a+ArmSides;
        }
        armSurface.vertices=armVertices;armSurface.uv=uv;armSurface.triangles=triangles;
        go.GetComponent<MeshFilter>().sharedMesh=armSurface;
        go.GetComponent<MeshRenderer>().sharedMaterial=Skin;
        go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        anatomyRenderers.Add(go.GetComponent<MeshRenderer>());
    }

    public void PoseArmSurface(Vector3 shoulder,Vector3 elbow,Vector3 wrist,float thickness)
    {
        if(armSurface==null)return;
        // Geometry is stored under the limb root, independently of the hand rotation.
        Transform frame=armFrame;
        Vector3 upper=(elbow-shoulder).normalized,lower=(wrist-elbow).normalized;
        Vector3 bendStart=elbow-upper*0.055f,bendEnd=elbow+lower*0.055f;
        Vector3 wristEnd=wrist-transform.forward*0.019f;
        Vector3 previousRight=Vector3.ProjectOnPlane(transform.right,upper).normalized;
        if(previousRight.sqrMagnitude<0.01f)
            previousRight=Vector3.Cross(upper,Mathf.Abs(upper.y)>0.9f?Vector3.forward:Vector3.up).normalized;
        for(int r=0;r<ArmRings;r++)
        {
            float t=r/(float)(ArmRings-1); Vector3 center,tangent;
            if(t<0.40f){center=Vector3.Lerp(shoulder,bendStart,t/0.40f);tangent=upper;}
            else if(t<0.60f)
            {
                float u=(t-0.40f)/0.20f;
                center=(1-u)*(1-u)*bendStart+2*(1-u)*u*elbow+u*u*bendEnd;
                tangent=Vector3.Lerp(elbow-bendStart,bendEnd-elbow,u).normalized;
            }
            else{center=Vector3.Lerp(bendEnd,wristEnd,(t-0.60f)/0.40f);tangent=(wristEnd-bendEnd).normalized;}
            Vector3 right=Vector3.ProjectOnPlane(previousRight,tangent).normalized;
            if(right.sqrMagnitude<0.01f)
                right=Vector3.Cross(tangent,Mathf.Abs(tangent.y)>0.9f?Vector3.forward:Vector3.up).normalized;
            Vector3 up=Vector3.Cross(tangent,right).normalized;previousRight=right;
            float radius;
            if(t<0.40f)radius=Mathf.Lerp(thickness*0.52f,thickness*0.38f,t/0.40f);
            else if(t<0.60f)radius=thickness*0.38f;
            else
            {
                float u=(t-0.60f)/0.40f;
                radius=Mathf.Lerp(thickness*0.39f,0.0275f,u)+Mathf.Sin(u*Mathf.PI)*thickness*0.075f;
            }
            for(int s=0;s<ArmSides;s++)
            {
                float angle=s*Mathf.PI*2/ArmSides;
                float flatten=Mathf.Lerp(0.82f,0.70f,Mathf.InverseLerp(0.60f,1f,t));
                Vector3 offset=right*(Mathf.Cos(angle)*radius)+up*(Mathf.Sin(angle)*radius*flatten);
                int i=r*ArmSides+s;
                armVertices[i]=frame.InverseTransformPoint(center+offset);
                armNormals[i]=frame.InverseTransformDirection(offset.normalized);
            }
        }
        armSurface.vertices=armVertices;armSurface.normals=armNormals;armSurface.RecalculateBounds();
    }

    Material MakeMaterial(Shader shader, string label, Color color)
    {
        var template = Resources.Load<Material>("WebbedPlayerInterior");
        var material = template != null ? new Material(template) : new Material(shader);
        material.name = label;
        material.SetColor("_BaseColor", color); materials.Add(material); return material;
    }
    void Render(string label, Builder builder, Material material)
    {
        Mesh mesh = builder.ToMesh(); meshes.Add(mesh);
        var binds = new Matrix4x4[bones.Count];
        for (int i = 0; i < binds.Length; i++) binds[i] = bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
        mesh.bindposes = binds;
        var go = new GameObject(label, typeof(SkinnedMeshRenderer)); go.transform.SetParent(transform, false);
        var renderer = go.GetComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
        renderer.bones = bones.ToArray(); renderer.rootBone = transform;
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
        anatomyRenderers.Add(renderer);
        renderer.localBounds = new Bounds(new Vector3(0, 0, 0.08f), Vector3.one * 0.6f);
    }
    static void Scatter(Builder builder, Vector3 center, Vector3 extent, int bone, int count, int seed, float scale = 1f)
    {
        var random = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);
            if (p.sqrMagnitude > 1) { i--; continue; }
            float size = 0.00045f + (float)random.NextDouble() * 0.00055f;
            builder.Octahedron(center + Vector3.Scale(p, extent), size * scale, bone);
        }
    }
    void OnDestroy()
    {
        foreach (var mesh in meshes) if (mesh != null) Release(mesh);
        foreach (var material in materials) if (material != null) Release(material);
    }
    static void Release(Object resource)
    { if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource); }

    sealed class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector4> motion = new List<Vector4>();
        readonly List<int> triangles = new List<int>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();
        readonly List<Vector3> normals = new List<Vector3>();
        public void HandSurface(float side, List<Transform> bones, Transform root)
        {
            const float step = 0.004f;
            const int nx = 56, ny = 18, nz = 70;
            // Open wrist boundary meets the arm sleeve; no hemispherical cap inside the forearm.
            Vector3 origin = new Vector3(-0.112f,-0.036f,-0.019f);
            var values = new float[nx+1,ny+1,nz+1];
            for(int x=0;x<=nx;x++)for(int y=0;y<=ny;y++)for(int z=0;z<=nz;z++)
                values[x,y,z]=Field(origin+new Vector3(x,y,z)*step,side);
            Vector3[] corners = {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1)};
            int[,] tetra = {{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
            int[,] edges={{0,1},{0,2},{0,3},{1,2},{1,3},{2,3}};
            var points=new Vector3[4]; var density=new float[4]; var polygon=new Vector3[4];
            for(int x=0;x<nx;x++)for(int y=0;y<ny;y++)for(int z=0;z<nz;z++)
            for(int t=0;t<6;t++)
            {
                int inside=0;
                for(int c=0;c<4;c++)
                {
                    Vector3 offset=corners[tetra[t,c]];
                    points[c]=origin+(new Vector3(x,y,z)+offset)*step;
                    density[c]=values[x+(int)offset.x,y+(int)offset.y,z+(int)offset.z];
                    if(density[c]<0)inside++;
                }
                if(inside==0||inside==4)continue;
                int count=0;
                for(int e=0;e<6;e++)
                {
                    int a=edges[e,0],b=edges[e,1];
                    if((density[a]<0)==(density[b]<0))continue;
                    polygon[count++]=Vector3.Lerp(points[a],points[b],density[a]/(density[a]-density[b]));
                }
                Vector3 center=Vector3.zero;for(int i=0;i<count;i++)center+=polygon[i];center/=count;
                Vector3 normal=Gradient(center,side), axis=(polygon[0]-center).normalized, tangent=Vector3.Cross(normal,axis);
                // Sort the four edge intersections around the surface normal before triangulating.
                for(int a=0;a<count-1;a++)for(int b=a+1;b<count;b++)
                    if(Mathf.Atan2(Vector3.Dot(polygon[a]-center,tangent),Vector3.Dot(polygon[a]-center,axis))>
                       Mathf.Atan2(Vector3.Dot(polygon[b]-center,tangent),Vector3.Dot(polygon[b]-center,axis)))
                    {Vector3 swap=polygon[a];polygon[a]=polygon[b];polygon[b]=swap;}
                int first=vertices.Count;
                for(int i=0;i<count;i++)
                {
                    Vector3 p=polygon[i]; int bone=0,other=0;float blend=0;
                    float thumbInfluence=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.035f,0.060f,-p.x*side)) *
                        (1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.09f,0.13f,p.z)));
                    if(p.z>0.085f)
                    {
                        int finger=Mathf.Clamp(Mathf.RoundToInt(p.x*side/0.031f)+1,0,2);
                        int baseIndex=1+finger*3;
                        float firstZ=root.InverseTransformPoint(bones[baseIndex].position).z;
                        if(p.z<firstZ){other=baseIndex;blend=Mathf.InverseLerp(0.085f,firstZ,p.z);}
                        else
                        {
                            bone=baseIndex;
                            for(int j=0;j<2;j++)
                            {
                                float from=root.InverseTransformPoint(bones[baseIndex+j].position).z;
                                float to=root.InverseTransformPoint(bones[baseIndex+j+1].position).z;
                                if(p.z>=from){bone=baseIndex+j;other=bone+1;blend=Mathf.InverseLerp(from,to,p.z);}
                            }
                        }
                    }
                    int vertex=Add(p,bone,other,blend);
                    weights[vertex]=new BoneWeight {
                        boneIndex0=bone,weight0=(1-blend)*(1-thumbInfluence),
                        boneIndex1=other,weight1=blend*(1-thumbInfluence),
                        boneIndex2=bones.Count-1,weight2=thumbInfluence };
                    normals.Add(Gradient(p,side));
                }
                for(int i=1;i<count-1;i++)Tri(first,first+i,first+i+1);
            }
        }
        static float SmoothMin(float a,float b)
        { const float k=0.012f;float h=Mathf.Clamp01(0.5f+0.5f*(b-a)/k);return Mathf.Lerp(b,a,h)-k*h*(1-h); }
        static float Capsule(Vector3 p,Vector3 a,Vector3 b,float radius)
        { Vector3 d=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude);return (p-a-d*t).magnitude-radius; }
        static float Ellipse(Vector3 p,Vector3 center,Vector3 radii)
        { p-=center;return (new Vector3(p.x/radii.x,p.y/radii.y,p.z/radii.z).magnitude-1)*Mathf.Min(radii.x,Mathf.Min(radii.y,radii.z)); }
        static float Field(Vector3 p,float side)
        {
            float d=Ellipse(p,new Vector3(0,0,0.063f),new Vector3(0.055f,0.023f,0.071f));
            d=SmoothMin(d,Ellipse(p,new Vector3(0,0,-0.005f),new Vector3(0.029f,0.020f,0.038f)));
            for(int f=0;f<3;f++)
            {
                float x=(f-1)*0.031f*side, z=f==1?0.117f:0.108f;
                float length=f==1?0.084f:0.075f;
                Vector3 q=p;q.y*=1.15f;
                d=SmoothMin(d,Capsule(q,new Vector3(x,0,z-0.01f),new Vector3(x,0,z+length),0.0145f));
                if(f<2)d=SmoothMin(d,Ellipse(p,new Vector3(x+0.0155f*side,0,z+0.018f),new Vector3(0.027f,0.009f,0.048f)));
            }
            d=SmoothMin(d,Capsule(p,new Vector3(-0.037f*side,0,0.037f),new Vector3(-0.087f*side,0,0.102f),0.018f));
            d=SmoothMin(d,Ellipse(p,new Vector3(-0.048f*side,0,0.073f),new Vector3(0.024f,0.010f,0.032f)));
            return d;
        }
        static Vector3 Gradient(Vector3 p,float side)
        {
            const float e=0.0003f;
            return new Vector3(Field(p+Vector3.right*e,side)-Field(p-Vector3.right*e,side),
                Field(p+Vector3.up*e,side)-Field(p-Vector3.up*e,side),
                Field(p+Vector3.forward*e,side)-Field(p-Vector3.forward*e,side)).normalized;
        }
        int Add(Vector3 v, int bone, int other = -1, float blend = 0)
        {
            int index = vertices.Count; vertices.Add(v); uv.Add(new Vector2(v.x * 5f + 0.5f, v.z * 4f));
            motion.Add(Vector4.zero);
            weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 - blend, boneIndex1 = other < 0 ? bone : other, weight1 = blend });
            return index;
        }
        void Tri(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        public void Ellipsoid(Vector3 center, Vector3 size, int bone)
        {
            int start = vertices.Count; const int rings = 10, sides = 16;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= sides; s++)
                {
                    float a = Mathf.PI * r / rings, b = Mathf.PI * 2 * s / sides;
                    Add(center + Vector3.Scale(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)), size), bone);
                }
            for (int r = 0; r < rings; r++) for (int s = 0; s < sides; s++)
            { int a = start + r * (sides + 1) + s, b = a + sides + 1; Tri(a, a + 1, b); Tri(a + 1, b + 1, b); }
        }
        public void Tube(Vector3 a, Vector3 b, float radius, float depth, int boneA, int boneB)
        {
            Vector3 direction = (b - a).normalized;
            Vector3 x = Vector3.Cross(direction, Vector3.up).normalized;
            if (x.sqrMagnitude < 0.1f) x = Vector3.right;
            Vector3 y = Vector3.Cross(direction, x).normalized;
            int start = vertices.Count; const int sides = 12;
            for (int r = 0; r <= 4; r++) for (int s = 0; s <= sides; s++)
            {
                float t = r / 4f, angle = s * Mathf.PI * 2 / sides;
                Add(Vector3.Lerp(a, b, t) + x * (Mathf.Cos(angle) * radius) + y * (Mathf.Sin(angle) * depth), boneA, boneB, t);
                uv[uv.Count-1]=new Vector2(s/(float)sides,t);
            }
            for (int r = 0; r < 4; r++) for (int s = 0; s < sides; s++)
            { int i = start + r * (sides + 1) + s; Tri(i, i + 1, i + sides + 1); Tri(i + 1, i + sides + 2, i + sides + 1); }
            int ca = Add(a, boneA), cb = Add(b, boneB);
            for (int s = 0; s < sides; s++) { Tri(ca, start + s + 1, start + s); Tri(cb, start + 4 * (sides + 1) + s, start + 4 * (sides + 1) + s + 1); }
        }
        public void BoneShape(Vector3 a,Vector3 b,float radius,float depth,int bone)
        { ProfiledSegment(a,b,radius,depth,bone,false); }
        public void Vessel(Vector3 a,Vector3 b,float radius,float depth,int bone)
        { ProfiledSegment(a,b,radius,depth,bone,true); }

        void ProfiledSegment(Vector3 a,Vector3 b,float radius,float depth,int bone,bool vessel)
        {
            Vector3 direction=(b-a).normalized;
            Vector3 x=Vector3.Cross(direction,Mathf.Abs(direction.y)>0.9f?Vector3.forward:Vector3.up).normalized;
            Vector3 y=Vector3.Cross(direction,x).normalized;
            const int rings=48,sides=16;
            int start=vertices.Count;
            float seed=Mathf.Repeat(Mathf.Sin(Vector3.Dot(a,new Vector3(127.1f,311.7f,74.7f)))*43758.5453f,1)*Mathf.PI*2;
            for(int r=0;r<=rings;r++)
            {
                float t=r/(float)rings;
                float envelope=Mathf.Sin(t*Mathf.PI);
                float profile;
                if(vessel)profile=Mathf.Pow(Mathf.Max(0,envelope),0.85f);
                else
                {
                    float headA=Mathf.Exp(-Mathf.Pow((t-0.14f)/0.12f,2));
                    float headB=Mathf.Exp(-Mathf.Pow((t-0.86f)/0.13f,2));
                    float capA=Mathf.Clamp01(t/0.035f),capB=Mathf.Clamp01((1-t)/0.035f);
                    float cap=Mathf.Sqrt((1-(1-capA)*(1-capA))*(1-(1-capB)*(1-capB)));
                    profile=cap*(0.66f+0.68f*headA+0.62f*headB);
                }
                if(r==0||r==rings)profile=0;
                Vector3 center=Vector3.Lerp(a,b,t)+x*(envelope*envelope*radius*(vessel?0.50f:0.16f));
                for(int s=0;s<=sides;s++)
                {
                    float angle=s*Mathf.PI*2/sides;
                    float lobes=vessel?1:1+0.10f*Mathf.Cos(angle*2)*Mathf.Pow(Mathf.Abs(t*2-1),2);
                    Vector3 radial=x*(Mathf.Cos(angle)*radius)+y*(Mathf.Sin(angle)*depth);
                    int index=Add(center+radial*(profile*lobes),bone);
                    uv[index]=new Vector2(s/(float)sides,t);
                    if(vessel)motion[index]=new Vector4(radius*profile,envelope,0,seed);
                }
            }
            for(int r=0;r<rings;r++)for(int s=0;s<sides;s++)
            {
                int i=start+r*(sides+1)+s;
                Tri(i,i+1,i+sides+1);Tri(i+1,i+sides+2,i+sides+1);
            }
        }
        public void Web(int a, int b, Vector3 worldA, Vector3 worldB, Transform root, float reach)
        {
            Vector3 from = root.InverseTransformPoint(worldA), to = root.InverseTransformPoint(worldB);
            const int columns = 12, rows = 4;
            int start = vertices.Count;
            for (int face = 0; face < 2; face++) for (int r = 0; r <= rows; r++) for (int c = 0; c <= columns; c++)
            {
                float t = c / (float)columns, v = r / (float)rows;
                float edge = reach * (1 - 0.32f * Mathf.Sin(t * Mathf.PI));
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.forward * Mathf.Lerp(-0.013f, edge, v);
                p.y += (face == 0 ? 1 : -1) * 0.004f;
                Add(p, a, b, t);
            }
            int layer = (rows + 1) * (columns + 1);
            for (int face = 0; face < 2; face++) for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            {
                int i = start + face * layer + r * (columns + 1) + c, j = i + columns + 1;
                if (face == 0) { Tri(i, j, i + 1); Tri(i + 1, j, j + 1); }
                else { Tri(i, i + 1, j); Tri(i + 1, j + 1, j); }
            }
            for (int c = 0; c < columns; c++)
            { int i = start + rows * (columns + 1) + c; Tri(i, i + 1, i + layer); Tri(i + 1, i + layer + 1, i + layer); }
        }
        public void Octahedron(Vector3 p, float size, int bone)
        {
            int start = vertices.Count;
            foreach (var d in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back }) Add(p + d * size, bone);
            float phase=Mathf.Repeat(Mathf.Sin(Vector3.Dot(p,new Vector3(127.1f,311.7f,74.7f)))*43758.5453f,1f)*Mathf.PI*2;
            for(int i=start;i<start+6;i++)motion[i]=new Vector4(p.x,p.y,p.z,phase);
            for (int i = 0; i < 4; i++) { int a = start + 2 + i, b = start + 2 + (i + 1) % 4; Tri(start, b, a); Tri(start + 1, a, b); }
        }
        public Mesh ToMesh()
        {
            var mesh = new Mesh { name = "Procedural webbed anatomy", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1,motion); mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            if(normals.Count==vertices.Count)mesh.SetNormals(normals);else mesh.RecalculateNormals();
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
