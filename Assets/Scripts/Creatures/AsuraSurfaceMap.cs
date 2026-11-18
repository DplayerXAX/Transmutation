using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Baked walkable surface graph. Stacked floors are distinct vertices, never a flattened circle.</summary>
[CreateAssetMenu(menuName="Creatures/Asura Surface Map")]
public sealed class AsuraSurfaceMap : ScriptableObject
{
    [Serializable] public sealed class Surface
    {
        public Vector3 position;
        public float area;
        public bool vine;
        public int[] neighbours;
    }
    public Bounds bounds;
    public float spacing=6;
    public Surface[] surfaces=Array.Empty<Surface>();
    public int Nearest(Vector3 point,float maxDistance=12)
    {
        int best=-1;float distance=maxDistance*maxDistance;
        for(int i=0;i<surfaces.Length;i++)
        {float d=(surfaces[i].position-point).sqrMagnitude;if(d<distance){distance=d;best=i;}}
        return best;
    }
    public Dictionary<int,float> Distances(int start,float limit)
    {
        var distances=new Dictionary<int,float>();
        if(start<0 || start>=surfaces.Length)return distances;
        var queue=new SortedSet<(float distance,int index)>();distances[start]=0;queue.Add((0,start));
        while(queue.Count>0)
        {
            var item=queue.Min;queue.Remove(item);
            if(distances[item.index]<item.distance)continue;
            foreach(int next in surfaces[item.index].neighbours)
            {
                float d=item.distance+Vector3.Distance(surfaces[item.index].position,surfaces[next].position);
                if(d>limit || (distances.TryGetValue(next,out float old) && old<=d))continue;
                distances[next]=d;queue.Add((d,next));
            }
        }
        return distances;
    }
    public List<int> Path(int start,int goal)
    {
        var result=new List<int>();if(start<0 || goal<0)return result;
        var queue=new SortedSet<(float distance,int index)>();var distances=new Dictionary<int,float>();var parents=new Dictionary<int,int>();
        distances[start]=0;queue.Add((0,start));
        while(queue.Count>0)
        {
            var item=queue.Min;queue.Remove(item);if(item.index==goal)break;
            if(distances[item.index]<item.distance)continue;
            foreach(int next in surfaces[item.index].neighbours)
            {
                float d=item.distance+Vector3.Distance(surfaces[item.index].position,surfaces[next].position);
                if(distances.TryGetValue(next,out float old) && old<=d)continue;
                distances[next]=d;parents[next]=item.index;queue.Add((d,next));
            }
        }
        if(!distances.ContainsKey(goal))return result;
        for(int i=goal;;i=parents[i]){result.Add(i);if(i==start)break;}
        result.Reverse();return result;
    }
}
