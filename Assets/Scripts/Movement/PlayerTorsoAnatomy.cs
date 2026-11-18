using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Domed turtle carapace and paired bony chest armour under translucent skin;
/// human-style great vessels connect the neck, shoulders, abdomen and iliac branches.</summary>
public sealed class PlayerTorsoAnatomy : MonoBehaviour
{
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly HashSet<Renderer> anatomy = new HashSet<Renderer>();
    Material flecks, translucentBone;
    public Material BoneMaterial => translucentBone;
    public void UpdateEmission(Color emission) { if (flecks != null) flecks.SetColor("_EmissionColor", emission); if (translucentBone != null) translucentBone.SetColor("_EmissionColor", emission); }
    public bool OwnsRenderer(Renderer renderer) => anatomy.Contains(renderer);
    public void Build(Material skin, Material bone, Material vessel)
    {
        var skeleton = new Geometry(); var blood = new Geometry(); var surface = new Geometry(); var dots = new Geometry();
        skeleton.ShellPlates();
        skeleton.MarkBoneSurface();
        // Aortic root / arch, descending aorta and paired common iliacs.
        blood.Tube(new[] { new Vector3(-.045f, .67f, .065f), new Vector3(-.055f, .79f, .045f), new Vector3(-.10f, .83f, -.015f),
            new Vector3(-.09f, .73f, -.065f), new Vector3(-.045f, .53f, -.075f), new Vector3(-.035f, .19f, -.065f) }, .010f, .010f, true);
        // Superior / inferior vena cava to the right of the arterial trunk.
        blood.Tube(new[] { new Vector3(.045f, .99f, .015f), new Vector3(.055f, .79f, .025f), new Vector3(.045f, .66f, .055f),
            new Vector3(.055f, .45f, -.025f), new Vector3(.055f, .17f, -.03f) }, .008f, .008f, true);
        foreach (float s in new[] { -1f, 1f })
        {
            blood.Tube(new[] { new Vector3(-.07f, .80f, -.005f), new Vector3(s * .06f, .87f, .025f), new Vector3(s * .07f, 1, .025f) }, .0048f, .0048f, true);
            blood.Tube(new[] { new Vector3(-.07f, .80f, -.005f), new Vector3(s * .16f, .86f, .05f), new Vector3(s * .31f, .91f, .025f) }, .0055f, .0055f, true);
            blood.Tube(new[] { new Vector3(s * .31f, .885f, .05f), new Vector3(s * .16f, .82f, .065f), new Vector3(.045f, .77f, .055f) }, .004f, .004f, true);
            blood.Tube(new[] { new Vector3(-.035f, .19f, -.065f), new Vector3(s * .10f, .11f, -.015f), new Vector3(s * .15f, .015f, .055f) }, .006f, .006f, true);
            blood.Tube(new[] { new Vector3(s * .17f, .015f, .075f), new Vector3(s * .12f, .12f, .015f), new Vector3(.055f, .17f, -.03f) }, .005f, .005f, true);
            blood.Tube(new[] { new Vector3(-.045f, .43f, -.07f), new Vector3(s * .12f, .45f, -.025f), new Vector3(s * .17f, .40f, .015f) }, .0035f, .0035f, true);
            // Anterior internal thoracic / epigastric paths link the chest to the
            // abdominal wall, supplementing the deep axial and intercostal network.
            blood.Tube(new[] { new Vector3(s * .17f, .85f, .06f), new Vector3(s * .075f, .74f, .12f),
                new Vector3(s * .065f, .53f, .125f), new Vector3(s * .10f, .36f, .12f) }, .0028f, .0028f, true);
            blood.Tube(new[] { new Vector3(s * .14f, .075f, .065f), new Vector3(s * .11f, .23f, .115f),
                new Vector3(s * .09f, .39f, .12f) }, .0028f, .0028f, true);
            for (int i = 0; i < 4; i++)
            {
                float y = .25f + i * .10f;
                blood.Tube(new[] { new Vector3(s * .09f, y, .12f), new Vector3(s * .15f, y + .025f, .11f),
                    new Vector3(s * .19f, y + .045f, .08f) }, .0014f, .0014f, true);
            }
            for (int i = 0; i < 6; i++)
            {
                float y = .32f + i * .082f;
                blood.Tube(new[] { new Vector3(-.045f, y, -.07f), new Vector3(s * .14f, y + .015f, -.115f),
                    new Vector3(s * .25f, y - .025f, -.02f), new Vector3(s * .20f, y - .06f, .075f), new Vector3(s * .065f, y - .07f, .095f) }, .0023f, .0023f, true);
            }
        }
        blood.AnteriorVessels();
        skeleton.FitInsideSkin(.012f);
        blood.FitInsideSkin(.012f);
        surface.Skin();
        var random = new System.Random(171126);
        for (int i = 0; i < 170; i++)
        {
            float y = Mathf.Lerp(.08f, .94f, (float)random.NextDouble()), angle = (float)random.NextDouble() * Mathf.PI * 2;
            float radial = Mathf.Sqrt((float)random.NextDouble()) * .91f;
            var p = new Vector3(Mathf.Cos(angle) * Geometry.Width(y) * radial, y, Mathf.Sin(angle) * .14f * radial - .015f);
            dots.Octahedron(p, Mathf.Lerp(.0013f, .0027f, (float)random.NextDouble()));
        }
        dots.FitInsideSkin(.012f);
        flecks = new Material(vessel); flecks.name = "Torso suspended white inclusions";
        flecks.SetColor("_BaseColor", new Color(.86f, 1, .98f)); flecks.SetFloat("_Effect", 2);
        translucentBone = new Material(bone) { name = "Torso softly translucent blue bone", renderQueue = 2990 };
        translucentBone.SetFloat("_Opacity", .72f);
        translucentBone.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        translucentBone.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        translucentBone.SetFloat("_ZWrite", 0);
        translucentBone.SetOverrideTag("RenderType", "Transparent");
        Render("Domed turtle carapace and paired chest armour plates", skeleton, translucentBone);
        Render("Human great vessels and intercostal branches", blood, vessel);
        Render("Suspended white inclusions", dots, flecks);
        Render("Translucent torso skin", surface, skin);
    }
    void Render(string name, Geometry data, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
        var mesh = data.Mesh(name); meshes.Add(mesh); go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
        anatomy.Add(renderer);
    }
    void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Release(mesh); if (flecks != null) Release(flecks); if (translucentBone != null) Release(translucentBone); }
    static void Release(Object resource) { if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource); }
    sealed class Geometry
    {
        readonly List<Vector3> v = new List<Vector3>(); readonly List<int> triangles = new List<int>();
        readonly List<Vector2> uv = new List<Vector2>(); readonly List<Vector4> motion = new List<Vector4>();
        readonly Dictionary<int, Vector3> plateNormals = new Dictionary<int, Vector3>();
        int Add(Vector3 p, Vector2 tex, Vector4 flow) { int n = v.Count; v.Add(p); uv.Add(tex); motion.Add(flow); return n; }
        void Quad(int a, int b, int c, int d) { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(d); triangles.Add(c); }
        public void ShellPlates()
        {
            // Front silhouette traced from the user's drawing: two long Y-shaped
            // wings, a broad lower lobe, a tapered lower prong and a narrow inner gap.
            var front = new[] {
                new Vector2(.070f,.905f), new Vector2(.050f,.88f), new Vector2(.045f,.82f),
                new Vector2(.057f,.76f), new Vector2(.052f,.71f), new Vector2(.061f,.69f),
                new Vector2(.053f,.66f), new Vector2(.061f,.64f), new Vector2(.052f,.61f),
                new Vector2(.062f,.59f), new Vector2(.055f,.56f), new Vector2(.063f,.54f),
                new Vector2(.060f,.48f), new Vector2(.075f,.43f), new Vector2(.105f,.40f),
                new Vector2(.095f,.35f), new Vector2(.108f,.27f), new Vector2(.115f,.16f),
                new Vector2(.130f,.065f), new Vector2(.147f,.16f), new Vector2(.158f,.29f),
                new Vector2(.19f,.35f), new Vector2(.255f,.37f), new Vector2(.277f,.42f),
                new Vector2(.275f,.47f), new Vector2(.245f,.53f), new Vector2(.248f,.64f),
                new Vector2(.245f,.72f), new Vector2(.265f,.83f), new Vector2(.269f,.875f),
                new Vector2(.25f,.89f), new Vector2(.23f,.87f), new Vector2(.195f,.80f),
                new Vector2(.177f,.79f), new Vector2(.152f,.84f), new Vector2(.135f,.90f),
                new Vector2(.105f,.925f) };
            foreach (float side in new[] { -1f, 1f }) Plate(front, side, false);

            // Iliac bridge and paired hip sockets continue into the thigh roots.
            BoneStrut(new[]{new Vector3(-.17f,.025f,0),new Vector3(-.15f,.105f,-.025f),
                new Vector3(0,.15f,-.045f),new Vector3(.15f,.105f,-.025f),new Vector3(.17f,.025f,0)},.018f,.009f,false);
            // Back: broad scalloped central bone, thin ribs and a narrow shield frame.
            var axis = new List<Vector2> { new Vector2(-.065f,.94f), new Vector2(.065f,.94f), new Vector2(.079f,.86f) };
            for (int row = 0; row < 6; row++)
            {
                float y = .83f - row * .115f;
                float width = row < 4 ? .142f : row == 4 ? .126f : .095f;
                axis.Add(new Vector2(width * .77f,y)); axis.Add(new Vector2(width,y-.025f));
                axis.Add(new Vector2(width,y-.063f)); axis.Add(new Vector2(width*.73f,y-.09f));
            }
            axis.Add(new Vector2(.035f,.12f)); axis.Add(new Vector2(0,.085f));
            int end = axis.Count;
            for(int i=end-3;i>=2;i--) axis.Add(new Vector2(-axis[i].x,axis[i].y));
            Plate(axis.ToArray(), 1, true);
            var rim = new[] { new Vector3(0,.945f,-.14f), new Vector3(.20f,.925f,-.14f),
                new Vector3(.29f,.87f,-.13f), new Vector3(.305f,.66f,-.12f), new Vector3(.30f,.37f,-.12f),
                new Vector3(.27f,.17f,-.12f), new Vector3(.16f,.075f,-.13f), new Vector3(0,.035f,-.14f),
                new Vector3(-.16f,.075f,-.13f), new Vector3(-.27f,.17f,-.12f), new Vector3(-.30f,.37f,-.12f),
                new Vector3(-.305f,.66f,-.12f), new Vector3(-.29f,.87f,-.13f), new Vector3(-.20f,.925f,-.14f), new Vector3(0,.945f,-.14f) };
            BoneStrut(rim,.015f,.0045f,true);
            foreach(float side in new[]{-1f,1f}) for(int row=0;row<7;row++)
            {
                float y=.85f-row*.11f;
                BoneStrut(new[]{new Vector3(side*.085f,y,-.19f),new Vector3(side*.21f,y-.016f,-.17f),
                    new Vector3(side*(row==6?.265f:.298f),y-.025f,-.125f)},.0105f,.0035f,false);
            }
        }
        public void MarkBoneSurface()
        {
            // Opt-in for the interior shader's bone grain; limbs and vessels keep their own appearance.
            for(int i=0;i<motion.Count;i++){var data=motion[i];data.z=1;motion[i]=data;}
        }
        void BoneStrut(Vector3[] points,float halfWidth,float halfDepth,bool closed)
        {
            int steps=closed?180:44; const int sides=8; int start=v.Count;
            var section=new[]{new Vector2(1,.45f),new Vector2(.8f,1),new Vector2(-.8f,1),new Vector2(-1,.45f),
                new Vector2(-1,-.45f),new Vector2(-.8f,-1),new Vector2(.8f,-1),new Vector2(1,-.45f)};
            Vector3 Path(float t)
            {
                float f=Mathf.Clamp01(t)*(points.Length-1);int i=Mathf.Min(points.Length-2,(int)f);float u=f-i;
                Vector3 a=points[Mathf.Max(0,i-1)],b=points[i],c=points[i+1],d=points[Mathf.Min(points.Length-1,i+2)];
                return .5f*((2*b)+(-a+c)*u+(2*a-5*b+4*c-d)*u*u+(-a+3*b-3*c+d)*u*u*u);
            }
            for(int r=0;r<=steps;r++)
            {
                float t=r/(float)steps;Vector3 p=Path(t),axis=(Path(Mathf.Min(1,t+.001f))-Path(Mathf.Max(0,t-.001f))).normalized;
                if(closed)
                {
                    Vector3 outward=new Vector3(p.x,(p.y-.5f)*.7f,0).normalized;
                    float wave=.0065f*Mathf.Sin(t*Mathf.PI*22)+.003f*Mathf.Sin(t*Mathf.PI*38+1.2f);
                    p+=outward*wave; p.z+=.004f*Mathf.Sin(t*Mathf.PI*14+.4f);
                }
                Vector3 width=Vector3.Cross(Vector3.forward,axis).normalized;
                float flare=closed?1f: .72f+.8f*Mathf.Exp(-Mathf.Pow(t/.17f,2))+.9f*Mathf.Exp(-Mathf.Pow((1-t)/.16f,2));
                float grown=1+.065f*Mathf.Sin(t*51+points[0].y*17)+.04f*Mathf.Sin(t*97+1.3f);
                // Broad bevelled faces and thin edges, with expanded attachment ends.
                for(int s=0;s<=sides;s++)
                {
                    Vector2 q=section[s%sides];
                    Add(p+width*(q.x*halfWidth*flare*grown)+Vector3.forward*(q.y*halfDepth*(.9f+.12f*flare)),
                        new Vector2(s/(float)sides,t),Vector4.zero);
                }
            }
            for(int r=0;r<steps;r++)for(int s=0;s<sides;s++){int a=start+r*(sides+1)+s;Quad(a,a+1,a+sides+1,a+sides+2);}
            if(!closed)
                for(int s=1;s<sides-1;s++)
                {
                    triangles.Add(start);triangles.Add(start+s+1);triangles.Add(start+s);
                    int end=start+steps*(sides+1);triangles.Add(end);triangles.Add(end+s);triangles.Add(end+s+1);
                }
        }
        void Plate(Vector2[] source,float side,bool back)
        {
            var control=new List<Vector2>();
            foreach(var a in source)control.Add(new Vector2(a.x*side,a.y));
            var p=new List<Vector2>();
            // Rounded bone outlines, with small inner notches rather than large spikes.
            for(int i=0;i<control.Count;i++) for(int j=0;j<3;j++)
            {
                Vector2 a=control[(i+control.Count-1)%control.Count],b=control[i],c=control[(i+1)%control.Count],d=control[(i+2)%control.Count];
                float t=j/3f;
                p.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
            }
            float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
            float area=0;for(int i=0;i<p.Count;i++)area+=Cross(p[i],p[(i+1)%p.Count]);
            if(area<0)p.Reverse();
            float Height(Vector2 a) => back ? -.185f-.025f*Mathf.Sin(a.y*Mathf.PI)
                : .015f+.19f*Mathf.Sqrt(Mathf.Max(.025f,1-Mathf.Pow(a.x/.285f,2)))
                    *(.96f+.04f*Mathf.Sin(a.y*Mathf.PI));
            Vector3 Point(Vector2 a,bool inner)=>new Vector3(a.x,a.y,Height(a)+(inner?(back?.014f:-.014f):0));
            void SmallFace(Vector2 a,Vector2 b,Vector2 c)
            {
                int n=Add(Point(a,false),a,Vector4.zero);Add(Point(b,false),b,Vector4.zero);Add(Point(c,false),c,Vector4.zero);
                for(int i=0;i<3;i++)
                {
                    Vector2 at=new Vector2(v[n+i].x,v[n+i].y); const float e=.0005f;
                    float dx=(Height(at+Vector2.right*e)-Height(at-Vector2.right*e))/(2*e);
                    float dy=(Height(at+Vector2.up*e)-Height(at-Vector2.up*e))/(2*e);
                    plateNormals[n+i]=(new Vector3(-dx,-dy,1)*(back?-1:1)).normalized;
                }
                triangles.Add(n);triangles.Add(n+(back?2:1));triangles.Add(n+(back?1:2));
            }
            void Face(Vector2 a,Vector2 b,Vector2 c)
            {
                // Subdivide the silhouette triangles so the bowed surface is actual geometry.
                int steps=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((a-b).magnitude,Mathf.Max((b-c).magnitude,(c-a).magnitude))/.035f),1,24);
                Vector2 At(int row,int column)=>a+(b-a)*(row/(float)steps)+(c-a)*(column/(float)steps);
                for(int row=0;row<steps;row++)for(int column=0;column<steps-row;column++)
                {
                    SmallFace(At(row,column),At(row+1,column),At(row,column+1));
                    if(column<steps-row-1)SmallFace(At(row+1,column),At(row+1,column+1),At(row,column+1));
                }
            }
            var remaining=new List<int>();for(int i=0;i<p.Count;i++)remaining.Add(i);
            while(remaining.Count>2)
            {
                bool found=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int ia=remaining[(i+remaining.Count-1)%remaining.Count],ib=remaining[i],ic=remaining[(i+1)%remaining.Count];
                    Vector2 a=p[ia],b=p[ib],c=p[ic];if(Cross(b-a,c-b)<=.00000001f)continue;
                    bool occupied=false;
                    foreach(int k in remaining)if(k!=ia&&k!=ib&&k!=ic&&Cross(b-a,p[k]-a)>=0&&Cross(c-b,p[k]-b)>=0&&Cross(a-c,p[k]-c)>=0){occupied=true;break;}
                    if(occupied)continue;
                    Face(a,b,c);remaining.RemoveAt(i);found=true;break;
                }
                if(!found)throw new System.InvalidOperationException("Invalid reference bone silhouette.");
            }
            for(int i=0;i<p.Count;i++)
            {
                int next=(i+1)%p.Count;
                Vector3 Edge(int index,int ring)
                {
                    Vector2 tangent=(p[(index+1)%p.Count]-p[(index+p.Count-1)%p.Count]).normalized;
                    Vector2 outward=new Vector2(tangent.y,-tangent.x);
                    float depthOffset=ring==0?0:ring==1?.0025f:ring==2?.0115f:.014f;
                    float lip=ring==0||ring==3?0: .0013f;
                    Vector3 at=Point(p[index]+outward*lip,false);at.z+=(back?1:-1)*depthOffset;return at;
                }
                // A gently rolled bone edge replaces the sharp extruded cut edge.
                for(int ring=0;ring<3;ring++)
                {
                    int n=Add(Edge(i,ring),p[i],Vector4.zero);Add(Edge(next,ring),p[next],Vector4.zero);
                    Add(Edge(i,ring+1),p[i],Vector4.zero);Add(Edge(next,ring+1),p[next],Vector4.zero);
                    Quad(n,n+2,n+1,n+3);
                }
            }
        }
        public void Tube(Vector3[] points, float width, float depth, bool vessel = false)
        {
            const int steps = 48, sides = 10; int start = v.Count;
            float phase = points[0].y * 19 + points[points.Length - 1].x * 31;
            Vector3 Path(float t)
            {
                float f = Mathf.Clamp01(t) * (points.Length - 1); int i = Mathf.Min(points.Length - 2, (int)f); float u = f - i;
                Vector3 a = points[Mathf.Max(0, i - 1)], b = points[i], c = points[i + 1], d = points[Mathf.Min(points.Length - 1, i + 2)];
                return .5f * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (-a + 3 * b - 3 * c + d) * u * u * u);
            }
            for (int r = 0; r <= steps; r++)
            {
                float t = r / (float)steps; Vector3 p = Path(t), tangent = (Path(Mathf.Min(1, t + .002f)) - Path(Mathf.Max(0, t - .002f))).normalized;
                Vector3 x = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 y = Vector3.Cross(tangent, x); float cap = Mathf.Min(1, Mathf.Min(t, 1 - t) * 20);
                for (int s = 0; s <= sides; s++)
                {
                    float angle = s * Mathf.PI * 2 / sides;
                    Add(p + (x * Mathf.Cos(angle) * depth + y * Mathf.Sin(angle) * width) * cap,
                        new Vector2(s / (float)sides, t), new Vector4(vessel ? width * cap : 0, Mathf.Sin(t * Mathf.PI), 0, phase));
                }
            }
            for (int r = 0; r < steps; r++) for (int s = 0; s < sides; s++) { int a = start + r * (sides + 1) + s; Quad(a, a + 1, a + sides + 1, a + sides + 2); }
        }
        static readonly float[] ProfileHeights = { 0, .18f, .34f, .52f, .72f, .88f, .94f, 1 };
        static readonly float[] Widths = { .215f, .225f, .215f, .235f, .27f, .275f, .20f, .065f };
        static readonly float[] FrontDepths = { .115f, .115f, .12f, .145f, .16f, .14f, .09f, .06f };
        static readonly float[] BackDepths = { .12f, .12f, .115f, .13f, .155f, .13f, .095f, .07f };
        static float Profile(float y, float[] values)
        {
            for(int i=0;i<ProfileHeights.Length-1;i++) if(y<=ProfileHeights[i+1])
                return Mathf.Lerp(values[i],values[i+1],Mathf.SmoothStep(0,1,Mathf.InverseLerp(ProfileHeights[i],ProfileHeights[i+1],y)));
            return values[values.Length-1];
        }
        public static float Width(float y) => Profile(y,Widths);
        static Vector3 Section(float y) => new Vector3(Width(y),Profile(y,FrontDepths),Profile(y,BackDepths));
        static Vector3 MeshSection(float y)
        {
            float row = Mathf.Clamp01(y) * 40; int i = Mathf.Min(39, Mathf.FloorToInt(row));
            return Vector3.Lerp(Section(i / 40f), Section((i + 1) / 40f), row - i);
        }
        public void FitInsideSkin(float clearance)
        {
            // Follow the tapered chest/waist cross sections, with space for skin and vessel motion.
            Vector3 Map(Vector3 raw)
            {
                float y=.06f+.88f*raw.y; var section=MeshSection(y);
                float horizontal=.94f*(section.x-clearance)/.335f;
                float depth=raw.z>=-.012f ? .90f*(section.y-clearance)/.21f : .87f*(section.z-clearance)/.24f;
                return new Vector3(raw.x*horizontal,y,-.012f+(raw.z+.012f)*depth);
            }
            var original=v.ToArray(); float maximum=1;
            for(int i=0;i<v.Count;i++)
            {
                var p=Map(original[i]); v[i]=p; var section=MeshSection(p.y);
                float w=section.x-clearance,d=(p.z>=-.012f?section.y:section.z)-clearance;
                float radius=Mathf.Sqrt(p.x*p.x/(w*w)+Mathf.Pow((p.z+.012f)/d,2))/Mathf.Cos(Mathf.PI/48);
                maximum=Mathf.Max(maximum,radius);
            }
            float inset=.99f/maximum;
            Vector3 InsetVector(Vector3 value)=>new Vector3(value.x*inset,value.y,value.z*inset);
            for(int i=0;i<v.Count;i++)
            {
                var p=v[i];p.x*=inset;p.z=-.012f+(p.z+.012f)*inset;v[i]=p;
                var flow=motion[i];flow.x*=.5f*inset;motion[i]=flow;
            }
            foreach(int key in new List<int>(plateNormals.Keys))
            {
                Vector3 n=plateNormals[key],p=original[key]; const float e=.0005f;
                Vector3 tx=new Vector3(1,0,-n.x/n.z),ty=new Vector3(0,1,-n.y/n.z);
                Vector3 dx=InsetVector(Map(p+e*tx)-Map(p-e*tx)),dy=InsetVector(Map(p+e*ty)-Map(p-e*ty));
                plateNormals[key]=(Vector3.Cross(dx/(2*e),dy/(2*e))*Mathf.Sign(n.z)).normalized;
            }
        }
        public void AnteriorVessels()
        {
            // Vessels occupy the inner chest, behind the bowed anterior armour.
            for(int i=0;i<v.Count;i++){var p=v[i];p.z=p.z*.35f-.02f;v[i]=p;}
        }
        public void Skin()
        {
            const int rings = 40, sides = 48;
            for (int r = 0; r <= rings; r++) for (int s = 0; s <= sides; s++)
            {
                float y = r / (float)rings, a = s * Mathf.PI * 2 / sides;
                var section = Section(y); float depth = Mathf.Sin(a) < 0 ? section.z : section.y;
                Add(new Vector3(Mathf.Cos(a) * section.x, y, Mathf.Sin(a) * depth - .012f), new Vector2(s / (float)sides, y), Vector4.zero);
            }
            for (int r = 0; r < rings; r++) for (int s = 0; s < sides; s++) { int a = r * (sides + 1) + s; Quad(a, a + sides + 1, a + 1, a + sides + 2); }
            // Short hip cuffs overlap the thigh skin rather than ending at an open waist.
            foreach(float side in new[]{-1f,1f})
            {
                int start=v.Count;const int rows=6;
                for(int r=0;r<=rows;r++)for(int s=0;s<=sides;s++)
                {
                    float t=r/(float)rows,a=s*Mathf.PI*2/sides;
                    float radius=Mathf.Lerp(.078f,.058f,t);
                    Add(new Vector3(side*.14f+Mathf.Cos(a)*radius,Mathf.Lerp(.07f,-.055f,t),
                        Mathf.Sin(a)*radius*.9f),new Vector2(s/(float)sides,t),Vector4.zero);
                }
                for(int r=0;r<rows;r++)for(int s=0;s<sides;s++)
                {int a=start+r*(sides+1)+s;Quad(a,a+sides+1,a+1,a+sides+2);}
            }

        }
        public void Octahedron(Vector3 p, float size)
        {
            int n = v.Count; var flow = new Vector4(p.x, p.y, p.z, p.y * 39 + p.x * 53);
            foreach (var d in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, Vector3.left, Vector3.back }) Add(p + d * size, Vector2.zero, flow);
            for (int i = 0; i < 4; i++) { int a = n + 2 + i, b = n + 2 + (i + 1) % 4; triangles.Add(n); triangles.Add(b); triangles.Add(a); triangles.Add(n + 1); triangles.Add(a); triangles.Add(b); }
        }
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, motion); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
            var normals=mesh.normals;foreach(var pair in plateNormals)normals[pair.Key]=pair.Value;mesh.normals=normals;
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
