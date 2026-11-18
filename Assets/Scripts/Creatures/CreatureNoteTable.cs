using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which baton note each creature species belongs to (Assets/Resources/CreatureNotes.asset, editable in the Inspector).
/// E: drifting and glowing things. G: growing things and gentle grazers. B: stone, water and slow bodies.
/// D: fire, hunters and danger. A subclass uses its base class's entry if it has none; a species missing from the table
/// still gets a fixed note from its name, so every creature always has one.
/// </summary>
[CreateAssetMenu(menuName = "Capstone/Creature Note Table", fileName = "CreatureNotes")]
public sealed class CreatureNoteTable : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [Tooltip("Class name of a Creature subclass, e.g. FuzzCreature.")]
        public string creatureType;
        public CaveNote note;
        [Tooltip("Its part in the cave's music (CreatureSong).")]
        public SongRole role;
        [Tooltip("How it follows after its phrase: walks, flies, stays and sings where it is, or takes no part (objects, food, shots).")]
        public SongMotion motion;
    }

    public List<Entry> entries = new List<Entry>();

    private static CreatureNoteTable loaded;
    private static bool tried;
    private static readonly Dictionary<Type, CaveNote> cache = new Dictionary<Type, CaveNote>();
    private static readonly Dictionary<Type, SongRole> roleCache = new Dictionary<Type, SongRole>();
    private static readonly Dictionary<Type, SongMotion> motionCache = new Dictionary<Type, SongMotion>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = null; tried = false; cache.Clear(); roleCache.Clear(); motionCache.Clear(); }

    /// <summary>The note of a species.</summary>
    public static CaveNote For(Type type)
    {
        if (cache.TryGetValue(type, out CaveNote known)) return known;
        if (!tried) { tried = true; loaded = Resources.Load<CreatureNoteTable>("CreatureNotes"); }
        CaveNote result = Fallback(type.Name);
        if (loaded != null)
        {
            bool found = false;
            for (Type t = type; !found && t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (Entry e in loaded.entries)
                    if (e != null && e.creatureType == t.Name) { result = e.note; found = true; break; }
        }
        cache[type] = result;
        return result;
    }

    /// <summary>The role of a species in the music: from the table, or from its note if it has no entry.</summary>
    public static SongRole RoleFor(Type type)
    {
        if (roleCache.TryGetValue(type, out SongRole known)) return known;
        CaveNote n = For(type);
        SongRole result = n == CaveNote.E ? SongRole.Melody : n == CaveNote.G ? SongRole.Accompaniment : SongRole.Drums;
        if (loaded != null)
        {
            bool found = false;
            for (Type t = type; !found && t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (Entry e in loaded.entries)
                    if (e != null && e.creatureType == t.Name) { result = e.role; found = true; break; }
        }
        roleCache[type] = result;
        return result;
    }

    /// <summary>How a species takes part in the song. A species not in the table takes no part.</summary>
    public static SongMotion MotionFor(Type type)
    {
        if (motionCache.TryGetValue(type, out SongMotion known)) return known;
        For(type); // loads the table
        SongMotion result = SongMotion.Silent;
        if (loaded != null)
        {
            bool found = false;
            for (Type t = type; !found && t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (Entry e in loaded.entries)
                    if (e != null && e.creatureType == t.Name) { result = e.motion; found = true; break; }
        }
        motionCache[type] = result;
        return result;
    }

    /// <summary>The note of one creature (a glow creature may have its own, like each Tone Mesa).</summary>
    public static CaveNote For(Creature creature) => creature is CaveGlowCreature glow ? glow.Note : For(creature.GetType());

    private static CaveNote Fallback(string name)
    {
        int sum = 0;
        foreach (char c in name) sum += c;
        return (CaveNote)(sum % 4);
    }
}
