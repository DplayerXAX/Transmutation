using System.Collections.Generic;
using UnityEngine;

/// <summary>Optimization gates are independent of semantic pauses (capture, throws, freeze).</summary>
public static class CreatureSimulation
{
    sealed class State
    {
        public Object distanceOwner;
        public bool distanceDormant, sourceDriven;
        public float awakeUntil;
        public readonly Dictionary<int,Object> requests=new();
        public bool HasActivity
        {
            get {if(requests.Count==0)return false;foreach(var owner in requests.Values)if(owner)return true;return false;}
        }
    }
    static readonly Dictionary<Creature, State> states = new();
    static int nextRequest;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { states.Clear();nextRequest=0; }
    static State For(Creature creature)
    {
        if (!states.TryGetValue(creature, out var state)) states.Add(creature, state = new State());
        return state;
    }
    public static bool Allows(Creature creature) => !states.TryGetValue(creature, out var state) ||
        (state.HasActivity || (!state.distanceDormant && (!state.sourceDriven || Time.time < state.awakeUntil)));
    /// <summary>Owned minimum activity request. Semantic simulation pauses still take precedence in Creature.</summary>
    public sealed class ActivityRequest : System.IDisposable
    {
        Creature creature;readonly int id;
        internal ActivityRequest(Creature creature,int id){this.creature=creature;this.id=id;}
        public void Dispose()
        {
            if(ReferenceEquals(creature,null))return;
            bool wasAllowed=Allows(creature);
            if(states.TryGetValue(creature,out var state))state.requests.Remove(id);
            if(creature && wasAllowed!=Allows(creature))creature.NotifyOptimizationChanged();
            creature=null;
        }
    }
    public static ActivityRequest KeepActive(Creature creature,Object owner)
    {
        if(!creature||!owner)throw new System.ArgumentException("Activity requires a live creature and owner.");
        bool wasAllowed=Allows(creature);
        int id=++nextRequest;For(creature).requests.Add(id,owner);
        if(wasAllowed!=Allows(creature))creature.NotifyOptimizationChanged();
        return new ActivityRequest(creature,id);
    }
    public static void SourceDriven(Creature creature, bool value)
    {
        if (value) For(creature).sourceDriven = true;
        else if (states.TryGetValue(creature, out var state)) state.sourceDriven = false;
    }
    public static void Wake(Creature creature, float seconds)
    {
        var state = For(creature);
        state.awakeUntil = Mathf.Max(state.awakeUntil, Time.time + seconds);
    }
    public static void DistanceDormant(Creature creature, Object owner, bool value)
    {
        if(ReferenceEquals(creature,null)||(!value&&!states.ContainsKey(creature)))return;
        bool wasAllowed = Allows(creature);
        var state = For(creature);
        if (state.distanceOwner != null && state.distanceOwner != owner) return;
        state.distanceOwner = value ? owner : null;
        state.distanceDormant = value;
        if (wasAllowed != Allows(creature)) creature.NotifyOptimizationChanged();
    }
    public static void Remove(Creature creature) => states.Remove(creature);
}
