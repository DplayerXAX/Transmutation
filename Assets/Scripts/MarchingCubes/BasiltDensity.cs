using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Rolling ground and six sculptable roots; individual basalt meshes and independent canopy arches.</summary>
public sealed class BasiltDensity
{
    public readonly struct Column
    {
        public readonly Vector2 centre;
        public readonly float radius, top;
        public Column(Vector2 p,float r,float h){centre=p;radius=r;top=h;}
    }
    public readonly struct Vine
    {
        public readonly Vector3[] keys;
        public readonly float radius;
        public Vine(Vector3[] points,float thickness){keys=points;radius=thickness;}
    }
    private readonly struct Segment
    {
        public readonly Vector3 a,b;
        public readonly float radius;
        public Segment(Vector3 p,Vector3 q,float r){a=p;b=q;radius=r;}
    }
    public static readonly float[] RoadRadii={8,9,8.5f,10,7,8};
    public static readonly Vector3[][] Roads=BuildRoads();
    public static readonly Vine[] Canopy=BuildCanopy();
    public readonly Column[] Columns;
    public static bool UseMeshColumn(Column column)=>true;
    private readonly Segment[] segments;
    private readonly bool vinesOnly;
    private readonly Dictionary<Vector2Int,List<int>> bins=new Dictionary<Vector2Int,List<int>>();
    private readonly ConcurrentDictionary<Vector2Int,int[]> candidates=new ConcurrentDictionary<Vector2Int,int[]>();
    private readonly ConcurrentDictionary<Vector2Int,float> heights=new ConcurrentDictionary<Vector2Int,float>();

    private static Vector3 Buried(float x,float z,float radius)=>new Vector3(x,GroundHeight(x,z)-radius*0.8f,z);
    // Deliberate traversable routes; density is concentrated in two forest pockets.
    private static Vector3[][] BuildRoads()=>new[]{
        new[]{Buried(-80,-205,8),new Vector3(-100,12,-160),new Vector3(-100,24,-115),new Vector3(-75,28,-80),new Vector3(-45,28,-62)},
        new[]{Buried(-170,-30,9),new Vector3(-145,28,12),new Vector3(-110,62,46),new Vector3(-67,96,69),new Vector3(-40,85,95),Buried(-10,173,9)},
        new[]{new Vector3(3,28,-12),new Vector3(39,22,-25),new Vector3(69,19,-44),new Vector3(105,17,-69),Buried(145,-105,8.5f)},
        new[]{Buried(-204,135,10),new Vector3(-166,51,112),new Vector3(-131,75,99),new Vector3(-96,59,133),Buried(-65,208,10)},
        new[]{Buried(191,28,7),new Vector3(158,28,42),new Vector3(137,48,82),new Vector3(141,32,131),Buried(180,193,7)},
        new[]{Buried(26,198,8),new Vector3(62,43,197),new Vector3(104,67,191),new Vector3(150,47,211),Buried(202,238,8)}
    };
    private static Vine[] BuildCanopy()
    {
        var result=new List<Vine>();
        void Arch(float ax,float az,float cx,float cz,float bx,float bz,float peak,float radius)
        {
            result.Add(new Vine(new[]{Buried(ax,az,radius),new Vector3(Mathf.Lerp(ax,cx,0.55f),peak*0.78f,Mathf.Lerp(az,cz,0.55f)),new Vector3(cx,peak,cz),new Vector3(Mathf.Lerp(cx,bx,0.45f),peak*0.76f,Mathf.Lerp(cz,bz,0.45f)),Buried(bx,bz,radius)},radius));
        }
        Arch(-210,-100,-170,-15,-155,104,52,9);
        Arch(-200,10,-147,52,-113,165,76,11);
        Arch(-216,162,-173,130,-115,187,58,8);
        Arch(-178,-125,-143,-65,-172,24,39,6);
        Arch(-210,205,-153,187,-77,230,64,9);
        Arch(-125,-9,-142,69,-191,151,91,10);
        Arch(135,-16,176,55,207,149,62,10);
        Arch(195,-65,163,3,124,41,40,7);
        Arch(126,158,164,174,218,209,72,12);
        Arch(30,180,73,222,143,249,81,10);
        Arch(162,57,196,113,211,219,91,9);
        Arch(32,154,40,187,98,216,51,7);
        Arch(125,-155,158,-108,197,-56,32,7);
        Arch(70,-177,118,-147,167,-169,21,5);
        return result.ToArray();
    }
    public BasiltDensity(int seed,bool roots)
    {
        vinesOnly=roots;Columns=BuildColumns(seed);
        var pieces=new List<Segment>();
        if(roots)for(int r=0;r<Roads.Length;r++)
        {var path=SampleRoad(r);for(int i=1;i<path.Length;i++)pieces.Add(new Segment(path[i-1],path[i],RoadRadii[r]));}
        segments=pieces.ToArray();
        for(int i=0;i<segments.Length;i++)
        {
            var s=segments[i];Vector2 a=new Vector2(s.a.x,s.a.z),b=new Vector2(s.b.x,s.b.z);
            Vector2 lo=Vector2.Min(a,b)-Vector2.one*(s.radius+2),hi=Vector2.Max(a,b)+Vector2.one*(s.radius+2);
            for(int z=Mathf.FloorToInt(lo.y/24);z<=Mathf.FloorToInt(hi.y/24);z++)for(int x=Mathf.FloorToInt(lo.x/24);x<=Mathf.FloorToInt(hi.x/24);x++)
            {var k=new Vector2Int(x,z);if(!bins.TryGetValue(k,out var list))bins[k]=list=new List<int>();list.Add(i);}
        }
    }
    private static Column[] BuildColumns(int seed)
    {
        var result=new List<Column>();var random=new System.Random(seed);
        void Patch(float cx,float cz,float rx,float rz,float radius,float top)
        {
            int nx=Mathf.CeilToInt(rx/(radius*1.5f)),nz=Mathf.CeilToInt(rz/(radius*1.732f));
            for(int col=-nx;col<=nx;col++)for(int row=-nz;row<=nz;row++)
            {
                Vector2 p=new Vector2(cx+col*radius*1.5f,cz+(row+(col&1)*0.5f)*radius*1.732f);
                float d=new Vector2((p.x-cx)/rx,(p.y-cz)/rz).magnitude;
                if(d>1 || (d>0.91f && random.NextDouble()<0.18))continue;
                result.Add(new Column(p,radius*1.012f,Mathf.Max(GroundHeight(p.x,p.y)+3,top)));
            }
        }
        Patch(-36,-24,68,61,7,36);
        Patch(-56,35,58,43,9,58);
        Patch(-63,65,39,32,9,81);
        Patch(-61,79,22,20,10,104);
        Patch(-104,-30,28,39,6,27);
        Patch(18,28,24,30,7,29);
        Patch(-130,-151,29,23,6,23);
        Patch(104,-92,35,28,8,26);
        Patch(139,-124,18,22,8,41);
        Patch(-161,154,32,25,9,49);
        Patch(169,205,30,23,8,37);
        Patch(13,218,23,19,6,23);
        Patch(197,82,21,29,7,39);
        Patch(-195,-207,18,20,7,32);
        return result.ToArray();
    }
    public static float GroundHeight(float x,float z)
    {
        float broad=(Mathf.PerlinNoise(x*0.009f+511,z*0.009f+213)-0.45f)*26;
        float detail=(Mathf.PerlinNoise(x*0.027f+61,z*0.027f+47)-0.5f)*4;
        float h=7+broad+detail+4*Mathf.Sin(x*0.018f+z*0.009f);
        h+=16*Mathf.Exp(-Mathf.Pow((x+120)/88,2)-Mathf.Pow((z-30)/125,2));
        h+=12*Mathf.Exp(-Mathf.Pow((x-143)/65,2)-Mathf.Pow((z+65)/125,2));
        float channel=x+24+18*Mathf.Sin(z*0.012f);
        h-=8*Mathf.Exp(-channel*channel/1100)*Mathf.Exp(-z*z/70000);
        float edge=Mathf.Max(Mathf.Abs(x)/240,Mathf.Abs(z)/288);
        h+=Mathf.SmoothStep(0,1,(edge-0.72f)/0.28f)*(17+8*Mathf.Sin(z*0.027f+x*0.018f));
        float fossil=1-Mathf.SmoothStep(0,1,(new Vector2((x-83)/74,(z-108)/95).magnitude-0.62f)/0.5f);
        return Mathf.Lerp(h,1.4f+0.9f*Mathf.Sin(x*0.035f)*Mathf.Cos(z*0.029f),fossil);
    }
    public float Height(float x,float z)=>GroundHeight(x,z);
    public float Sample(Vector3 p)
    {
        var key=new Vector2Int(BitConverter.SingleToInt32Bits(p.x),BitConverter.SingleToInt32Bits(p.z));
        if(!vinesOnly)
        {if(!heights.TryGetValue(key,out float h)){h=GroundHeight(p.x,p.z);heights.TryAdd(key,h);}return h-p.y;}
        if(!candidates.TryGetValue(key,out var list))
        {var bin=new Vector2Int(Mathf.FloorToInt(p.x/24),Mathf.FloorToInt(p.z/24));list=bins.TryGetValue(bin,out var found)?found.ToArray():Array.Empty<int>();candidates.TryAdd(key,list);}
        float d=-1000;
        foreach(int i in list)
        {var s=segments[i];Vector3 ab=s.b-s.a;float t=Mathf.Clamp01(Vector3.Dot(p-s.a,ab)/ab.sqrMagnitude);d=Mathf.Max(d,s.radius-(p-s.a-ab*t).magnitude);}
        return d;
    }
    public static Vector3[] SampleRoad(int road,int steps=12)=>SamplePath(Roads[road],steps);
    public static Vector3[] SamplePath(Vector3[] keys,int steps=12)
    {
        var points=new List<Vector3>();
        for(int i=0;i<keys.Length-1;i++)for(int j=0;j<steps;j++)
        {float t=j/(float)steps;Vector3 a=keys[Mathf.Max(0,i-1)],b=keys[i],c=keys[i+1],d=keys[Mathf.Min(keys.Length-1,i+2)];points.Add(0.5f*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));}
        points.Add(keys[keys.Length-1]);return points.ToArray();
    }
}

