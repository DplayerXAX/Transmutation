using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pauses tentacle creatures far from the player (their legs and skin are the costliest per-frame work in a
/// big map) and wakes them again as the player comes near. Only creatures this component paused are woken,
/// so other systems that pause creatures keep control of theirs.
/// </summary>
public sealed class CreatureSleep : MonoBehaviour
{
    [Tooltip("Creatures further than this from the player are paused (metres).")]
    [Min(10f)] [SerializeField] private float sleepBeyond = 75f;
    [Tooltip("Paused creatures closer than this wake up again (metres).")]
    [Min(5f)] [SerializeField] private float wakeWithin = 60f;

    private readonly List<TentacleCreature> creatures = new List<TentacleCreature>();
    private readonly HashSet<TentacleCreature> sleeping = new HashSet<TentacleCreature>();
    private float nextCheck, nextScan;
    private Transform player;

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (player == null)
        {
            var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            if (carrier == null) return;
            player = carrier.PlayerBody != null ? carrier.PlayerBody.transform : carrier.transform;
        }
        // New tentacles appear during play (the mother's companions, practice sites); look again now and then.
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 5f;
            creatures.Clear();
            creatures.AddRange(FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None));
        }
        foreach (var creature in creatures)
        {
            if (creature == null || creature.IsDead) continue;
            float distance = Vector3.Distance(creature.transform.position, player.position);
            bool busy = creature.IsCarried || creature.PinnedCount > 0;
            if (!sleeping.Contains(creature))
            {
                if (busy || distance <= sleepBeyond || !creature.SimulationEnabled) continue;
                creature.SetSimulationEnabled(false);
                sleeping.Add(creature);
            }
            else if (busy || distance < wakeWithin)
            {
                creature.SetSimulationEnabled(true);
                sleeping.Remove(creature);
            }
        }
        sleeping.RemoveWhere(c => c == null);
    }

    private void OnDisable()
    {
        foreach (var creature in sleeping) if (creature != null) creature.SetSimulationEnabled(true);
        sleeping.Clear();
    }
}
