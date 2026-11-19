using UnityEngine;

/// <summary>Colony ownership, manager-issued idle march orders, and bounded remains.</summary>
public sealed class AsuraTerritoryMember : MonoBehaviour
{
    public AsuraHome home;
    public Creature Creature {get; private set;}
    public AsuraTerritory Territory {get;internal set;}
    public AsuraTerritory.BattlefieldSite Objective {get;internal set;}
    public Vector3 MarchWaypoint {get;internal set;}
    public float MarchValidUntil {get;internal set;}
    internal float NextMarchOrder;
    internal AsuraTerritory.BattlefieldSite ApproachSite;
    internal System.Collections.Generic.List<int> ApproachPath;
    internal int ApproachStep;
    internal AsuraTerritory.BattlefieldSite RouteSite;
    internal int RouteWaypoint=-1;
    private Vector3 progressPosition;
    public static bool TryMarch(Creature creature,AsuraMotor motor,float speed)
    {
        if(motor==null || creature==null || creature.IsDead || creature.IsCarried || creature.IsBubbleCaptured ||
            creature.IsConsumptionPending || !creature.SimulationEnabled || FlashBlindness.IsBlinded(creature) ||
            !creature.TryGetComponent<AsuraTerritoryMember>(out var member) || Time.time>=member.MarchValidUntil)return false;
        Vector3 delta=member.MarchWaypoint-creature.transform.position;
        var horizontal=Vector3.ProjectOnPlane(delta,Vector3.up);
        motor.SetIntent(horizontal.magnitude>1.2f?horizontal.normalized*speed*AsuraBreachMomentum.Speed(creature):Vector3.zero,delta);
        return true;
    }
    // Written by the territory manager using its existing surface-coverage union.
    public float TerritoryDetectionRange {get; internal set;}
    public static float DetectionRange(Creature creature,float normal,bool retention=false)
    {
        float range=normal;
        if(creature!=null && creature.TryGetComponent<AsuraTerritoryMember>(out var member))
            range=Mathf.Max(range,member.TerritoryDetectionRange*(retention?1.25f:1f));
        return range*AsuraBreachMomentum.Detection(creature);
    }
    public static float SearchInterval(Creature creature,float normal)=>
        creature!=null && creature.TryGetComponent<AsuraTerritoryMember>(out var member) && member.TerritoryDetectionRange>0?Mathf.Max(normal,0.5f):normal;
    private float deadTime,quietTime;
    private void Awake(){Creature=GetComponent<Creature>();Creature.Died+=OnDied;}
    private void OnDestroy(){if(Creature!=null)Creature.Died-=OnDied;}
    private void OnDied(Creature dead)
    {
        // Blood from this renewable encounter expires; existing blood elsewhere is unchanged.
        var drop=GetComponent<AsuraBloodDrop>();
        if(drop!=null && drop.DroppedBlood!=null)drop.DroppedBlood.gameObject.AddComponent<AsuraTerritoryResidue>();
    }
    private void Update()
    {
        if(Creature==null)return;
        if(Creature.IsCarried || Creature.IsBubbleCaptured || Creature.IsConsumptionPending)return;
        if(Creature.IsDead){deadTime+=Time.deltaTime;if(deadTime>25)Destroy(gameObject);return;}
        if(!Creature.SimulationEnabled)return;
        var combat=GetComponent<CreatureCombat>();
        quietTime=(combat!=null && combat.Target!=null) || (Creature is RollingAsura roller && roller.Target!=null)?0:quietTime+Time.deltaTime;
        if(Time.time<MarchValidUntil && Vector3.Distance(transform.position,progressPosition)>1)
        {quietTime=0;progressPosition=transform.position;}
        // Prevent an unreachable quiet pocket from permanently consuming the whole population budget.
        if(quietTime>180 || transform.position.y< -40)Creature.Die();
    }
}

