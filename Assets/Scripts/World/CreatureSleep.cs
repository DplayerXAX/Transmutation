using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pauses creatures far from the player and wakes them again as the player comes near. Only creatures this
/// component paused are woken, so other systems that pause creatures keep control of theirs.
/// Tentacles sleep beyond sleepBeyond (their legs and skin are the costliest per-frame work); every other quiet,
/// ground-bound kind (goo, flowers, Bellows, spiders, squirrels, shrooms...) sleeps beyond otherSleepBeyond.
/// Things in flight or in motion are never paused: bubbles, birds, water drops, thrown or carried creatures.
/// </summary>
public sealed class CreatureSleep : MonoBehaviour, ICreatureRelevancePolicy
{
    [Tooltip("Tentacle creatures further than this from the player are paused (metres).")]
    [Min(10f)] [SerializeField] private float sleepBeyond = 75f;
    [Tooltip("Paused tentacles closer than this wake up again (metres).")]
    [Min(5f)] [SerializeField] private float wakeWithin = 60f;
    [Tooltip("Pause the other quiet kinds of creature too (off = tentacles only, as before).")]
    [SerializeField] private bool allKinds = true;
    [Tooltip("Other creatures further than this from the player are paused (metres).")]
    [Min(10f)] [SerializeField] private float otherSleepBeyond = 85f;
    [Tooltip("Paused other creatures closer than this wake up again (metres).")]
    [Min(5f)] [SerializeField] private float otherWakeWithin = 70f;

    sealed class Entry { public Creature creature; public float nextCheck; }
    [SerializeField, Min(1)] private int checksPerFrame = 32;
    private readonly List<Entry> creatures = new();
    private readonly HashSet<Creature> registered = new();
    private readonly HashSet<Creature> sleeping = new HashSet<Creature>();
    private int cursor;
    private float nextPlayerSearch;
    private Transform player;
    private CreatureSimulationScheduler scheduler;
    private bool previousAllKinds;
    public float RelevanceInterval=>0;
    public int CandidateCount=>creatures.Count;
    public int ChecksLastFrame {get;private set;}
    public int ChecksPerFrame=>checksPerFrame;

    public int SleepingCount => sleeping.Count;

    /// <summary>Kinds that are safe to pause: nothing that is flying, falling or carried by the world's own motion.</summary>
    private static bool Sleepable(Creature c) =>
        !(c is SeaAngelCreature || c is DimensionStone || c is LeashBubble || c is BirdCreature || c is ErosiveFluidDroplet || c is ProjectileCreature || c is BlastCore ||
          c is GlowBee || c is SquirrelFood || c is GlowHoney);

    void OnEnable()
    {
        if(!Application.isPlaying)return;
        previousAllKinds=allKinds;
        CreatureRegistry.Added+=Add;CreatureRegistry.Removed+=Remove;
        foreach(var creature in CreatureRegistry.All)Add(creature);
        RegisterScheduler();
    }
    void Start()=>RegisterScheduler();
    void RegisterScheduler(){scheduler=CreatureSimulationScheduler.For(gameObject.scene);if(scheduler)scheduler.Register(this,this);}
    void Add(Creature creature)
    {
        if(!creature || creature.gameObject.scene!=gameObject.scene || (!allKinds && !(creature is TentacleCreature)) ||
            !Sleepable(creature) || !registered.Add(creature))return;
        creatures.Add(new Entry{creature=creature,nextCheck=Time.time+Mathf.Repeat(Mathf.Abs(creature.GetInstanceID())*.618034f,1)*.5f});
    }
    void Remove(Creature creature)
    {
        registered.Remove(creature);sleeping.Remove(creature);
        CreatureSimulation.DistanceDormant(creature,this,false);
        creatures.RemoveAll(e=>e.creature==creature);
        if(cursor>=creatures.Count)cursor=0;
    }
    public void RefreshRelevance()
    {
        ChecksLastFrame=0;
        if(previousAllKinds!=allKinds)
        {
            previousAllKinds=allKinds;
            foreach(var creature in CreatureRegistry.All)Add(creature);
        }
        if (player == null)
        {
            if(Time.time<nextPlayerSearch)return;
            nextPlayerSearch=Time.time+1;
            foreach(var carrier in FindObjectsByType<PlayerCreatureCarrier>(FindObjectsSortMode.None))
                if(carrier.gameObject.scene==gameObject.scene){player=carrier.PlayerBody?carrier.PlayerBody.transform:carrier.transform;break;}
            if(!player)return;
        }
        int visits=Mathf.Min(Mathf.Max(1,checksPerFrame),creatures.Count);
        for(int visit=0;visit<visits;visit++)
        {
            if(creatures.Count==0)break;
            if(cursor>=creatures.Count)cursor=0;
            var entry=creatures[cursor++];var creature=entry.creature;
            if (!creature || creature.IsDead || Time.time<entry.nextCheck) continue;
            entry.nextCheck=Time.time+.5f;ChecksLastFrame++;
            if(!allKinds && !(creature is TentacleCreature))
            {CreatureSimulation.DistanceDormant(creature,this,false);sleeping.Remove(creature);continue;}
            bool tentacle = creature is TentacleCreature;
            float beyond = tentacle ? sleepBeyond : otherSleepBeyond, within = tentacle ? wakeWithin : otherWakeWithin;
            float distance = Vector3.Distance(creature.transform.position, player.position);
            bool busy = creature.IsCarried || creature.IsBubbleCaptured || creature.IsPetted || CreatureThrower.IsFlying(creature) ||
                (creature.TryGetComponent<WebCapture>(out var capture) && capture.IsCaptured) ||
                (creature.TryGetComponent<CreatureCombat>(out var combat) && combat.Target!=null) ||
                creature is SquirrelNest nest && nest.IsMoving || creature is Squirrel squirrel && squirrel.IsMigrating ||
                tentacle && (((TentacleCreature)creature).PinnedCount > 0 || ((TentacleCreature)creature).Suspended);
            // Do not freeze autonomous decisions in the middle of an unresolved fall.
            var rb=creature.GetComponent<Rigidbody>();
            if(rb && !rb.isKinematic && rb.useGravity)
            {
                Vector3 relative=rb.linearVelocity-(creature.TryGetComponent<MovementFrameMember>(out var member)?member.PointVelocity:Vector3.zero);
                busy|=Mathf.Abs(relative.y)>1f;
            }
            if (!sleeping.Contains(creature))
            {
                if (busy || distance <= beyond || !creature.SimulationPermitted) continue;
                CreatureSimulation.DistanceDormant(creature, this, true);
                sleeping.Add(creature);
            }
            else if (busy || distance < within)
            {
                CreatureSimulation.DistanceDormant(creature, this, false);
                sleeping.Remove(creature);
            }
        }
    }

    private void OnDisable()
    {
        CreatureRegistry.Added-=Add;CreatureRegistry.Removed-=Remove;
        if(scheduler)scheduler.Unregister(this);
        foreach (var creature in sleeping) if (creature != null) CreatureSimulation.DistanceDormant(creature, this, false);
        sleeping.Clear();
        creatures.Clear();registered.Clear();cursor=0;
    }
}
