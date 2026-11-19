using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class AsuraTerritory
{
    [Header("Fixed battlefield sites")]
    public bool fixedCaptureSites=true;
    [Min(1)] public float captureSeconds=6;
    [Min(1)] public float marchOrderSeconds=30;
    [Serializable] public sealed class BattlefieldSite
    {
        public Vector3 position;
        public int surface;
        public AsuraLinkage occupant;
        public AsuraHome claimant;
        public float progress;
        [NonSerialized] public Dictionary<int,float> support;
        [NonSerialized] public Dictionary<int,float> routes;
        [NonSerialized] public LineRenderer marker;
        [NonSerialized] public int[] approaches;
    }
    [SerializeField] private List<BattlefieldSite> battlefieldSites=new List<BattlefieldSite>();
    public IReadOnlyList<BattlefieldSite> Sites=>battlefieldSites;
    public int Captured {get;private set;}
    private readonly Dictionary<BattlefieldSite,int> assignments=new Dictionary<BattlefieldSite,int>();
    private readonly Dictionary<AsuraHome,List<BattlefieldSite>> frontiers=new Dictionary<AsuraHome,List<BattlefieldSite>>();
    private bool builtRouteThisTick;

    private void InitializeSites()
    {
        battlefieldSites.Clear();
        if(!fixedCaptureSites)return;
        foreach(var node in nodes)if(!node.Permanent && node.surfaceIndex>=0)
        {
            var site=new BattlefieldSite {position=node.transform.position,surface=node.surfaceIndex,occupant=node,
                support=surfaceMap.Distances(node.surfaceIndex,supportRadius)};
            site.approaches=site.support.Where(p=>p.Value>=6 && p.Value<=Mathf.Min(18,supportRadius-2)).Select(p=>p.Key).OrderBy(i=>i).ToArray();
            var go=new GameObject("Neutral territory site");go.transform.SetParent(transform,false);
            site.marker=go.AddComponent<LineRenderer>();site.marker.sharedMaterial=tendrilMaterial;
            site.marker.useWorldSpace=true;site.marker.loop=true;site.marker.widthMultiplier=0.16f;
            site.marker.positionCount=20;site.marker.startColor=site.marker.endColor=Color.gray;
            for(int i=0;i<20;i++){float a=i*Mathf.PI/10;site.marker.SetPosition(i,site.position+new Vector3(Mathf.Cos(a)*2,0.15f,Mathf.Sin(a)*2));}
            site.marker.enabled=false;battlefieldSites.Add(site);
        }
    }
    private void NeutralizeSite(AsuraLinkage node)
    {
        foreach(var site in battlefieldSites)if(site.occupant==node)
        {site.occupant=null;site.claimant=null;site.progress=0;if(site.marker!=null)site.marker.enabled=true;break;}
    }
    private bool CanSupply(AsuraHome home,BattlefieldSite site)=>home!=null && home.SimulationEnabled &&
        nodes.Any(n=>n.home==home && n.Connected && n.distances.TryGetValue(site.surface,out float d) && d<=Mathf.Min(maximumLinkDistance,2*influenceRadius));

    private void UpdateCaptures(float dt)
    {
        foreach(var site in battlefieldSites)
        {
            if(site.occupant!=null)continue;
            AsuraHome claimant=null;
            foreach(var home in homes)
            {
                if(!CanSupply(home,site) || nodes.Count(n=>n.home==home)>=maximumNodesPerHome)continue;
                int friends=0,enemies=0;
                foreach(var f in fighters)if(fighterSurfaces.TryGetValue(f,out int s) && site.support.ContainsKey(s))
                {if(f.Polarity==home.Polarity)friends++;else if(f.Polarity!=CreaturePolarity.None)enemies++;}
                if(friends>=minimumSupport && enemies==0){claimant=home;break;}
            }
            if(claimant==null){site.claimant=null;site.progress=0;}
            else
            {
                if(site.claimant!=claimant){site.claimant=claimant;site.progress=0;}
                site.progress+=dt;
                // Never grow a body into an occupying fighter or a removed terrain surface.
                if(site.progress>=captureSeconds && Clear(site.position,1.1f,3.4f) &&
                    Physics.Raycast(site.position+Vector3.up*0.6f,Vector3.down,1.2f,groundLayers,QueryTriggerInteraction.Ignore))
                {
                    var node=Instantiate(linkagePrefab,site.position,Quaternion.identity,transform);
                    node.name=claimant.Polarity+" captured linkage";node.home=claimant;node.state=AsuraLinkState.Active;
                    Prepare(node);nodes.Add(node);site.occupant=node;site.claimant=null;site.progress=0;
                    site.marker.enabled=false;Captured++;Formed++;RefreshConnections();
                }
            }
            if(site.marker!=null && site.occupant==null)
            {site.marker.enabled=true;site.marker.startColor=site.marker.endColor=site.claimant!=null?AsuraBloodPalette.For(site.claimant.Polarity):Color.gray;}
        }
    }
    private bool IsObjective(AsuraHome home,BattlefieldSite site)=>
        (site.occupant==null || site.occupant.home!=home) && CanSupply(home,site);

    private void UpdateMarchOrders()
    {
        builtRouteThisTick=false;assignments.Clear();
        // Compute supply eligibility once per home, not once per fighter/site pair.
        foreach(var home in homes)
        {
            if(!frontiers.TryGetValue(home,out var list)){list=new List<BattlefieldSite>();frontiers[home]=list;}
            list.Clear();foreach(var site in battlefieldSites)if(IsObjective(home,site))list.Add(site);
        }
        foreach(var m in members)if(m!=null && m.Creature!=null && !m.Creature.IsDead && m.Objective!=null)
        {assignments.TryGetValue(m.Objective,out int n);assignments[m.Objective]=n+1;}
        foreach(var m in members)
        {
            if(m==null)continue;m.MarchValidUntil=0;m.Territory=this;
            var c=m.Creature;
            if(c==null || c.IsDead || !c.SimulationEnabled || c.IsCarried || c.IsBubbleCaptured ||
                c.IsConsumptionPending || m.home==null || c.Polarity!=m.home.Polarity ||
                !fighterSurfaces.TryGetValue(c,out int current) || current<0)continue;
            if(!frontiers.TryGetValue(m.home,out var objectives))continue;
            if(m.Objective==null || !objectives.Contains(m.Objective) || Time.time>=m.NextMarchOrder)
            {
                if(m.Objective!=null && assignments.ContainsKey(m.Objective))assignments[m.Objective]--;
                var previous=m.Objective;m.Objective=null;float best=float.PositiveInfinity;
                foreach(var site in objectives)
                {
                    if(site.routes!=null && !site.routes.ContainsKey(current))continue;
                    assignments.TryGetValue(site,out int count);
                    // A modest crowd change must not reverse a committed march.
                    float score=Vector3.Distance(c.transform.position,site.position)+count*8-(site==previous?40:0);
                    if(score<best){best=score;m.Objective=site;}
                }
                m.NextMarchOrder=Time.time+marchOrderSeconds+Mathf.Abs(m.GetInstanceID()%17)*0.1f;
                if(m.Objective!=null){assignments.TryGetValue(m.Objective,out int n);assignments[m.Objective]=n+1;}
            }
            var goal=m.Objective;if(goal==null)continue;
            if(goal.routes==null)
            {
                if(builtRouteThisTick)continue;
                goal.routes=surfaceMap.Distances(goal.surface,float.PositiveInfinity);builtRouteThisTick=true;
            }
            if(!goal.routes.TryGetValue(current,out float remaining)){m.Objective=null;continue;}
            // Spread the final approach among graph samples within capture range, off the node's body.
            int destination=goal.surface;
            bool approaching=remaining<supportRadius || (m.ApproachSite==goal && remaining<supportRadius+12);
            if(approaching)
            {
                var approaches=goal.approaches;
                if(approaches.Length>0)destination=approaches[Mathf.Abs(m.GetInstanceID()%approaches.Length)];
            }
            Vector3 waypoint=surfaceMap.surfaces[current].position;
            if(destination!=goal.surface && approaching)
            {
                // Short final routes are computed only on reassignment or reaching the approach region.
                if(m.ApproachSite!=goal || m.ApproachPath==null)
                {m.ApproachSite=goal;m.ApproachPath=surfaceMap.Path(current,destination);m.ApproachStep=0;}
                while(m.ApproachStep<m.ApproachPath.Count-1 && Vector3.Distance(c.transform.position,surfaceMap.surfaces[m.ApproachPath[m.ApproachStep]].position)<2.5f)m.ApproachStep++;
                if(m.ApproachPath.Count>0)waypoint=surfaceMap.surfaces[m.ApproachPath[m.ApproachStep]].position;
            }
            else
            {
                m.ApproachSite=null;m.ApproachPath=null;
                // Retain the actual next waypoint across nearest-sample changes. Choosing a fresh
                // nearest cell each tick previously made fighters reverse direction mid-edge.
                if(m.RouteSite!=goal || m.RouteWaypoint<0 || Vector3.Distance(c.transform.position,surfaceMap.surfaces[m.RouteWaypoint].position)>18)
                {m.RouteSite=goal;m.RouteWaypoint=current;}
                if(Vector3.Distance(c.transform.position,surfaceMap.surfaces[m.RouteWaypoint].position)<2.5f)
                {
                    int from=m.RouteWaypoint,next=from;float best=goal.routes[from];
                    foreach(int neighbour in surfaceMap.surfaces[from].neighbours)
                        if(goal.routes.TryGetValue(neighbour,out float d) && d<best){best=d;next=neighbour;}
                    m.RouteWaypoint=next;
                }
                waypoint=surfaceMap.surfaces[m.RouteWaypoint].position;
            }
            m.MarchWaypoint=waypoint;m.MarchValidUntil=Time.time+1.5f;
        }
    }
}
