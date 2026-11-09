using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-local emitter budgets, planting grounds and the rising fire-bubble rift.</summary>
public sealed class LeafMapEcology : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;
    [SerializeField] private SquirrelSystemManager squirrels;
    [SerializeField] private FireBubble riftBubblePrefab;
    [SerializeField] private List<BubbleFlower> flowers = new List<BubbleFlower>();
    [SerializeField, Min(1)] private int maxFlowers = 36;
    [SerializeField, Min(1)] private int ambientBubbleBudget = 32;
    [SerializeField, Min(1)] private int riftBubbleBudget = 12;
    [SerializeField, Min(1f)] private float emissionInterval = 4.5f;
    [Tooltip("Bubble flowers further than this from the player stay quiet (metres).")]
    [SerializeField, Min(10f)] private float emissionRange = 65f;
    [SerializeField] private List<FireBubble> riftBubbles = new List<FireBubble>();
    private readonly HashSet<FireBubble> claimedBubbles = new HashSet<FireBubble>();
    private float nextScan, nextFire;
    private Transform player;
    private bool ready;
    private bool childrenPlaced;
    public bool HasFlowerCapacity { get { flowers.RemoveAll(f => f == null); return flowers.Count < maxFlowers; } }
    public int FlowerCount { get { flowers.RemoveAll(f=>f==null);return flowers.Count; } }
    public int RisingFireCount => riftBubbles.Count;

    public bool CanPlant(Vector3 p)
    {
        Vector2 at=new Vector2(p.x,p.z),design=LeafMapTerrain.ToDesign(at);
        Vector3 spire=LeafMapTerrain.SpireSpot,mother=LeafMapTerrain.MotherSpot;
        return p.y > -1f && LeafMapTerrain.Footprint(p.x,p.z)>8f
            && design.y>-102f && design.y<119f && LeafMapTerrain.RouteDistance(at)>2.1f
            && Vector2.Distance(at,new Vector2(spire.x,spire.z))>25f*LeafMapTerrain.Scale
            && Vector2.Distance(at,new Vector2(mother.x,mother.z))>16f;
    }
    public void RegisterFlower(BubbleFlower flower)
    {
        if(flower==null || flowers.Contains(flower))return;
        // New plants join the same sparse emitter pattern; every flower remains usable while carried.
        flower.ConfigureGroundEmission(flowers.Count%4==0,new Vector2(12,22));
        flowers.Add(flower);
    }
    public void RegisterRiftBubble(FireBubble bubble) { if(bubble!=null && !riftBubbles.Contains(bubble))riftBubbles.Add(bubble); }

    private void Update()
    {
        if(!childrenPlaced && world!=null && world.GenerationComplete)
        {
            var mother=GetComponentInChildren<BoneKiln>();
            if(mother!=null && mother.Root!=null)
            {
                var children=mother.Root.GetComponentsInChildren<BoneChild>();
                if(children.Length>=3)
                {
                    for(int i=0;i<3;i++)children[i].SetRestingHome(LeafMapTerrain.ChildRest(i),mother.transform.position);
                    childrenPlaced=true;
                }
            }
        }
        if(!ready)
        {
            if(world==null || !world.GenerationComplete)return;
            player=FindFirstObjectByType<PlayerCreatureCarrier>()?.transform;
            if(squirrels!=null) { squirrels.enabled=true; squirrels.InitializeSystem(); }
            ready=true; nextFire=Time.time;
        }
        if(Time.time>=nextScan)
        {
            nextScan=Time.time+1f;
            int bubbles=0;
            foreach(var bubble in FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None))
                if(bubble.gameObject.scene==gameObject.scene && !bubble.IsLeashed && bubble.Occupant==null)bubbles++;
            flowers.RemoveAll(f=>f==null);
            foreach(var flower in flowers)
                if(!flower.IsDead)
                {
                    flower.SetSimulationEnabled(true);
                    flower.AmbientEmissionAllowed=bubbles<ambientBubbleBudget &&
                        (player==null || (flower.transform.position-player.position).sqrMagnitude<emissionRange*emissionRange);
                }
            for(int i=riftBubbles.Count-1;i>=0;i--)
            {
                var bubble=riftBubbles[i];
                if(bubble==null) { riftBubbles.RemoveAt(i); continue; }
                if(bubble.IsCarried || bubble.IsLeashed || bubble.IsBoundToBone)claimedBubbles.Add(bubble);
                if(claimedBubbles.Contains(bubble)) { riftBubbles.RemoveAt(i); continue; }
                // Recycle only unclaimed vent bubbles beyond the playable skyline, never by lifetime.
                if(bubble.transform.position.y>82f) { Destroy(bubble.gameObject); riftBubbles.RemoveAt(i); }
            }
        }
        if(Time.time<nextFire || riftBubblePrefab==null || riftBubbles.Count>=riftBubbleBudget)return;
        nextFire=Time.time+emissionInterval;
        Vector3 at;
        if(LeafMapTerrain.HasRift)
        {
            float t=Random.Range(0.2f,0.72f);
            at=LeafMapTerrain.RiftPoint(t);
            // The rift may open into the inner world: start just above whatever floor is under it.
            at.y=LeafMapTerrain.RiftFloor(t)+5f;
            float floor=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(new Vector3(at.x,40f,at.z),Vector3.down,140f,1<<7))
                if(world.OwnsCollider(hit.collider) && hit.point.y<0f)floor=Mathf.Max(floor,hit.point.y);
            if(!float.IsNegativeInfinity(floor))at.y=Mathf.Max(at.y,floor+5f);
        }
        else
        {
            // Without the rift the vent is a small hollow east of the mother; bubbles rise from just above it.
            at=LeafMapTerrain.VentSpot+new Vector3(Random.Range(-2f,2f),3f,Random.Range(-2f,2f));
            if(Physics.Raycast(at+Vector3.up*30f,Vector3.down,out RaycastHit hit,60f,1<<7))at.y=Mathf.Max(at.y,hit.point.y+3f);
        }
        var fire=Instantiate(riftBubblePrefab,at,Quaternion.identity,transform);
        fire.name="Rift fire bubble";
        riftBubbles.Add(fire);
    }

    private void OnDrawGizmosSelected()
    {
        Draw(LeafMapTerrain.MainRoute,new Color(0.3f,1f,0.5f));
        Draw(LeafMapTerrain.WestRoute,new Color(0.4f,0.7f,1f));
        Draw(LeafMapTerrain.EastRoute,new Color(1f,0.4f,0.2f));
        Draw(LeafMapTerrain.NorthRoute,new Color(0.9f,0.7f,1f));
        void Draw(Vector3[] route,Color color)
        {
            Gizmos.color=color;
            for(int i=1;i<route.Length;i++)Gizmos.DrawLine(route[i-1]+Vector3.up,route[i]+Vector3.up);
        }
    }
}
