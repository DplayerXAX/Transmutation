using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creatures that grow back at home: each slot keeps one copy of a template alive at its spot. When the copy is
/// gone (eaten, used up, destroyed) or dead, a new one appears there after the slot's delay. Copies with a
/// Rigidbody are held still until the ground under them exists (SettleWhenGroundReady).
/// Templates can be prefabs or inactive scene objects. Used by BasinLevel and SpireCreatures.
/// </summary>
public sealed class RegrowingSlots
{
    private sealed class Slot
    {
        public GameObject template;
        public Vector3 home;
        public float delay;
        public GameObject current;
        public float timer;
    }

    private readonly List<Slot> slots = new List<Slot>();
    private float scan;

    public int Count => slots.Count;

    /// <summary>Adds a slot and places its first copy straight away.</summary>
    public void Add(GameObject template, Vector3 home, float delay)
    {
        if (template == null) return;
        var slot = new Slot { template = template, home = home, delay = delay };
        slots.Add(slot);
        Spawn(slot);
    }

    /// <summary>Call every frame; checks once a second.</summary>
    public void Tick(float deltaTime)
    {
        scan -= deltaTime;
        if (scan > 0f) return;
        scan = 1f;
        foreach (Slot slot in slots)
        {
            bool alive = slot.current != null && slot.current.activeInHierarchy;
            if (alive && slot.current.TryGetComponent(out Creature creature)) alive = !creature.IsDead;
            if (alive) continue;
            slot.timer += 1f;
            if (slot.timer >= slot.delay) Spawn(slot);
        }
    }

    private static void Spawn(Slot slot)
    {
        GameObject copy = Object.Instantiate(slot.template, slot.home + Vector3.up * 0.5f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        copy.name = slot.template.name;
        copy.SetActive(true);
        if (copy.GetComponent<Rigidbody>() != null) copy.AddComponent<SettleWhenGroundReady>();
        slot.current = copy;
        slot.timer = 0f;
    }
}
