using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places where time stands still for creatures: inside an active source's sphere a creature does not age,
/// neither naturally (Creature.AdvanceAge) nor from erosive water. Gaining life still works.
/// Sources (the Geode Larva's aura) register while enabled and say each frame whether they are active.
/// </summary>
public static class StillnessField
{
    public interface ISource
    {
        bool StillnessActive { get; }
        Vector3 StillnessCentre { get; }
        float StillnessRadius { get; }
        UnityEngine.SceneManagement.Scene StillnessScene { get; }
    }

    private static readonly List<ISource> sources = new List<ISource>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => sources.Clear();

    public static int Count => sources.Count;

    public static void Register(ISource source) { if (source != null && !sources.Contains(source)) sources.Add(source); }
    public static void Unregister(ISource source) => sources.Remove(source);

    /// <summary>Whether time stands still at this point in this scene.</summary>
    public static bool Holds(Vector3 point, UnityEngine.SceneManagement.Scene scene)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            ISource source = sources[i];
            if (source == null || (source is Object unity && unity == null) || !source.StillnessActive || source.StillnessScene != scene) continue;
            float radius = source.StillnessRadius;
            if ((point - source.StillnessCentre).sqrMagnitude <= radius * radius) return true;
        }
        return false;
    }

    /// <summary>Whether this creature is inside an active field and should not age.</summary>
    public static bool Holds(Creature creature) =>
        sources.Count > 0 && creature != null && creature is not ISource && Holds(creature.transform.position, creature.gameObject.scene);
}
