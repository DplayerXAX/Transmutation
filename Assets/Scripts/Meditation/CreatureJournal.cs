using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers each kind of creature the first time the player gets close to it.
/// A small plaster-like cast of its current shape is saved, so it can stand in the
/// reflection space. One entry per creature type (all Fuzz Creatures share one).
/// </summary>
[DisallowMultipleComponent]
public sealed class CreatureJournal : MonoBehaviour
{
    [Tooltip("How close the player has to be, in metres, to remember a creature.")]
    [Min(0.5f)] [SerializeField] private float encounterDistance = 6f;
    [Tooltip("The creature also has to be on screen.")]
    [SerializeField] private bool requireInView = true;
    [Tooltip("Casts keep the creature's real size, but nothing larger than this many metres.")]
    [Min(0.1f)] [SerializeField] private float castMaxSize = 2.5f;
    [Min(0.05f)] [SerializeField] private float scanInterval = 0.25f;

    public event Action<JournalEntry> CreatureRecorded;

    /// <summary>False while meditating, so nothing is recorded then.</summary>
    public bool Scanning { get; set; } = true;
    public IReadOnlyList<JournalEntry> Entries => data.entries;

    private JournalData data;
    private readonly Dictionary<string, CreatureCast> casts = new Dictionary<string, CreatureCast>();
    private readonly HashSet<string> known = new HashSet<string>();
    private Creature[] creatures = Array.Empty<Creature>();
    private float scanTimer;
    private float refreshTimer;
    private Camera viewCamera;

    private void Awake()
    {
        data = ReflectionStorage.LoadJournal();
        foreach (JournalEntry entry in data.entries) known.Add(entry.key);
    }

    private void Update()
    {
        if (!Scanning) return;
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 2f;
            creatures = FindObjectsByType<Creature>(FindObjectsSortMode.None);
            if (viewCamera == null) viewCamera = Camera.main;
        }

        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f || viewCamera == null) return;
        scanTimer = scanInterval;

        Vector3 eye = viewCamera.transform.position;
        foreach (Creature creature in creatures)
        {
            if (creature == null || !creature.isActiveAndEnabled) continue;
            string key = KeyOf(creature);
            if (known.Contains(key)) continue;
            Vector3 position = creature.transform.position;
            if ((position - eye).sqrMagnitude > encounterDistance * encounterDistance) continue;
            if (requireInView)
            {
                Vector3 viewport = viewCamera.WorldToViewportPoint(position);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
            }
            Record(creature);
        }
    }

    public static string KeyOf(Creature creature) => creature.GetType().Name;

    private void Record(Creature creature)
    {
        string key = KeyOf(creature);
        known.Add(key);
        var entry = new JournalEntry
        {
            key = key,
            displayName = DisplayName(creature),
            firstSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            encounterPosition = creature.transform.position
        };
        data.entries.Add(entry);

        CreatureCast cast = CreatureCast.Capture(creature, castMaxSize);
        if (cast != null)
        {
            casts[key] = cast;
            cast.Save(ReflectionStorage.CastPath(key));
        }
        Save();
        CreatureRecorded?.Invoke(entry);
    }

    public void Save() => ReflectionStorage.SaveJournal(data);

    /// <summary>
    /// The cast for a creature: from memory, from disk, copied again from a creature of the
    /// same kind still in the scene, or a stand-in pebble.
    /// </summary>
    public CreatureCast GetCast(JournalEntry entry)
    {
        if (casts.TryGetValue(entry.key, out CreatureCast cast) && cast != null) return cast;
        string path = ReflectionStorage.CastPath(entry.key);
        cast = CreatureCast.Load(path);
        if (cast == null)
        {
            foreach (Creature creature in FindObjectsByType<Creature>(FindObjectsSortMode.None))
            {
                if (KeyOf(creature) != entry.key) continue;
                cast = CreatureCast.Capture(creature, castMaxSize);
                if (cast != null)
                {
                    cast.Save(path);
                    break;
                }
            }
        }
        if (cast == null) cast = CreatureCast.Pebble(entry.key.GetHashCode(), 0.5f);
        casts[entry.key] = cast;
        return cast;
    }

    private static string DisplayName(Creature creature)
    {
        string name = creature.CreatureName;
        if (!string.IsNullOrEmpty(name) && name != "Unnamed Creature") return name;
        // "FuzzCreature" -> "Fuzz Creature"
        string type = creature.GetType().Name;
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < type.Length; i++)
        {
            if (i > 0 && char.IsUpper(type[i])) builder.Append(' ');
            builder.Append(type[i]);
        }
        return builder.ToString();
    }

}
