using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Active creature membership, shared by scene-local food queries. No per-step discovery.</summary>
internal static class CreatureRegistry
{
    private static readonly List<Creature> creatures = new List<Creature>(256);
    private static readonly List<PlayerCreatureStats> players = new List<PlayerCreatureStats>(4);
    internal static int CombatRevision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        creatures.Clear(); players.Clear(); CombatRevision++;
        SceneManager.sceneUnloaded -= RemoveScene;
        SceneManager.sceneUnloaded += RemoveScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreMembership()
    {
        // Also handles entering Play Mode with domain and scene reload disabled.
        foreach (Creature creature in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
            if (creature.isActiveAndEnabled) Register(creature);
        foreach (PlayerCreatureStats player in Object.FindObjectsByType<PlayerCreatureStats>(FindObjectsSortMode.None))
            if (player.isActiveAndEnabled) Register(player);
    }

    internal static IReadOnlyList<Creature> All => creatures;

    internal static void Register(Creature creature)
    {
        if (creature != null && !creature.IsConsumptionPending && !creatures.Contains(creature))
        { creatures.Add(creature); CombatRevision++; }
    }

    internal static void Unregister(Creature creature) { if (creatures.Remove(creature)) CombatRevision++; }
    internal static void Register(PlayerCreatureStats player)
    { if (player != null && !players.Contains(player)) { players.Add(player); CombatRevision++; } }
    internal static void Unregister(PlayerCreatureStats player) { if (players.Remove(player)) CombatRevision++; }

    private static void RemoveScene(Scene scene)
    {
        for (int i = creatures.Count - 1; i >= 0; i--)
            if (creatures[i] == null || creatures[i].gameObject.scene == scene) creatures.RemoveAt(i);
        for (int i = players.Count - 1; i >= 0; i--)
            if (players[i] == null || players[i].gameObject.scene == scene) players.RemoveAt(i);
        CombatRevision++;
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

    internal static void SnapshotCombat(Scene scene, List<Creature> targets, List<Vector3> positions)
    {
        targets.Clear(); positions.Clear();
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            Creature creature = creatures[i];
            if (creature == null) { creatures.RemoveAt(i); continue; }
            if (!creature.IsCombatActive || creature.gameObject.scene != scene) continue;
            targets.Add(creature); positions.Add(creature.AimPosition);
        }
    }

    internal static void SnapshotCombat(Scene scene, List<ICombatTarget> targets, List<Vector3> positions)
    {
        targets.Clear(); positions.Clear();
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            Creature creature = creatures[i];
            if (creature == null) { creatures.RemoveAt(i); continue; }
            if (!creature.isActiveAndEnabled || creature.IsDead || creature.IsConsumptionPending ||
                creature.gameObject.scene != scene) continue;
            targets.Add(creature);
            positions.Add(creature.AimPosition);
        }
        for (int i = players.Count - 1; i >= 0; i--)
        {
            PlayerCreatureStats player = players[i];
            if (player == null) { players.RemoveAt(i); continue; }
            if (!player.IsCombatActive || player.CombatRoot.gameObject.scene != scene) continue;
            targets.Add(player); positions.Add(player.AimPosition);
        }
    }
}
