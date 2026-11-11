using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Thick perforated membranes, round crowns, and crossed interior drapes.</summary>
public static class SilkForestGeometry
{
    public sealed class MeshBuffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> indices = new List<int>();
        private readonly List<Color> colours = new List<Color>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly Dictionary<int, Vector3> surfaceNormals = new Dictionary<int, Vector3>();
        public int Vertex(Vector3 p, Color colour, Vector2 tex, Vector3? normal = null)
        {
            int i = vertices.Count; vertices.Add(p); colours.Add(colour); uv.Add(tex);
            if (normal.HasValue) surfaceNormals[i] = normal.Value;
            return i;
        }
        public void Normal(int vertex, Vector3 normal) { surfaceNormals[vertex] = normal; }
        public void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3? sphericalCentre = null)
        {
            int k = Vertex(a, colour, Vector2.zero);
            Vertex(b, colour, Vector2.right); Vertex(c, colour, Vector2.one); Vertex(d, colour, Vector2.up);
            Triangle(k, k + 1, k + 2); Triangle(k, k + 2, k + 3);
            if (sphericalCentre.HasValue)
                for (int j = k; j < k + 4; j++) surfaceNormals[j] = (vertices[j] - sphericalCentre.Value).normalized;
        }
        public void Tube(Vector3[] path, float radius, int sides, Color colour, bool taper = false)
        {
            if (path.Length < 2 || radius <= 0) return;
            int start = vertices.Count;
            Vector3 previousNormal = Vector3.zero;
            for (int p = 0; p < path.Length; p++)
            {
                Vector3 tangent = (path[Mathf.Min(p + 1, path.Length - 1)] - path[Mathf.Max(0, p - 1)]).normalized;
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.up;
                Vector3 normal = p == 0 ? Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up).normalized : Vector3.ProjectOnPlane(previousNormal, tangent).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.Cross(tangent, Vector3.forward).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
                previousNormal = normal;
                float thickness = radius * (taper ? Mathf.Lerp(1, 0.08f, p / (float)(path.Length - 1)) : 1);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * 2 * Mathf.PI / sides;
                    Vertex(path[p] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * thickness, colour, new Vector2(s / (float)sides, p / (float)(path.Length - 1)));
                    if (p == 0) continue;
                    int v = start + p * sides + s, next = start + p * sides + (s + 1) % sides;
                    Triangle(v - sides, next - sides, next); Triangle(v - sides, next, v);
                }
            }
        }
        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            if (surfaceNormals.Count > 0)
            {
                var normals = mesh.normals;
                foreach (var pair in surfaceNormals) normals[pair.Key] = pair.Value;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds(); return mesh;
        }
    }

    private static float R(System.Random random, float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());

    public static float Opening(float sample,float openness)
    {
        float amount=Mathf.Clamp01(openness);
        if(amount<=0)return 0;
        return Mathf.Lerp(0.12f,0.97f,Mathf.Pow(Mathf.Clamp01(sample),Mathf.Lerp(4,0.08f,amount)))*Mathf.Lerp(0.4f,1,Mathf.Sqrt(amount));
    }

    public static void WebBridge(MeshBuffer mesh,Vector3 start,Vector3 end,float width,float sag,System.Random random,bool junctionEnd=false)
    {
        SilkBundle(mesh,t=>Vector3.Lerp(start,end,t)-Vector3.up*Mathf.Sin(t*Mathf.PI)*sag,
            width*0.16f,width,junctionEnd?0.001f:width*0.7f,0.008f,0.6f,random,0.96f);
    }

    // Spatial, lenticular ribbons: longitudinal strands peel apart and merge
    // again, leaving stretched gaps rather than punching pores into a box.
    public static void SilkBundle(MeshBuffer mesh, Func<float,Vector3> spine, float waist,
        float rootWidth,float tipWidth,float thickness,float tornAmount,System.Random random,float openness=0.94f,
        float irregularity=0.85f,float twistDegrees=65,float pairedHoleChance=0.72f,float triangleHoleChance=0.32f,float rootOpenness=0.9f,
        int strandCount=9,bool looseEnd=false,bool openBase=false)
    {
        int count=Mathf.Clamp(strandCount,5,18);
        float length=Vector3.Distance(spine(0),spine(1));
        int steps=Mathf.Clamp(Mathf.CeilToInt(length*14),28,180);
        var paths=new Func<float,Vector3>[count];
        var widths=new float[count];
        Vector3 axis=(spine(1)-spine(0)).normalized;
        Vector3 x=Vector3.Cross(axis,Mathf.Abs(axis.y)>0.9f?Vector3.forward:Vector3.up).normalized;
        Vector3 z=Vector3.Cross(axis,x).normalized;
        float phase=R(random,0,6.28f),twist=twistDegrees*Mathf.Deg2Rad;
        for(int i=0;i<count;i++)
        {
            float angle=phase+i*Mathf.PI*2/count+R(random,-0.16f,0.16f);
            float offset=R(random,0.62f,1.15f),drift=R(random,-0.6f,0.6f),wave=R(random,0,6.28f);
            float frequency=R(random,1.1f,2.1f),bulge=R(random,0.18f,0.8f);
            widths[i]=R(random,0.045f,0.10f)*waist;
            paths[i]=t=>
            {
                float w=waist+(rootWidth-waist)*Mathf.Pow(1-t,4)+(tipWidth-waist)*Mathf.Pow(t,5);
                float a=angle+twist*(t-0.5f)*0.45f+drift*Mathf.Sin(t*3.1f)+
                    irregularity*0.8f*Mathf.Sin(t*length*1.8f+wave);
                float separation=Mathf.Lerp(0.06f,1,Mathf.Clamp01(openness));
                float breathe=Mathf.Lerp(0.6f,0.28f+bulge*Mathf.Pow(0.5f+0.5f*Mathf.Sin(t*frequency*6.28f+wave),2),pairedHoleChance);
                breathe*=1+rootOpenness*0.4f*Mathf.Pow(1-t,5);
                float gather=Mathf.Lerp(0.07f,1,Mathf.SmoothStep(0,1,t/0.14f));
                float merge=0.16f+0.84f*Mathf.Pow(Mathf.Abs(Mathf.Sin(t*length*1.8f+0.7f*Mathf.Sin(t*length*0.8f))),1.3f);
                if(openBase)
                {
                    float baseBlend=1-Mathf.SmoothStep(0,1,t/0.22f);
                    gather=Mathf.Lerp(gather,1,baseBlend);
                    merge=Mathf.Lerp(merge,1,baseBlend);
                    breathe=Mathf.Lerp(breathe,0.85f,baseBlend);
                }
                if(looseEnd)
                {
                    float fan=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.35f,1,t));
                    merge=Mathf.Lerp(merge,1.5f,fan);
                    breathe=Mathf.Lerp(breathe,1,fan);
                }
                return spine(t)+(x*Mathf.Cos(a)+z*Mathf.Sin(a)*0.8f)*w*offset*separation*breathe*gather*merge;
            };
            float ribbonWidth=widths[i];
            // Low openness widens the ribbons until the bundle becomes dense.
            Ribbon(mesh,paths[i],t=>ribbonWidth*Mathf.Lerp(4.5f,1,openness)*
                (0.65f+0.35f*Mathf.Sin(t*5+wave)*Mathf.Sin(t*5+wave))*
                Mathf.Lerp(1,Mathf.Max(0.06f,tipWidth/Mathf.Max(waist,0.001f)),Mathf.Pow(t,4)),
                Mathf.Min(thickness*0.28f,ribbonWidth*0.3f),angle,steps);
        }
        // Long tapered lamellae join neighbouring fibres. Their offset intervals
        // prevent ladder rungs, repeated oval cells, and planar silhouettes.
        int bonds=Mathf.Max(3,Mathf.CeilToInt(length*2.8f));
        for(int b=0;b<bonds;b++)
        {
            int a=random.Next(count),c=(a+1+((R(random,0,1)<triangleHoleChance)?1:0))%count;
            float start=R(random,0.05f,looseEnd?0.4f:0.8f),end=Mathf.Min(looseEnd?0.65f:0.98f,start+R(random,0.12f,0.32f));
            float w=waist*R(random,0.04f,0.12f)*Mathf.Lerp(3,1,openness);
            Ribbon(mesh,u=>Vector3.Lerp(paths[a](Mathf.Lerp(start,end,u)),paths[c](Mathf.Lerp(start,end,u)),Mathf.SmoothStep(0,1,u)),
                u=>w*(0.16f+Mathf.Sin(u*Mathf.PI)*0.84f),Mathf.Min(thickness*0.2f,w*0.25f),phase+b,24);
        }
    }

    private static void Ribbon(MeshBuffer mesh,Func<float,Vector3> path,Func<float,float> width,float depth,float roll,int steps)
    {
        const int sides=8;
        int first=mesh.vertices.Count;
        Vector3 previous=Vector3.zero;
        for(int j=0;j<=steps;j++)
        {
            float t=j/(float)steps;
            Vector3 tangent=(path(Mathf.Min(1,t+0.002f))-path(Mathf.Max(0,t-0.002f))).normalized;
            Vector3 n=j==0?Vector3.Cross(tangent,Mathf.Abs(tangent.y)>0.9f?Vector3.forward:Vector3.up).normalized:Vector3.ProjectOnPlane(previous,tangent).normalized;
            if(n.sqrMagnitude<0.5f)n=Vector3.Cross(tangent,Vector3.right).normalized;
            previous=n;
            Vector3 k=Vector3.Cross(tangent,n).normalized;
            float turn=roll+0.25f*Mathf.Sin(t*5+roll);
            Vector3 broad=n*Mathf.Cos(turn)+k*Mathf.Sin(turn),thin=Vector3.Cross(tangent,broad).normalized;
            float w=Mathf.Max(0.0005f,width(t)),d=Mathf.Max(0.0003f,Mathf.Min(depth,w*0.3f));
            for(int s=0;s<sides;s++)
            {
                float theta=s*2*Mathf.PI/sides;
                Vector3 normal=(broad*Mathf.Cos(theta)*d+thin*Mathf.Sin(theta)*w).normalized;
                mesh.Vertex(path(t)+broad*Mathf.Cos(theta)*w+thin*Mathf.Sin(theta)*d,Color.white,new Vector2(s/(float)sides,t),normal);
                if(j>0)
                {
                    int v=first+j*sides+s,next=first+j*sides+(s+1)%sides;
                    mesh.Triangle(v-sides,next-sides,next);mesh.Triangle(v-sides,next,v);
                }
            }
        }
        // Small planar ends, rather than spherical junction primitives.
        int bottom=mesh.Vertex(path(0),Color.white,Vector2.zero,-(path(0.002f)-path(0)).normalized);
        int top=mesh.Vertex(path(1),Color.white,Vector2.one,(path(1)-path(0.998f)).normalized);
        for(int s=0;s<sides;s++)
        {
            mesh.Triangle(bottom,first+(s+1)%sides,first+s);
            mesh.Triangle(top,first+steps*sides+s,first+steps*sides+(s+1)%sides);
        }
    }

    public static void Junction(MeshBuffer mesh,Vector3 centre,float radius)
    {
        const int rings=6,sides=12;
        Vector3 Surface(int ring,int side)
        {
            float p=ring*Mathf.PI/rings,a=side*Mathf.PI*2/sides;
            return centre+new Vector3(Mathf.Sin(p)*Mathf.Cos(a),Mathf.Cos(p),Mathf.Sin(p)*Mathf.Sin(a))*radius;
        }
        for(int r=0;r<rings;r++)for(int s=0;s<sides;s++)
            mesh.Quad(Surface(r,s),Surface(r+1,s),Surface(r+1,s+1),Surface(r,s+1),Color.white,centre);
    }

    public static Vector3 CrownPoint(Vector3 centre, float radius, Vector3 direction, float asymmetry)
    {
        Vector3 n = direction.normalized;
        float variation = Mathf.Clamp01(asymmetry) * 0.12f * Mathf.Sin(n.x * 3.4f + n.z) * Mathf.Sin(n.y * 2.7f - n.z);
        return centre + n * radius * (1 + variation);
    }

    public static Vector2 HoleOutline(float angle,Vector2 opening,float rotation,float triangularity,Vector2 metric)
    {
        if(opening.sqrMagnitude<0.0001f)return Vector2.zero;
        Vector2 ray=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
        Vector2 physical=Vector2.Scale(ray,metric);
        float c=Mathf.Cos(rotation),t=Mathf.Sin(rotation);
        Vector2 local=new Vector2(physical.x*c+physical.y*t,-physical.x*t+physical.y*c);
        float major=Mathf.Max(0.00001f,opening.x*metric.x),minor=Mathf.Max(0.00001f,opening.y*metric.y);
        float radius=1/Mathf.Sqrt(local.x*local.x/(major*major)+local.y*local.y/(minor*minor));
        float shapeAngle=Mathf.Atan2(local.y/minor,local.x/major);
        float sector=Mathf.Repeat(shapeAngle+Mathf.PI/3,Mathf.PI*2/3)-Mathf.PI/3;
        radius*=Mathf.Lerp(1,0.5f/Mathf.Cos(sector),Mathf.Clamp01(triangularity));
        float bx=Mathf.Sqrt(major*major*c*c+minor*minor*t*t)/metric.x;
        float by=Mathf.Sqrt(major*major*t*t+minor*minor*c*c)/metric.y;
        float fit=Mathf.Min(1,0.97f/Mathf.Max(0.001f,Mathf.Max(bx,by)));
        return ray*(radius*fit);
    }

    // A thick, continuous sheet surrounding an irregular elliptical hole. The
    // front/back skins and the pore wall are geometry, not a two-sided billboard.
    public static void Pore(MeshBuffer mesh, Func<Vector2, Vector3> surface,
        Func<Vector2, Vector3> normal, Vector2 minimum, Vector2 maximum,
        Vector2 opening, float thickness, Color colour, System.Random random, float lipHeight = 0, float holeRotation = 0, float triangularity = 0, Vector2? cellMetric = null)
    {
        const int segments = 24;
        if(opening.sqrMagnitude<0.0001f)lipHeight=0;
        float[] radial=lipHeight>0?new[]{0f,0.08f,0.2f,0.5f,1f}:new[]{0f,0.5f,1f};
        int bands=radial.Length-1;
        Vector2 middle = (minimum + maximum) * 0.5f, extent = (maximum - minimum) * 0.5f;
        Vector2 offset = new Vector2(R(random,-0.11f,0.11f), R(random,-0.1f,0.1f));
        float phase = R(random,0,6.28f);
        int[,,] ids = new int[2,bands + 1,segments + 1];
        for (int side = 0; side < 2; side++)
        for (int band = 0; band <= bands; band++)
        for (int k = 0; k <= segments; k++)
        {
            float angle = (k % segments) * Mathf.PI * 2 / segments;
            Vector2 circle = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            Vector2 border = circle / Mathf.Max(Mathf.Abs(circle.x),Mathf.Abs(circle.y));
            float wobble = 1 + 0.09f * Mathf.Sin(angle * 3 + phase);
            Vector2 outline=HoleOutline(angle,opening,holeRotation,triangularity,cellMetric??Vector2.one);
            Vector2 hole = opening.sqrMagnitude < 0.0001f ? Vector2.zero : outline * wobble + offset;
            hole=new Vector2(Mathf.Clamp(hole.x,-0.975f,0.975f),Mathf.Clamp(hole.y,-0.975f,0.975f));
            Vector2 uv = middle + Vector2.Scale(Vector2.Lerp(hole,border,radial[band]),extent);
            Vector3 n = normal(uv).normalized;
            float rim=lipHeight*(band==0?0.55f:band==1?1f:band==2?0.38f:0);
            Color tint=Color.Lerp(colour,Color.white,lipHeight>0&&band<3?0.12f:0);
            ids[side,band,k] = mesh.Vertex(surface(uv) + n * (thickness*0.5f+rim)*(side==0?1:-1),tint,uv,side==0?n:-n);
        }
        void Tri(int a,int b,int c, Vector3 desired)
        {
            if (Vector3.Dot(Vector3.Cross(mesh.vertices[b]-mesh.vertices[a],mesh.vertices[c]-mesh.vertices[a]),desired)<0)
                mesh.Triangle(a,c,b);
            else mesh.Triangle(a,b,c);
        }
        if(lipHeight>0)
        for(int side=0;side<2;side++)for(int band=0;band<=bands;band++)for(int k=0;k<=segments;k++)
        {
            int at=k%segments;
            Vector3 tangent=mesh.vertices[ids[side,band,(at+1)%segments]]-mesh.vertices[ids[side,band,(at+segments-1)%segments]];
            Vector3 across=mesh.vertices[ids[side,Mathf.Min(bands,band+1),at]]-mesh.vertices[ids[side,Mathf.Max(0,band-1),at]];
            Vector3 n=Vector3.Cross(tangent.normalized,across.normalized).normalized;
            Vector3 baseline=normal(middle).normalized*(side==0?1:-1);
            if(n.sqrMagnitude<0.1f)n=baseline;
            if(Vector3.Dot(n,baseline)<0)n=-n;
            if(band<bands)mesh.Normal(ids[side,band,k],n);
        }
        for (int side = 0; side < 2; side++)
        for (int band = 0; band < bands; band++)
        for (int k = 0; k < segments; k++)
        {
            Vector3 n = normal(middle) * (side == 0 ? 1 : -1);
            int a=ids[side,band,k],b=ids[side,band+1,k],c=ids[side,band+1,k+1],d=ids[side,band,k+1];
            Tri(a,b,c,n); Tri(a,c,d,n);
        }
        for (int k=0;k<segments;k++)
        {
            int a=ids[0,0,k],b=ids[0,0,k+1],c=ids[1,0,k+1],d=ids[1,0,k];
            mesh.Triangle(a,b,c);mesh.Triangle(a,c,d);
        }
    }

    public sealed class ColumnProfile
    {
        private readonly Vector2[] lower=new Vector2[4],upper=new Vector2[4];
        private readonly float amount,phase,twist;
        public ColumnProfile(System.Random random,float irregularity,float twistDegrees)
        {
            amount=Mathf.Clamp01(irregularity);phase=R(random,-Mathf.PI,Mathf.PI)*amount;
            twist=twistDegrees*Mathf.Deg2Rad*R(random,-1,1)*amount;
            Vector2[] regular={new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
            float shear=R(random,-0.45f,0.45f)*amount,trap=R(random,-0.28f,0.28f)*amount;
            for(int i=0;i<4;i++)
            {
                Vector2 q=regular[i];
                lower[i]=new Vector2(q.x*(1+trap*q.y)+shear*q.y,q.y)*R(random,1-amount*0.18f,1+amount*0.18f);
                upper[i]=new Vector2(q.x*(1-trap*q.y)-shear*q.y,q.y)*R(random,1-amount*0.18f,1+amount*0.18f);
            }
        }
        public Vector2 Corner(int corner,float t)
        {
            Vector2 q=Vector2.Lerp(lower[corner%4],upper[corner%4],Mathf.SmoothStep(0,1,t));
            q*=1+0.09f*amount*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(t*5+corner*1.7f+phase);
            q.x+=q.y*amount*0.18f*Mathf.Sin(t*4+phase);
            float angle=phase+twist*Mathf.SmoothStep(0,1,t);
            q=new Vector2(q.x*Mathf.Cos(angle)-q.y*Mathf.Sin(angle),q.x*Mathf.Sin(angle)+q.y*Mathf.Cos(angle));
            return q+new Vector2(Mathf.Sin(t*5+phase),Mathf.Cos(t*4+phase))*Mathf.Sin(t*Mathf.PI)*amount*0.08f;
        }
    }

    public struct RootFork
    {
        public Vector3 start,control,end;
        public bool grounded;
    }

    public static RootFork[] RootLayout(Vector3 foot,Vector3 junction,int count,Vector2 spread,float curlChance,System.Random random)
    {
        count=Mathf.Clamp(count,2,8);
        var roots=new RootFork[count];float phase=R(random,0,Mathf.PI*2);
        for(int i=0;i<count;i++)
        {
            float angle=phase+i*Mathf.PI*2/count+R(random,-0.28f,0.28f);
            bool grounded=i<2||R(random,0,1)>=curlChance;
            float distance=R(random,Mathf.Min(spread.x,spread.y),Mathf.Max(spread.x,spread.y));
            Vector3 toe=foot+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Mathf.Max(0.15f,distance);
            if(!grounded)toe+=Vector3.up*R(random,0.15f,0.45f);
            Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 start=junction+Vector3.up*R(random,0.05f,0.85f)+radial*R(random,0.035f,0.085f);
            Vector3 control=new Vector3(start.x,Mathf.Lerp(foot.y,start.y,0.2f),start.z)+radial*distance*0.15f;
            roots[i]=new RootFork {start=start,end=toe,control=control,grounded=grounded};
        }
        return roots;
    }

    public static Vector3 ForkPoint(RootFork root,float t)
    {
        return (1-t)*(1-t)*root.start+2*(1-t)*t*root.control+t*t*root.end;
    }

    public static Vector3 ReturningStrandPoint(Vector3 start,Vector3 end,Vector3 outward,float reach,float t)
    {
        return Vector3.Lerp(start,end,t)+outward.normalized*reach*Mathf.Pow(Mathf.Max(0,Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI)),1.5f);
    }

    public static void ForkedRoots(MeshBuffer mesh,RootFork[] roots,float radius,float thickness,float openness,
        float irregularity,float twist,float paired,float triangles,System.Random random)
    {
        // Individual fibres continue downward from staggered trunk positions.
        // There is no root-level bundle envelope, common vertex, or gathering node.
        foreach(var root in roots)
        {
            Vector3 radial=Vector3.ProjectOnPlane(root.end-root.start,Vector3.up).normalized;
            Vector3 side=Vector3.Cross(radial,Vector3.up).normalized;
            for(int i=0;i<7;i++)
            {
                float spread=(i/6f-0.5f)*radius*R(random,2.5f,4);
                Vector3 start=root.start+side*(i/6f-0.5f)*radius*0.55f+Vector3.up*R(random,0,0.45f);
                Vector3 end=root.end+side*spread+radial*R(random,-0.1f,0.18f);
                Vector3 control=root.control+side*spread*0.25f;
                var fibre=new RootFork {start=start,control=control,end=end};
                float width=radius*R(random,0.025f,0.055f)*Mathf.Lerp(3,1,openness);
                Ribbon(mesh,t=>ForkPoint(fibre,t),t=>width*Mathf.Lerp(1,0.12f,Mathf.Pow(t,3)),
                    Mathf.Min(thickness*0.2f,width*0.25f),R(random,0,6.28f),40);
            }
        }
    }

    public struct StemPoreCell
    {
        public Vector2 minimum,maximum;
        public bool attachment;
    }

    public static List<StemPoreCell> StemPoreLayout(int rows,System.Random random,float pairedChance)
    {
        rows=Mathf.Clamp(rows,3,90);
        var cuts=new float[rows+1];float total=0;
        for(int i=1;i<=rows;i++){total+=R(random,0.42f,1.65f);cuts[i]=total;}
        for(int i=1;i<=rows;i++)cuts[i]/=total;
        var cells=new List<StemPoreCell>();
        for(int row=0;row<rows;row++)
        {
            bool pair=R(random,0,1)<Mathf.Clamp01(pairedChance)&&row>0&&row<rows-1;
            float split=R(random,0.31f,0.69f);
            cells.Add(new StemPoreCell { minimum=new Vector2(0,cuts[row]),maximum=new Vector2(pair?split:1,cuts[row+1]),attachment=row==rows-1 });
            if(pair)cells.Add(new StemPoreCell { minimum=new Vector2(split,cuts[row]),maximum=new Vector2(1,cuts[row+1]),attachment=false });
        }
        return cells;
    }

    public static void WebColumn(MeshBuffer mesh, Func<float,Vector3> spine, float waist,
        float rootWidth, float tipWidth, float thickness, float tornAmount, System.Random random, float openness = 0.85f, float irregularity = 0.85f, float twistDegrees = 65, float pairedHoleChance = 0.72f, float triangleHoleChance = 0.32f, float rootOpenness = 0.9f)
    {
        Vector3 direction=(spine(1)-spine(0)).normalized;
        Vector3 x=Vector3.Cross(direction,Mathf.Abs(direction.y)>0.9f?Vector3.forward:Vector3.up).normalized;
        Vector3 z=Vector3.Cross(x,direction).normalized;
        var profile=new ColumnProfile(random,irregularity,twistDegrees);
        float length=Vector3.Distance(spine(0),spine(1));
        int rows=Mathf.Clamp(Mathf.CeilToInt(length/Mathf.Clamp(waist*2.2f,0.2f,0.55f)),5,90);
        for(int face=0;face<4;face++)
        {
            int edge=face;
            Vector3 Surface(Vector2 uv)
            {
                float t=uv.y;
                float width=waist+(rootWidth-waist)*Mathf.Pow(1-t,7)+(tipWidth-waist)*Mathf.Pow(t,6);
                Vector2 cross=Vector2.Lerp(profile.Corner(edge,t),profile.Corner((edge+1)%4,t),uv.x);
                cross*=0.87f+0.13f*Mathf.Pow(uv.x*2-1,2);
                // The centre and width vary continuously; all four faces share edges.
                float swell=1+0.055f*Mathf.Sin(t*17)*Mathf.Sin(uv.x*Mathf.PI);
                return spine(t)+(x*cross.x+z*cross.y)*width*swell;
            }
            Vector3 Normal(Vector2 uv)
            {
                const float step=0.003f;
                Vector3 across=Surface(new Vector2(Mathf.Min(1,uv.x+step),uv.y))-Surface(new Vector2(Mathf.Max(0,uv.x-step),uv.y));
                Vector3 along=Surface(new Vector2(uv.x,Mathf.Min(1,uv.y+step)))-Surface(new Vector2(uv.x,Mathf.Max(0,uv.y-step)));
                return Vector3.Cross(along.normalized,across.normalized).normalized;
            }
            foreach(var cell in StemPoreLayout(rows,random,pairedHoleChance))
            {
                float sample=R(random,0,1);
                float root=Mathf.Pow(1-(cell.minimum.y+cell.maximum.y)*0.5f,7)*Mathf.Clamp01(rootOpenness)*Mathf.Clamp01(openness*2);
                float size=Opening(sample,Mathf.Lerp(openness,1,root));
                if(cell.attachment||sample<Mathf.Lerp(0.25f,0,Mathf.Clamp01(openness)))size=0;
                Vector2 minimum=cell.minimum,maximum=cell.maximum,span=maximum-minimum,middle=(minimum+maximum)*0.5f;
                float width=Vector3.Distance(Surface(new Vector2(minimum.x,middle.y)),Surface(new Vector2(maximum.x,middle.y)));
                float height=Vector3.Distance(Surface(new Vector2(middle.x,minimum.y)),Surface(new Vector2(middle.x,maximum.y)));
                float kind=R(random,0,1);
                float rotation=kind<0.34f?R(random,-0.22f,0.22f):kind<0.68f?Mathf.PI*0.5f+R(random,-0.25f,0.25f):R(random,0.45f,1.1f)*(R(random,0,1)<0.5f?-1:1);
                float aspect=R(random,1.4f,2.8f);
                float c=Mathf.Cos(rotation),sn=Mathf.Sin(rotation);
                float major=Mathf.Min(width/Mathf.Sqrt(c*c+sn*sn/(aspect*aspect)),height/Mathf.Sqrt(sn*sn+c*c/(aspect*aspect)));
                float holeX=size*major/Mathf.Max(0.001f,width);
                float holeY=size*major/aspect/Mathf.Max(0.001f,height);
                float triangularity=R(random,0,1)<triangleHoleChance?R(random,0.8f,1):0;
                // Wide root sheets need broad apertures rather than a solid skirt.
                // Their perimeter remains continuous so the membrane still meets ground.
                if(root>0.12f)
                {
                    float blend=Mathf.Clamp01(root*1.8f);
                    holeX=Mathf.Lerp(holeX,0.97f,blend);
                    holeY=Mathf.Lerp(holeY,0.96f,blend);
                    rotation*=1-blend;
                }
                float warpX=R(random,-0.12f,0.12f),warpY=R(random,-0.18f,0.18f);
                Vector2 Warp(Vector2 uv)
                {
                    Vector2 local=(uv-minimum)/span;
                    float envelope=Mathf.Sin(local.x*Mathf.PI)*Mathf.Sin(local.y*Mathf.PI);
                    return uv+new Vector2(warpX*span.x,warpY*span.y)*envelope;
                }
                Pore(mesh,uv=>Surface(Warp(uv)),uv=>Normal(Warp(uv)),minimum,maximum,
                    new Vector2(holeX,Mathf.Min(0.97f,holeY)),thickness,new Color(0.87f,0.94f,0.91f),random,0,rotation,triangularity,new Vector2(Mathf.Max(0.001f,width),Mathf.Max(0.001f,height)));
            }
        }
    }

    public static List<Vector3> Crown(MeshBuffer silk, MeshBuffer film, Vector3 centre, float radius, float asymmetry,
        float webThickness, float tornAmount, float coverage, System.Random random,
        Vector3 attachmentDirection, int interiorSheets = 3, float openness = 0.8f, float rimHeight = 0.018f)
    {
        // Spherical coordinates make a round shell. Unequal pore sizes are sampled
        // over staggered bands, leaving broad continuous stretches of opaque tissue.
        int rows=6,columns=10;
        var sockets=new List<Vector3>();
        float rotation=R(random,0,Mathf.PI*2);
        Vector3 Direction(Vector2 uv)
        {
            float theta=uv.x*Mathf.PI*2+rotation,phi=uv.y*Mathf.PI;
            return new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),-Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta));
        }
        Vector3 Surface(Vector2 uv)=>CrownPoint(centre,radius,Direction(uv),asymmetry);
        for(int row=0;row<rows;row++)
        for(int col=0;col<columns;col++)
        {
            Vector2 min=new Vector2((col+(row%2)*0.5f)/columns,row/(float)rows);
            Vector2 max=min+new Vector2(1f/columns,1f/rows);
            Vector3 axis=Direction((min+max)*0.5f);
            float choice=R(random,0,1);
            float size=Opening(choice,openness);
            // Keep the lower contact cap solid: the stalk fans into tissue here.
            if(Vector3.Dot(axis,attachmentDirection.normalized)>0.78f||choice<Mathf.Lerp(0.25f,0,Mathf.Clamp01(openness)))size=0;
            // A tiny pore behaves as a filled patch while keeping topology regular.
            size=Mathf.Max(0,size);
            Pore(silk,Surface,uv=>Direction(uv),min,max,new Vector2(size*R(random,0.86f,1),size),
                radius*0.009f,new Color(0.86f,0.94f,0.9f),random,radius*Mathf.Max(0,rimHeight));
            if(row>0&&row<rows-1)sockets.Add(Surface(min));
        }
        // Crossed ragged curtains have both edges on the shell and hang through
        // its interior. Their holes expose layered tissue rather than an empty ball.
        for(int sheet=0;sheet<Mathf.Clamp(interiorSheets,0,8);sheet++)
        {
            float angle=rotation+sheet*Mathf.PI/Mathf.Max(1,interiorSheets);
            Vector3 side=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 normal=Vector3.Cross(side,Vector3.up).normalized;
            Vector3 Draped(Vector2 uv)
            {
                float y=Mathf.Lerp(-0.82f,0.82f,uv.y);
                float span=Mathf.Sqrt(1-y*y);
                Vector3 left=CrownPoint(centre,radius,new Vector3(-side.x*span,y,-side.z*span),asymmetry);
                Vector3 right=CrownPoint(centre,radius,new Vector3(side.x*span,y,side.z*span),asymmetry);
                return Vector3.Lerp(left,right,uv.x)+normal*radius*0.16f*Mathf.Sin(uv.x*Mathf.PI)*Mathf.Sin(uv.y*Mathf.PI*2+sheet);
            }
            for(int row=0;row<5;row++)
            for(int col=0;col<5;col++)
            {
                float opening=Opening(R(random,0,1),Mathf.Clamp01(openness*1.08f));
                Pore(silk,Draped,uv=>normal,new Vector2(col/5f,row/5f),new Vector2((col+1)/5f,(row+1)/5f),
                    new Vector2(opening*R(random,0.65f,1),opening),radius*0.004f,
                    new Color(0.65f,0.75f,0.8f),random);
            }
        }
        return sockets;
    }
}
