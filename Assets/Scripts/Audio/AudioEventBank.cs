using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public enum AudioEventKind
{
    // Main loop. Starting it also starts every music layer, silent and in sync.
    MusicBase,
    // Loop that plays in sync with the base; its volume follows nearby channels.
    MusicLayer,
    // One shot or looping sound effect, 2D or attached to an emitter.
    Sound
}

public enum AudioBus { Music, Sound }

[Serializable]
public class AudioEventDef
{
    public string name = "New_Event";
    public AudioEventKind kind = AudioEventKind.Sound;
    [Tooltip("One is picked at random. Music clips should all have the same length as the base loop.")]
    public AudioClip[] clips = new AudioClip[0];
    [Range(0f, 1f)] public float volume = 1f;
    public Vector2 pitchRange = Vector2.one;
    [Tooltip("Sounds only. Music always loops.")]
    public bool loop;
    [Tooltip("Sounds only. 0 = 2D, 1 = fully 3D at the emitter.")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Min(0.1f)] public float maxDistance = 30f;
    [Tooltip("Music layers: seconds to fade in when a channel gets close.")]
    [Min(0.01f)] public float fadeIn = 1.5f;
    [Tooltip("Music layers: seconds to fade out when the channel is left.")]
    [Min(0.01f)] public float fadeOut = 3f;
    [Tooltip("Synth voice used while no clip is assigned. -1 = silent.")]
    public int placeholderVoice = -1;
}

[Serializable]
public class CreatureChannel
{
    [Tooltip("Class name of a Creature subclass, e.g. FuzzCreature.")]
    public string creatureType;
    public string layerEvent;
    [Min(0f)] public float innerRadius = 4f;
    [Min(0.1f)] public float outerRadius = 20f;
}

/// <summary>
/// All audio events by name, plus which creature types play which music layer.
/// Event names are meant to match Wwise event names later.
/// </summary>
[CreateAssetMenu(menuName = "Capstone/Audio Event Bank", fileName = "AudioEvents")]
public class AudioEventBank : ScriptableObject
{
    [Header("Music Clock (used by placeholder synth)")]
    [Min(20f)] public float bpm = 84f;
    [Min(1)] public int beatsPerBar = 4;
    [Min(1)] public int bars = 4;

    [Header("Mixer (optional)")]
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup soundGroup;

    public List<AudioEventDef> events = new List<AudioEventDef>();
    public List<CreatureChannel> creatureChannels = new List<CreatureChannel>();

    public float LoopSeconds => bars * beatsPerBar * 60f / bpm;

    public AudioEventDef Find(string eventName)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].name == eventName) return events[i];
        return null;
    }
}
