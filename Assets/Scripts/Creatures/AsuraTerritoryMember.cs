using UnityEngine;

/// <summary>Colony ownership and bounded remains. Does not steer or choose combat actions.</summary>
public sealed class AsuraTerritoryMember : MonoBehaviour
{
    public AsuraHome home;
    public Creature Creature {get; private set;}
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
        quietTime=combat!=null && combat.Target!=null?0:quietTime+Time.deltaTime;
        // Prevent an unreachable quiet pocket from permanently consuming the whole population budget.
        if(quietTime>180 || transform.position.y< -40)Creature.Die();
    }
}
