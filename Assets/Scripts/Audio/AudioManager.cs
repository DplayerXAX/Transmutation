using System.Collections.Generic;
using UnityEngine;

public enum AudioBackend
{
    // Events and RTPCs go to Wwise. Falls back to Unity if Wwise isn't initialized.
    Wwise,
    // Unity AudioSources, using clips from the event bank or placeholder synth.
    Unity
}

/// <summary>
/// Plays audio by event name. Music is vertical layering: one base loop plus layers that
/// stay in sync and fade in when the listener is near a matching channel (creature or zone).
///
/// Wwise: the base is a Wwise event; each layer is a Game Parameter (0..100) with the
/// layer's name, driving the volume of a music track inside the base music.
/// Unity: the base and layers are synced looping AudioSources.
/// Callers only use event names, so they don't care which backend runs.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioEventBank bank;
    [SerializeField] private AudioBackend backend = AudioBackend.Wwise;
    [Tooltip("Wwise SoundBanks loaded on Start.")]
    [SerializeField] private string[] wwiseBanks = { "Transmutation" };
    [Tooltip("Posted on Start. Leave empty to start music from code.")]
    [SerializeField] private string startEvent = "PlayBGM";

    [Header("Volume (Unity backend)")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.8f;
    [Range(0f, 1f)] public float soundVolume = 1f;

    [Header("Channels")]
    [Tooltip("Seconds between scans for creatures spawned at runtime.")]
    [Min(0.1f)] [SerializeField] private float creatureScanInterval = 1f;
    [SerializeField] private bool showDebug;

    private sealed class Layer
    {
        public AudioEventDef def;
        public AudioSource source;
        public float weight;
        public float target;
        public float forced = -1f;
        public float sent = -1f;
    }

    private sealed class Voice
    {
        public AudioEventDef def;
        public AudioSource source;
        public GameObject emitter;
        public bool followEmitter;
        public float fade = 1f;
        public float fadeSpeed;
    }

    private struct CreatureEmitter
    {
        public Creature creature;
        public CreatureChannel channel;
    }

    private static readonly List<MusicChannel> channels = new List<MusicChannel>();

    private readonly Dictionary<string, AudioClip> placeholders = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>();
    private readonly List<CreatureEmitter> creatures = new List<CreatureEmitter>();
    private readonly List<Voice> voices = new List<Voice>();
    private readonly Stack<AudioSource> pool = new Stack<AudioSource>();

    private bool useWwise;
    private AudioEventDef baseDef;
    private AudioSource baseSource;
    private bool musicPlaying;
    private float beatDuration;
    private float lastBeatTime = -1f;
    private float lastGridHit = -1f;

    private struct GridHit
    {
        public float time;
        public string eventName;
        public GameObject emitter;
        public bool emitterWasNull;
    }

    private readonly List<GridHit> gridHits = new List<GridHit>();
    private float baseFade;
    private float baseFadeTarget;
    private Transform listener;
    private float nextScan;

    public AudioEventBank Bank => bank;
    public bool UsingWwise => useWwise;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (bank == null)
        {
            Debug.LogWarning("[Audio] No event bank assigned.", this);
            return;
        }

        useWwise = backend == AudioBackend.Wwise && AkUnitySoundEngine.IsInitialized();
        if (backend == AudioBackend.Wwise && !useWwise)
            Debug.LogWarning("[Audio] Wwise is not initialized (no AkInitializer?). Using Unity audio.", this);
        if (useWwise)
            foreach (string b in wwiseBanks)
                if (!string.IsNullOrEmpty(b)) AkBankManager.LoadBank(b, false, false);

        foreach (AudioEventDef d in bank.events)
            if (d.kind == AudioEventKind.MusicLayer) layers[d.name] = new Layer { def = d };

        if (!string.IsNullOrEmpty(startEvent)) Post(startEvent, null);
    }

    // ---------- Public API ----------

    /// <summary>Plays an event, optionally on an emitter object for 3D sound.</summary>
    public static void PostEvent(string eventName, GameObject emitter = null)
    {
        if (Instance != null) Instance.Post(eventName, emitter);
    }

    /// <summary>Stops an event. With an emitter, only that emitter's instances stop.</summary>
    public static void StopEvent(string eventName, GameObject emitter = null, float fadeSeconds = 0.3f)
    {
        if (Instance != null) Instance.Stop(eventName, emitter, fadeSeconds);
    }

    /// <summary>Overrides a music layer's weight (0..1). Pass a negative value to go back to channels.</summary>
    public static void SetLayerWeight(string layerEvent, float weight)
    {
        if (Instance != null && Instance.layers.TryGetValue(layerEvent, out Layer layer))
            layer.forced = weight < 0f ? -1f : Mathf.Clamp01(weight);
    }

    /// <summary>Current audible weight of a music layer.</summary>
    public static float GetLayerWeight(string layerEvent)
    {
        return Instance != null && Instance.layers.TryGetValue(layerEvent, out Layer layer) ? layer.weight : 0f;
    }

    public static void RegisterChannel(MusicChannel channel)
    {
        if (!channels.Contains(channel)) channels.Add(channel);
    }

    public static void UnregisterChannel(MusicChannel channel) => channels.Remove(channel);

    /// <summary>
    /// Plays an event on the next music grid slot (division 1 = beats, 2 = eighths, 4 = sixteenths).
    /// Extra calls in a slot that's already taken are dropped. Plays at once if no music runs.
    /// </summary>
    public static void PostEventOnGrid(string eventName, GameObject emitter = null, int division = 2)
    {
        if (Instance != null) Instance.PostOnGrid(eventName, emitter, division);
    }

    // ---------- Posting ----------

    private void Post(string eventName, GameObject emitter)
    {
        AudioEventDef def = bank != null ? bank.Find(eventName) : null;
        if (def == null)
        {
            // Unknown to the bank: still try Wwise, so plain sound events need no bank entry.
            if (useWwise) WwisePost(eventName, emitter != null ? emitter : gameObject);
            else Debug.LogWarning($"[Audio] Unknown event '{eventName}'.", this);
            return;
        }

        switch (def.kind)
        {
            case AudioEventKind.MusicBase:
                StartMusic(def);
                break;
            case AudioEventKind.MusicLayer:
                // Posting a layer directly turns it fully on until StopEvent.
                if (layers.TryGetValue(def.name, out Layer layer)) layer.forced = 1f;
                break;
            default:
                if (useWwise) WwisePost(def.name, emitter != null ? emitter : gameObject);
                else PlaySound(def, emitter);
                break;
        }
    }

    private void Stop(string eventName, GameObject emitter, float fadeSeconds)
    {
        if (baseDef != null && baseDef.name == eventName)
        {
            StopMusic(fadeSeconds);
            return;
        }
        if (layers.TryGetValue(eventName, out Layer layer))
        {
            layer.forced = -1f;
            return;
        }
        if (useWwise)
        {
            AkUnitySoundEngine.ExecuteActionOnEvent(eventName, AkActionOnEventType.AkActionOnEventType_Stop,
                emitter != null ? emitter : gameObject, Mathf.RoundToInt(fadeSeconds * 1000f));
            return;
        }
        foreach (Voice v in voices)
        {
            if (v.def.name != eventName || (emitter != null && v.emitter != emitter)) continue;
            v.fadeSpeed = -1f / Mathf.Max(0.01f, fadeSeconds);
        }
    }

    private void StartMusic(AudioEventDef def)
    {
        foreach (Layer l in layers.Values) l.forced = -1f;
        if (musicPlaying && baseDef == def)
        {
            baseFadeTarget = 1f;
            return;
        }

        StopMusicNow();
        baseDef = def;
        musicPlaying = true;
        baseFade = 0f;
        baseFadeTarget = 1f;

        lastBeatTime = -1f;
        if (useWwise)
        {
            // Beat callbacks give the grid that PostEventOnGrid snaps to.
            uint id = AkUnitySoundEngine.PostEvent(def.name, gameObject,
                (uint)AkCallbackType.AK_MusicSyncBeat, OnMusicBeat, null);
            if (id == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                Debug.LogWarning($"[Audio] Wwise could not post '{def.name}'. Is it in a loaded SoundBank?", this);
            return;
        }

        baseSource = MakeMusicSource(def);
        if (baseSource == null) return;

        // Every layer starts on the same DSP tick so they stay locked to the base.
        double startTime = AudioSettings.dspTime + 0.1;
        beatDuration = 60f / bank.bpm;
        lastBeatTime = Time.unscaledTime + 0.1f;
        baseSource.PlayScheduled(startTime);
        foreach (Layer l in layers.Values)
        {
            l.source = MakeMusicSource(l.def);
            if (l.source != null) l.source.PlayScheduled(startTime);
        }
    }

    private void WwisePost(string eventName, GameObject target)
    {
        if (AkUnitySoundEngine.PostEvent(eventName, target) == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            Debug.LogWarning($"[Audio] Wwise could not post '{eventName}'. Is it in a loaded SoundBank? Regenerate banks after changes.", this);
    }

    private void StopMusic(float fadeSeconds)
    {
        if (!musicPlaying) return;
        if (useWwise)
        {
            AkUnitySoundEngine.ExecuteActionOnEvent(baseDef.name, AkActionOnEventType.AkActionOnEventType_Stop,
                gameObject, Mathf.RoundToInt(fadeSeconds * 1000f));
            StopMusicNow();
            return;
        }
        baseFadeTarget = 0f;
    }

    private void StopMusicNow()
    {
        if (baseSource != null) Destroy(baseSource.gameObject);
        baseSource = null;
        foreach (Layer l in layers.Values)
        {
            if (l.source != null) Destroy(l.source.gameObject);
            l.source = null;
            l.weight = 0f;
            l.forced = -1f;
            l.sent = -1f;
        }
        baseDef = null;
        musicPlaying = false;
        lastBeatTime = -1f;
    }

    private void OnMusicBeat(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (info is AkMusicSyncCallbackInfo music && music.segmentInfo_fBeatDuration > 0f)
            beatDuration = music.segmentInfo_fBeatDuration;
        lastBeatTime = Time.unscaledTime;
    }

    private void PostOnGrid(string eventName, GameObject emitter, int division)
    {
        if (lastBeatTime < 0f || beatDuration <= 0f)
        {
            Post(eventName, emitter);
            return;
        }

        float slot = beatDuration / Mathf.Max(1, division);
        float now = Time.unscaledTime;
        float time = lastBeatTime + Mathf.Ceil((now - lastBeatTime) / slot - 0.1f) * slot;
        // One hit per grid slot keeps busy input from cluttering the rhythm.
        foreach (GridHit h in gridHits)
            if (Mathf.Abs(h.time - time) < slot * 0.5f) return;
        if (Mathf.Abs(lastGridHit - time) < slot * 0.5f) return;
        gridHits.Add(new GridHit { time = time, eventName = eventName, emitter = emitter, emitterWasNull = emitter == null });
    }

    private void UpdateGrid()
    {
        float now = Time.unscaledTime;
        for (int i = gridHits.Count - 1; i >= 0; i--)
        {
            GridHit h = gridHits[i];
            if (h.time > now) continue;
            gridHits.RemoveAt(i);
            lastGridHit = h.time;
            if (h.emitter != null || h.emitterWasNull) Post(h.eventName, h.emitter);
        }
    }

    private AudioSource MakeMusicSource(AudioEventDef def)
    {
        AudioClip clip = ClipFor(def);
        if (clip == null) return null;
        var go = new GameObject("Music - " + def.name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;
        src.outputAudioMixerGroup = bank.musicGroup;
        return src;
    }

    private void PlaySound(AudioEventDef def, GameObject emitter)
    {
        AudioClip clip = ClipFor(def);
        if (clip == null) return;

        AudioSource src = pool.Count > 0 ? pool.Pop() : NewPooledSource();
        src.gameObject.SetActive(true);
        src.clip = clip;
        src.loop = def.loop;
        src.pitch = Random.Range(def.pitchRange.x, def.pitchRange.y);
        src.spatialBlend = emitter != null ? def.spatialBlend : 0f;
        src.maxDistance = def.maxDistance;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.outputAudioMixerGroup = bank.soundGroup;
        src.transform.position = emitter != null ? emitter.transform.position : transform.position;

        var voice = new Voice { def = def, source = src, emitter = emitter, followEmitter = emitter != null };
        src.volume = SoundVolume(voice);
        src.Play();
        voices.Add(voice);
    }

    private AudioSource NewPooledSource()
    {
        var go = new GameObject("Sound Voice");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.dopplerLevel = 0f;
        return src;
    }

    private AudioClip ClipFor(AudioEventDef def)
    {
        if (def.clips != null && def.clips.Length > 0)
        {
            AudioClip c = def.clips[Random.Range(0, def.clips.Length)];
            if (c != null) return c;
        }
        if (def.placeholderVoice < 0) return null;
        if (!placeholders.TryGetValue(def.name, out AudioClip clip))
        {
            clip = PlaceholderSynth.Build(def.name, def.placeholderVoice, bank);
            placeholders[def.name] = clip;
        }
        return clip;
    }

    // ---------- Update ----------

    private void Update()
    {
        if (bank == null) return;

        if (listener == null || !listener.gameObject.activeInHierarchy)
        {
            AudioListener l = FindFirstObjectByType<AudioListener>();
            listener = l != null ? l.transform : Camera.main != null ? Camera.main.transform : null;
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + creatureScanInterval;
            ScanCreatures();
        }

        float dt = Time.unscaledDeltaTime;
        UpdateMusic(dt);
        UpdateSounds(dt);
        UpdateGrid();
    }

    private void ScanCreatures()
    {
        creatures.Clear();
        if (bank.creatureChannels.Count == 0) return;
        Creature[] found = FindObjectsByType<Creature>(FindObjectsSortMode.None);
        foreach (Creature c in found)
        {
            string type = c.GetType().Name;
            foreach (CreatureChannel ch in bank.creatureChannels)
                if (ch.creatureType == type) creatures.Add(new CreatureEmitter { creature = c, channel = ch });
        }
    }

    private void UpdateMusic(float dt)
    {
        if (!musicPlaying) return;

        baseFade = Mathf.MoveTowards(baseFade, baseFadeTarget, dt / 2f);
        float musicGain = masterVolume * musicVolume * baseFade;
        if (baseSource != null) baseSource.volume = baseDef.volume * musicGain;

        foreach (Layer l in layers.Values) l.target = 0f;

        if (listener != null)
        {
            Vector3 p = listener.position;
            foreach (MusicChannel ch in channels)
                Raise(ch.layerEvent, ch.Weight(p));
            foreach (CreatureEmitter e in creatures)
            {
                if (e.creature == null || !e.creature.isActiveAndEnabled) continue;
                float d = Vector3.Distance(p, e.creature.transform.position);
                Raise(e.channel.layerEvent, Falloff(d, e.channel.innerRadius, e.channel.outerRadius));
            }
        }

        foreach (Layer l in layers.Values)
        {
            float target = l.forced >= 0f ? l.forced : l.target;
            float time = target > l.weight ? l.def.fadeIn : l.def.fadeOut;
            l.weight = Mathf.MoveTowards(l.weight, target, dt / time);

            if (useWwise)
            {
                if (Mathf.Abs(l.weight - l.sent) > 0.002f)
                {
                    l.sent = l.weight;
                    AkUnitySoundEngine.SetRTPCValue(l.def.name, l.weight * 100f);
                }
            }
            else if (l.source != null)
            {
                // Equal power curve so fades don't dip in the middle.
                l.source.volume = l.def.volume * Mathf.Sin(l.weight * Mathf.PI * 0.5f) * musicGain;
            }
        }

        if (baseFadeTarget <= 0f && baseFade <= 0f) StopMusicNow();
    }

    private void Raise(string layerEvent, float w)
    {
        if (w > 0f && layers.TryGetValue(layerEvent, out Layer l)) l.target = Mathf.Max(l.target, w);
    }

    public static float Falloff(float d, float inner, float outer)
    {
        if (d <= inner) return 1f;
        if (d >= outer) return 0f;
        return 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / Mathf.Max(0.01f, outer - inner));
    }

    private void UpdateSounds(float dt)
    {
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            Voice v = voices[i];
            if (v.followEmitter)
            {
                if (v.emitter == null) v.fadeSpeed = Mathf.Min(v.fadeSpeed, -4f);
                else v.source.transform.position = v.emitter.transform.position;
            }

            v.fade = Mathf.Clamp01(v.fade + v.fadeSpeed * dt);
            v.source.volume = SoundVolume(v);

            if (v.fade <= 0f || !v.source.isPlaying)
            {
                v.source.Stop();
                v.source.clip = null;
                v.source.gameObject.SetActive(false);
                pool.Push(v.source);
                voices.RemoveAt(i);
            }
        }
    }

    private float SoundVolume(Voice v) => v.def.volume * v.fade * masterVolume * soundVolume;

    private void OnGUI()
    {
        if (!showDebug || !musicPlaying) return;
        var r = new Rect(10, 10, 300, 20);
        GUI.Label(r, $"[{(useWwise ? "Wwise" : "Unity")}] {baseDef.name}");
        foreach (Layer l in layers.Values)
        {
            r.y += 18;
            GUI.Label(r, $"{l.def.name}  {l.weight:0.00}");
        }
    }
}
