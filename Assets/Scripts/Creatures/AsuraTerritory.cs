using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-local, bounded population and surface-connected territory. Combat stays with each fighter.</summary>
public sealed partial class AsuraTerritory : MonoBehaviour
{
    public AsuraSurfaceMap surfaceMap;
    public AsuraLinkage linkagePrefab;
    public Creature[] fighterPrefabs;
    public Material tendrilMaterial;
    [Header("Territory awareness")]
    [Tooltip("Enemy detection inside connected friendly territory. Outside it, normal creature range applies.")]
    [Min(0)] public float territoryDetectionRange=60;
    [Header("Surface territory")]
    [Min(1)] public float influenceRadius=65;
    [Min(1)] public float minimumLinkDistance=50;
    [Min(1)] public float maximumLinkDistance=100;
    [Min(1)] public int maximumNodesPerHome=16;
    [Min(1)] public float supportRadius=26;
    [Min(1)] public float pressureRadius=36;
    [Min(1)] public int minimumSupport=3;
    [Min(0.5f)] public float formationSeconds=6;
    [Min(1)] public float collapseSeconds=12;
    [Min(1)] public float disconnectionGrace=10;
    [Header("Spawning")]
    [Min(1)] public float minimumSpacing=3.2f;
    [Min(1)] public float enemyClearance=7;
    [Min(1)] public float densityRadius=11;
    [Min(1)] public int localPopulationLimit=5;
    [Range(0,1)] public float quietSpawnChance=0.2f;
    [Range(0,1)] public float expansionSpawnChance=0.35f;
    [Tooltip("Share of spawn attempts that prioritize safe vine surfaces. Falls back to other terrain if none fit.")]
    [Range(0,1)] public float vineSpawnChance=0.35f;
    [Min(0)] public int initialPopulation=24;
    public LayerMask groundLayers=1<<7;
    public bool Ready {get; private set;}
    public int Formed {get; private set;}
    public int Collapsed {get; private set;}
    public int FailedSpawns {get; private set;}
    public int ElevatedSpawns {get; private set;}
    public int VineSpawns {get; private set;}
    public IReadOnlyList<AsuraLinkage> Nodes=>nodes;
    public IReadOnlyList<AsuraHome> Homes=>homes;
    private readonly List<AsuraLinkage> nodes=new List<AsuraLinkage>();
    private readonly List<AsuraHome> homes=new List<AsuraHome>();
    private readonly List<Creature> fighters=new List<Creature>();
    private readonly List<Creature> snapshot=new List<Creature>();
    private readonly List<Vector3> positions=new List<Vector3>();
    private readonly List<AsuraTerritoryMember> members=new List<AsuraTerritoryMember>();
    private readonly Dictionary<AsuraLinkage,AsuraLinkage> parents=new Dictionary<AsuraLinkage,AsuraLinkage>();
    private readonly Dictionary<AsuraLinkage,LineRenderer> tendrils=new Dictionary<AsuraLinkage,LineRenderer>();
    private readonly Dictionary<Creature,int> fighterSurfaces=new Dictionary<Creature,int>();
    private readonly Collider[] overlaps=new Collider[32];
    private float nextTick;
    private readonly Dictionary<AsuraHome,int> expansionSites=new Dictionary<AsuraHome,int>();
    private readonly Dictionary<AsuraHome,float> nextSiteSearch=new Dictionary<AsuraHome,float>();
    private readonly Dictionary<AsuraHome,float> siteExpires=new Dictionary<AsuraHome,float>();

    public static bool IsFighter(Creature c)=>c is MeleeAsura || c is RangedAsura || c is GeneralAsura || c is RollingAsura;
    private void Update()
    {
        if(!Ready)
        {
            if(surfaceMap==null || surfaceMap.surfaces.Length==0 || linkagePrefab==null || fighterPrefabs==null || fighterPrefabs.Length==0)return;
            if(FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None).Any(w=>w.gameObject.scene==gameObject.scene && !w.GenerationComplete))return;
            Initialize();
        }
        if(Time.time<nextTick)return;nextTick=Time.time+0.5f;Step(0.5f);
    }
    private void Initialize()
    {
        homes.AddRange(GetComponentsInChildren<AsuraHome>());nodes.AddRange(GetComponentsInChildren<AsuraLinkage>());
        members.AddRange(GetComponentsInChildren<AsuraTerritoryMember>(true));
        foreach(var node in nodes)Prepare(node);
        InitializeSites();
        RefreshFighters();RefreshConnections();Ready=true;
        // Alternate sides to avoid giving the first colony all contested spawn positions.
        if(members.Count==0)for(int i=0;i<initialPopulation;i++)foreach(var home in homes){TrySpawn(home,true);RefreshFighters();}
    }
    private void Prepare(AsuraLinkage node)
    {
        if(node.Permanent)node.home=(AsuraHome)node;
        node.surfaceIndex=surfaceMap.Nearest(node.transform.position);
        node.distances=surfaceMap.Distances(node.surfaceIndex,Mathf.Max(maximumLinkDistance,influenceRadius));node.patch.Clear();
        node.vinePatch.Clear();
        foreach(var entry in node.distances)if(entry.Value<=influenceRadius)
        {node.patch.Add(entry.Key);if(surfaceMap.surfaces[entry.Key].vine)node.vinePatch.Add(entry.Key);}
        if(node.home!=null)node.SetResolvedPolarity(node.home.Polarity);
    }
    private void RefreshFighters()
    {
        CreatureRegistry.SnapshotCombat(gameObject.scene,snapshot,positions);fighters.Clear();fighterSurfaces.Clear();
        foreach(var c in snapshot)if(IsFighter(c) && c.SimulationEnabled && !c.IsCarried && !c.IsBubbleCaptured)
        {fighters.Add(c);fighterSurfaces[c]=surfaceMap.Nearest(c.transform.position,9);}
        members.RemoveAll(m=>m==null);
        foreach(var home in homes)home.Living=members.Count(m=>m.home==home && m.Creature!=null && !m.Creature.IsDead);
    }
    public void Step(float dt)
    {
        RefreshFighters();nodes.RemoveAll(n=>n==null);RefreshConnections();
        foreach(var node in nodes.ToArray())
        {
            if(node.home==null || !node.home.SimulationEnabled || !node.SimulationEnabled)continue;
            if(node.Permanent)continue;
            CountSupport(node,out int friendly,out int enemy);
            if(node.state==AsuraLinkState.Forming)
            {
                bool linked=nodes.Any(p=>p!=node && p.home==node.home && p.Connected && CanLink(p,node));
                CountSupport(node,out friendly,out enemy,supportRadius);
                if(linked && friendly>=minimumSupport && friendly>enemy)node.Formation+=dt;
                else node.Formation=Mathf.Max(0,node.Formation-dt*2);
                node.DisconnectedTime+=dt;
                if(node.Formation>=formationSeconds){node.state=AsuraLinkState.Active;node.DisconnectedTime=0;Formed++;}
                else if(node.DisconnectedTime>formationSeconds*4)RemoveNode(node);
                continue;
            }
            if(!node.Connected)
            {
                node.state=AsuraLinkState.Disconnected;node.DisconnectedTime+=dt;
                if(node.DisconnectedTime>=disconnectionGrace)RemoveNode(node);
                continue;
            }
            node.DisconnectedTime=0;node.state=AsuraLinkState.Active;
            node.Pressure=(1-node.Health/node.MaxHealth)*12;
        }
        // Collapse disconnects descendants before this tick's spawning.
        RefreshConnections();
        if(fixedCaptureSites){UpdateCaptures(dt);UpdateMarchOrders();}
        foreach(var home in homes)
        {
            if(!home.SimulationEnabled)continue;
            home.budget=Mathf.Min(2,home.budget+home.spawnsPerSecond*dt);
            for(int attempt=0;attempt<2 && home.budget>=1 && home.Living<home.populationCap;attempt++)
            {if(TrySpawn(home,false))home.budget-=1;else {home.budget=Mathf.Min(home.budget,1);break;}}
            home.expansionClock+=dt;
            if(home.expansionClock>=4){home.expansionClock=0;if(!fixedCaptureSites)TryExpand(home);}
        }
    }
    public bool CanLink(AsuraLinkage a,AsuraLinkage b)=>a.distances!=null && a.distances.TryGetValue(b.surfaceIndex,out float d) && d<=Mathf.Min(maximumLinkDistance,2*influenceRadius);
    public void RefreshConnections()
    {
        foreach(var member in members)if(member!=null)member.TerritoryDetectionRange=0;
        foreach(var node in nodes)node.Connected=false;
        foreach(var home in homes)
        {
            if(home==null || !home.SimulationEnabled)continue;
            var queue=new Queue<AsuraLinkage>();home.Connected=true;queue.Enqueue(home);
            while(queue.Count>0)
            {
                var parent=queue.Dequeue();
                foreach(var node in nodes)
                {
                    if(node.home!=home || node.Connected || !node.SimulationEnabled || node.state==AsuraLinkState.Forming || node.state==AsuraLinkState.Collapsing || !CanLink(parent,node))continue;
                    node.Connected=true;queue.Enqueue(node);DrawTendril(node,parent);
                }
            }
            var union=new HashSet<int>();foreach(var node in nodes)if(node.home==home && node.Connected)foreach(int i in node.patch)union.Add(i);
            home.TerritoryArea=0;foreach(int i in union)home.TerritoryArea+=surfaceMap.surfaces[i].area;
            foreach(var member in members)
                if(member!=null && member.home==home && member.Creature!=null && member.Creature.Polarity==home.Polarity &&
                    fighterSurfaces.TryGetValue(member.Creature,out int surface) && union.Contains(surface))
                    member.TerritoryDetectionRange=territoryDetectionRange;
        }
        foreach(var pair in tendrils)if(pair.Value!=null)pair.Value.enabled=pair.Key!=null && pair.Key.Connected;
    }
    private void DrawTendril(AsuraLinkage node,AsuraLinkage parent)
    {
        if(parents.TryGetValue(node,out var old) && old==parent)return;
        parents[node]=parent;
        if(!tendrils.TryGetValue(node,out var line))
        {
            var go=new GameObject("Home tendril");go.transform.SetParent(node.transform,false);line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=tendrilMaterial;line.widthMultiplier=0.3f;line.numCornerVertices=3;line.useWorldSpace=true;tendrils[node]=line;
        }
        var path=surfaceMap.Path(parent.surfaceIndex,node.surfaceIndex);
        line.positionCount=path.Count;line.SetPositions(path.Select(i=>surfaceMap.surfaces[i].position+Vector3.up*0.2f).ToArray());
        Color color=AsuraBloodPalette.For(node.home.Polarity);line.startColor=color;line.endColor=color;
    }
    private void CountSupport(AsuraLinkage node,out int friendly,out int enemy,float radius=-1)
    {
        friendly=enemy=0;
        foreach(var fighter in fighters)
        {
            if(!fighterSurfaces.TryGetValue(fighter,out int i) || !node.distances.TryGetValue(i,out float d) || d>(radius<0?pressureRadius:radius))continue;
            if(fighter.Polarity==node.home.Polarity)friendly++;else if(fighter.Polarity!=CreaturePolarity.None)enemy++;
        }
    }
    public bool TryExpand(AsuraHome home)
    {
        if(fixedCaptureSites)return false;
        if(nodes.Count(n=>n.home==home)>=maximumNodesPerHome || nodes.Any(n=>n.home==home && n.state==AsuraLinkState.Forming))return false;
        var owned=CoveredSurfaces(home);
        int best=-1;float bestScore=float.NegativeInfinity;
        foreach(var fighter in fighters)
        {
            if(fighter.Polarity!=home.Polarity || !fighterSurfaces.TryGetValue(fighter,out int index) || index<0)continue;
            // Sample near a real group; growth never projects into unsupported empty space.
            foreach(int candidate in new[]{index}.Concat(surfaceMap.surfaces[index].neighbours))
            {
                Vector3 p=surfaceMap.surfaces[candidate].position;
                if(!surfaceMap.bounds.Contains(p) || nodes.Any(n=>Vector3.Distance(n.transform.position,p)<minimumLinkDistance))continue;
                var parent=nodes.FirstOrDefault(n=>n.home==home && n.Connected && n.distances.TryGetValue(candidate,out float d) && d>=minimumLinkDistance && d<=Mathf.Min(maximumLinkDistance,2*influenceRadius));
                if(parent==null)continue;
                var distances=surfaceMap.Distances(candidate,Mathf.Max(supportRadius,influenceRadius));int friend=0,enemy=0;
                foreach(var f in fighters)if(fighterSurfaces.TryGetValue(f,out int s) && distances.TryGetValue(s,out float distance) && distance<=supportRadius){if(f.Polarity==home.Polarity)friend++;else if(f.Polarity!=CreaturePolarity.None)enemy++;}
                if(friend<minimumSupport || friend<=enemy || !Clear(p,1.8f,4))continue;
                float newArea=0;foreach(var entry in distances)if(entry.Value<=influenceRadius && !owned.Contains(entry.Key))newArea+=surfaceMap.surfaces[entry.Key].area;
                float score=friend*6-enemy*8+newArea/180-EnemyDistance(home,p)*0.01f;
                if(score>bestScore){best=candidate;bestScore=score;}
            }
        }
        if(best<0)return false;
        var node=Instantiate(linkagePrefab,surfaceMap.surfaces[best].position+Vector3.up*0.1f,Quaternion.identity,transform);
        node.name="Forming "+home.Polarity+" linkage";node.home=home;node.surfaceIndex=best;node.state=AsuraLinkState.Forming;
        Prepare(node);nodes.Add(node);return true;
    }
    private float EnemyDistance(AsuraHome home,Vector3 point)
    {
        float best=10000;
        foreach(var n in nodes)if(n.home!=home && n.Connected)best=Mathf.Min(best,Vector3.Distance(point,n.transform.position));
        return best;
    }
    private HashSet<int> CoveredSurfaces(AsuraHome home)
    {
        var covered=new HashSet<int>();foreach(var n in nodes)if(n.home==home && n.Connected)foreach(int i in n.patch)covered.Add(i);return covered;
    }
    private int ExpansionSite(AsuraHome home)
    {
        if(fixedCaptureSites)return -1;
        if(nodes.Count(n=>n.home==home)>=maximumNodesPerHome)return -1;
        if(expansionSites.TryGetValue(home,out int existing) && siteExpires.TryGetValue(home,out float expiry) && Time.time<expiry && !nodes.Any(n=>Vector3.Distance(n.transform.position,surfaceMap.surfaces[existing].position)<minimumLinkDistance))return existing;
        if(nextSiteSearch.TryGetValue(home,out float next) && Time.time<next)return -1;
        nextSiteSearch[home]=Time.time+8;expansionSites.Remove(home);
        var owned=CoveredSurfaces(home);int best=-1;float score=600;
        foreach(var parent in nodes.Where(n=>n.home==home && n.Connected && n.patch.Count>0))
        for(int attempt=0;attempt<8;attempt++)
        {
            int candidate=parent.patch[Random.Range(0,parent.patch.Count)];var p=surfaceMap.surfaces[candidate].position;
            if(parent.distances[candidate]<minimumLinkDistance || nodes.Any(n=>Vector3.Distance(n.transform.position,p)<minimumLinkDistance) || !Clear(p,1.8f,4))continue;
            if(fighters.Any(f=>f.Polarity!=home.Polarity && f.Polarity!=CreaturePolarity.None && Vector3.Distance(f.transform.position,p)<supportRadius))continue;
            float gain=0;foreach(var entry in surfaceMap.Distances(candidate,influenceRadius))if(!owned.Contains(entry.Key))gain+=surfaceMap.surfaces[entry.Key].area;
            if(gain>score){score=gain;best=candidate;}
        }
        if(best>=0){expansionSites[home]=best;siteExpires[home]=Time.time+60;}return best;
    }
    public bool TrySpawn(AsuraHome home,bool initial)
    {
        if(home.Living>=home.populationCap || !home.SimulationEnabled)return false;
        var prefab=ChooseFighter();if(prefab==null)return false;
        float bodyRadius=prefab is GeneralAsura?1.45f:0.9f;
        float bodyHeight=prefab is GeneralAsura?3.3f:2.6f;
        var available=nodes.Where(n=>n.home==home && n.Connected && n.patch.Count>0).ToArray();if(available.Length==0)return false;
        bool quiet=Random.value<(initial?0.65f:quietSpawnChance);
        bool preferVines=Random.value<vineSpawnChance;
        var vineNodes=preferVines?available.Where(n=>n.vinePatch.Count>0).ToArray():System.Array.Empty<AsuraLinkage>();
        int expansionSite=!initial && Random.value<expansionSpawnChance?ExpansionSite(home):-1;
        float bestScore=float.NegativeInfinity;int best=-1;
        for(int attempt=0;attempt<70;attempt++)
        {
            // Give the first half of the search to vines, then keep a safe ground fallback.
            bool vineSearch=preferVines && vineNodes.Length>0 && attempt<35;
            var candidates=vineSearch?vineNodes:available;
            var node=candidates[Random.Range(0,candidates.Length)];if(Random.value>node.SpawnWeight)continue;
            var patch=vineSearch?node.vinePatch:node.patch;
            int index=patch[Random.Range(0,patch.Count)];Vector3 p=surfaceMap.surfaces[index].position;
            if(!surfaceMap.bounds.Contains(p) || nodes.Any(n=>(n.transform.position-p).sqrMagnitude<25))continue;
            int density=0;bool safe=true;float nearestEnemy=float.PositiveInfinity;
            foreach(var f in fighters)
            {
                float d=Vector3.Distance(f.transform.position,p);
                if(f.Polarity!=home.Polarity && f.Polarity!=CreaturePolarity.None)nearestEnemy=Mathf.Min(nearestEnemy,d);
                if(d<minimumSpacing || (f.Polarity!=home.Polarity && d<enemyClearance)){safe=false;break;}
                if(f.Polarity==home.Polarity && d<densityRadius)density++;
            }
            if(!safe || density>=localPopulationLimit || !Clear(p,bodyRadius,bodyHeight))continue;
            // Recheck ground at runtime: the bake does not authorize spawning on removed/sculpted surfaces.
            if(!Physics.Raycast(p+Vector3.up*0.6f,Vector3.down,out var hit,1.2f,groundLayers,QueryTriggerInteraction.Ignore) || hit.normal.y<0.7f)continue;
            // A broad engagement band, not a race to the one closest opposing beacon.
            float frontCost=float.IsPositiveInfinity(nearestEnemy)?Mathf.Max(0,EnemyDistance(home,p)-influenceRadius)*0.12f:Mathf.Abs(nearestEnemy-18)*0.18f;
            float score=-density*12+(expansionSite>=0?-Vector3.Distance(p,surfaceMap.surfaces[expansionSite].position)*1.5f:quiet?Random.value*15:-frontCost)+Random.value*10;
            bool preferred=preferVines && surfaceMap.surfaces[index].vine;
            bool bestPreferred=preferVines && best>=0 && surfaceMap.surfaces[best].vine;
            if(best<0 || (preferred && !bestPreferred) || (preferred==bestPreferred && score>bestScore))
            {bestScore=score;best=index;}
        }
        if(best<0){FailedSpawns++;return false;}
        Vector3 position=surfaceMap.surfaces[best].position+Vector3.up*0.15f;
        var fighter=Instantiate(prefab,position,Quaternion.Euler(0,Random.Range(0,360),0),transform);
        fighter.SetResolvedPolarity(home.Polarity);fighter.name=home.Polarity+" colony "+prefab.name;
        if(fighter.TryGetComponent<CreatureCombat>(out var combat))combat.AsuraOpponentsOnly=true;
        var member=fighter.gameObject.AddComponent<AsuraTerritoryMember>();member.home=home;members.Add(member);fighters.Add(fighter);
        home.Living++;home.Spawned++;
        if(surfaceMap.surfaces[best].vine)VineSpawns++;
        if(position.y>surfaceMap.bounds.min.y+60)ElevatedSpawns++;
        var pulse=new GameObject("Asura arrival pulse");pulse.transform.SetParent(transform);pulse.transform.position=position;
        var ring=pulse.AddComponent<LineRenderer>();ring.sharedMaterial=tendrilMaterial;ring.widthMultiplier=0.12f;ring.loop=true;ring.positionCount=24;
        ring.startColor=ring.endColor=AsuraBloodPalette.For(home.Polarity);
        for(int i=0;i<24;i++){float a=i*Mathf.PI*2/24;ring.SetPosition(i,position+new Vector3(Mathf.Cos(a),0.1f,Mathf.Sin(a))*1.4f);}
        Destroy(pulse,0.8f);return true;
    }
    private bool Clear(Vector3 floor,float radius,float height)
    {
        int count=Physics.OverlapCapsuleNonAlloc(floor+Vector3.up*(radius+0.2f),floor+Vector3.up*(height-radius+0.2f),radius,overlaps,~0,QueryTriggerInteraction.Ignore);
        return count==0;
    }
    public Creature ChooseFighter()
    {
        float Weight(Creature c)=>c is MeleeAsura?62:c is RangedAsura?23:c is RollingAsura?10:c is GeneralAsura?5:0;
        float total=0;foreach(var p in fighterPrefabs)if(p!=null)total+=Weight(p);
        float roll=Random.value*total;
        foreach(var p in fighterPrefabs)if(p!=null){roll-=Weight(p);if(roll<0)return p;}
        return fighterPrefabs.FirstOrDefault(p=>p!=null);
    }
    public void LinkageDied(AsuraLinkage node)
    {
        if(node.Permanent || !nodes.Contains(node))return;
        if(node.DeathCause==CreatureDeathCause.Damage && node.LastAttacker!=null && IsFighter(node.LastAttacker) && node.LastAttacker.Polarity!=node.Polarity)
        {
            RefreshFighters();var area=surfaceMap.Distances(node.surfaceIndex,65);
            foreach(var f in fighters)if(f.Polarity==node.LastAttacker.Polarity && fighterSurfaces.TryGetValue(f,out int i) && area.ContainsKey(i))
            {var buff=f.GetComponent<AsuraBreachMomentum>();if(buff==null)buff=f.gameObject.AddComponent<AsuraBreachMomentum>();buff.Refresh();}
        }
        RemoveNode(node);RefreshConnections();
    }
    public void RemoveNode(AsuraLinkage node)
    {
        if(node==null || node.Permanent)return;
        NeutralizeSite(node);
        node.Connected=false;node.state=AsuraLinkState.Collapsing;nodes.Remove(node);parents.Remove(node);tendrils.Remove(node);Collapsed++;
        node.Die();node.gameObject.SetActive(false);Destroy(node.gameObject);
    }
    private void OnDrawGizmosSelected()
    {
        if(surfaceMap==null)return;Gizmos.color=Color.cyan;Gizmos.DrawWireCube(surfaceMap.bounds.center,surfaceMap.bounds.size);
        foreach(var node in nodes)if(node!=null && node.Connected)
        {Gizmos.color=AsuraBloodPalette.For(node.home.Polarity);foreach(int i in node.patch)Gizmos.DrawSphere(surfaceMap.surfaces[i].position+Vector3.up*0.3f,0.35f);}
    }
}

