using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Active creature membership, shared by scene-local food queries. No per-step discovery.</summary>
internal static class CreatureRegistry
{
    private static readonly List<Creature> creatures = new List<Creature>(256);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        creatures.Clear();
        SceneManager.sceneUnloaded -= RemoveScene;
        SceneManager.sceneUnloaded += RemoveScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreMembership()
    {
        // Also handles entering Play Mode with domain and scene reload disabled.
        foreach (Creature creature in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
            if (creature.isActiveAndEnabled) Register(creature);
    }

    internal static void Register(Creature creature)
    {
        if (creature != null && !creature.IsConsumptionPending && !creatures.Contains(creature)) creatures.Add(creature);
    }

    internal static void Unregister(Creature creature) => creatures.Remove(creature);

    private static void RemoveScene(Scene scene)
    {
        for (int i = creatures.Count - 1; i >= 0; i--)
            if (creatures[i] == null || creatures[i].gameObject.scene == scene) creatures.RemoveAt(i);
    }

    internal static void SnapshotFood(Scene scene, bool includeUntagged, List<Creature> targets, List<Vector3> positions)
    {
        targets.Clear();
        positions.Clear();
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            Creature creature = creatures[i];
            if (creature == null) { creatures.RemoveAt(i); continue; }
            if (!creature.isActiveAndEnabled || creature.IsConsumptionPending || creature.gameObject.scene != scene ||
                (!includeUntagged && creature.FoodTags == Creature.FoodTag.None)) continue;
            targets.Add(creature);
            positions.Add(creature.TryGetComponent<Rigidbody>(out var body) ? body.position : creature.transform.position);
        }
    }
}
