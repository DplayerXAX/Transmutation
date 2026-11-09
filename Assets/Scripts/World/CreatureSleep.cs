using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pauses creatures far from the player and wakes them again as the player comes near. Only creatures this
/// component paused are woken, so other systems that pause creatures keep control of theirs.
/// Tentacles sleep beyond sleepBeyond (their legs and skin are the costliest per-frame work); every other quiet,
/// ground-bound kind (goo, flowers, Bellows, spiders, squirrels, shrooms...) sleeps beyond otherSleepBeyond.
/// Things in flight or in motion are never paused: bubbles, birds, water drops, thrown or carried creatures.
/// </summary>
public sealed class CreatureSleep : MonoBehaviour
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

    private readonly List<Creature> creatures = new List<Creature>();
    private readonly HashSet<Creature> sleeping = new HashSet<Creature>();
    private float nextCheck, nextScan;
    private Transform player;

    public int SleepingCount => sleeping.Count;

    /// <summary>Kinds that are safe to pause: nothing that is flying, falling or carried by the world's own motion.</summary>
    private static bool Sleepable(Creature c) =>
        !(c is LeashBubble || c is BirdCreature || c is ErosiveFluidDroplet || c is ProjectileCreature || c is BlastCore ||
          c is GlowBee || c is SquirrelFood || c is GlowHoney);

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
        // Creatures appear during play (the mother's companions, practice sites, spawned flowers); look again now and then.
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 5f;
            creatures.Clear();
            if (allKinds)
            {
                foreach (Creature c in FindObjectsByType<Creature>(FindObjectsSortMode.None)) if (Sleepable(c)) creatures.Add(c);
            }
            else creatures.AddRange(FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None));
        }
        foreach (var creature in creatures)
        {
            if (creature == null || creature.IsDead) continue;
            bool tentacle = creature is TentacleCreature;
            float beyond = tentacle ? sleepBeyond : otherSleepBeyond, within = tentacle ? wakeWithin : otherWakeWithin;
            float distance = Vector3.Distance(creature.transform.position, player.position);
            bool busy = creature.IsCarried || creature.IsBubbleCaptured || tentacle && ((TentacleCreature)creature).PinnedCount > 0;
            if (!sleeping.Contains(creature))
            {
                if (busy || distance <= beyond || !creature.SimulationEnabled) continue;
                creature.SetSimulationEnabled(false);
                sleeping.Add(creature);
            }
            else if (busy || distance < within)
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
