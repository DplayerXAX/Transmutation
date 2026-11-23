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
    }
    static readonly Dictionary<Creature, State> states = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => states.Clear();
    static State For(Creature creature)
    {
        if (!states.TryGetValue(creature, out var state)) states.Add(creature, state = new State());
        return state;
    }
    public static bool Allows(Creature creature) => !states.TryGetValue(creature, out var state) ||
        (!state.distanceDormant && (!state.sourceDriven || Time.time < state.awakeUntil));
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
        bool wasAllowed = Allows(creature);
        var state = For(creature);
        if (state.distanceOwner != null && state.distanceOwner != owner) return;
        state.distanceOwner = value ? owner : null;
        state.distanceDormant = value;
        if (wasAllowed != Allows(creature)) creature.NotifyOptimizationChanged();
    }
    public static void Remove(Creature creature) => states.Remove(creature);
}
