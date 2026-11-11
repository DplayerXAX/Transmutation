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
    private static Vector3[][] BuildRoads()=>new[]{
        new[]{Buried(-24,-239,8),new Vector3(-41,16,-194),new Vector3(-62,30,-141),new Vector3(-74,19,-85),new Vector3(-83,48,-27),new Vector3(-54,34,38),new Vector3(-15,22,87),Buried(12,165,8)},
        new[]{Buried(118,-211,9),new Vector3(85,27,-160),new Vector3(21,53,-113),new Vector3(-36,46,-60),new Vector3(-66,23,0),Buried(-91,85,9)},
        new[]{Buried(-194,-143,8.5f),new Vector3(-143,37,-110),new Vector3(-81,48,-105),new Vector3(-20,26,-70),new Vector3(41,14,-32),Buried(97,2,8.5f)},
        new[]{Buried(-184,163,10),new Vector3(-132,64,133),new Vector3(-89,79,74),new Vector3(-69,49,13),new Vector3(-27,23,-26),Buried(15,-54,10)},
        new[]{Buried(21,216,7),new Vector3(-13,39,176),new Vector3(-51,63,122),new Vector3(-89,79,74),new Vector3(-126,44,22),Buried(-171,-4,7)},
        new[]{Buried(185,-68,8),new Vector3(142,43,-32),new Vector3(95,58,2),new Vector3(47,39,21),new Vector3(14,14,4),Buried(-22,-27,8)}
    };
    private static Vine[] BuildCanopy()
    {
        var result=new List<Vine>();var random=new System.Random(9271);
        for(int i=0;i<32;i++)
        {
            float cx=-170+(i%6)*57+(float)random.NextDouble()*20;
            float cz=-180+(i/6)*70+(float)random.NextDouble()*28;
            if(cx>35 && cz>35 && cz<170)cx-=95;
            float angle=(float)random.NextDouble()*Mathf.PI*2;
            Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            float span=58+(float)random.NextDouble()*70;
            float radius=5.5f+(float)random.NextDouble()*6.5f;
            Vector2 a=new Vector2(cx,cz)-direction*span,b=new Vector2(cx,cz)+direction*span;
            a=new Vector2(Mathf.Clamp(a.x,-218,218),Mathf.Clamp(a.y,-259,259));b=new Vector2(Mathf.Clamp(b.x,-218,218),Mathf.Clamp(b.y,-259,259));
            // Keep the winding valley as an exploration route; arches can roof it, their feet sit beside it.
            float channelA=-24-18*Mathf.Sin(a.y*0.012f),channelB=-24-18*Mathf.Sin(b.y*0.012f);
            if(Mathf.Abs(a.x-channelA)<24)a.x=channelA+(a.x<channelA?-32:32);
            if(Mathf.Abs(b.x-channelB)<24)b.x=channelB+(b.x<channelB?-32:32);
            float peak=32+(float)random.NextDouble()*56+GroundHeight(cx,cz);
            result.Add(new Vine(new[]{Buried(a.x,a.y,radius),new Vector3(Mathf.Lerp(a.x,cx,0.55f),peak*0.8f,Mathf.Lerp(a.y,cz,0.55f)),new Vector3(cx,peak,cz),new Vector3(Mathf.Lerp(cx,b.x,0.55f),peak*0.7f,Mathf.Lerp(cz,b.y,0.55f)),Buried(b.x,b.y,radius)},radius));
        }
        result.Add(new Vine(new[]{Buried(-220,-180,15),new Vector3(-160,67,-135),new Vector3(-85,113,-85),new Vector3(13,95,-45),new Vector3(112,63,-10),Buried(215,12,15)},15));
        result.Add(new Vine(new[]{Buried(-205,188,13),new Vector3(-145,101,143),new Vector3(-50,123,87),new Vector3(38,98,38),Buried(171,-16,13)},13));
        result.Add(new Vine(new[]{Buried(165,-227,12),new Vector3(107,80,-179),new Vector3(20,111,-126),new Vector3(-73,87,-69),Buried(-204,-23,12)},12));
        result.Add(new Vine(new[]{Buried(195,240,14),new Vector3(160,75,197),new Vector3(86,105,179),new Vector3(8,74,198),Buried(-55,260,14)},14));
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
        void Patch(float cx,float cz,float extent,float radius,float top,float step)
        {
            int n=Mathf.CeilToInt(extent/radius);
            for(int row=-n;row<=n;row++)for(int col=-n;col<=n;col++)
            {
                Vector2 p=new Vector2(cx+col*radius*1.72f+(row&1)*radius*0.86f,cz+row*radius*1.49f);
                float d=Vector2.Distance(p,new Vector2(cx,cz))/extent;
                if(d>1 || Mathf.Abs(p.x)>222 || Mathf.Abs(p.y)>264)continue;
                if(d>0.85f && random.NextDouble()<0.3)continue;
                float tier=d<0.45f?2:d<0.73f?1:0;
                result.Add(new Column(p,radius*(0.995f+(float)random.NextDouble()*0.025f),Mathf.Max(GroundHeight(p.x,p.y)+2,top+tier*step)));
            }
        }
        Patch(-128,-82,87,5.2f,12,7);
        Patch(-156,34,80,9.2f,32,11);
        Patch(-132,132,70,12,55,14);
        Patch(137,-103,60,6.5f,24,8);
        Patch(157,-4,66,12,38,8);
        Patch(-47,203,58,7,30,7);
        Patch(159,204,45,9,31,6);
        result.Add(new Column(new Vector2(-167,145),35,100));
        result.Add(new Column(new Vector2(-129,182),26,116));
        result.Add(new Column(new Vector2(159,26),35,62));
        result.Add(new Column(new Vector2(-102,-155),22,33));
        for(int i=0;i<76;i++)
        {
            float angle=i*Mathf.PI*2/76;float x=Mathf.Cos(angle)*212,z=Mathf.Sin(angle)*250;
            if(z<-211 && Mathf.Abs(x)<57)continue;
            result.Add(new Column(new Vector2(x,z),10+(float)random.NextDouble()*14,45+(float)random.NextDouble()*72+18*Mathf.Sin(angle*3)));
        }
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
