using UnityEngine;

/// <summary>
/// Tutorial music, played through Wwise (Play_Tutorial). The stems intro_1..5 loop together and
/// each fades in when its tutorial step is unlocked (they stack up). Finish lets the current loop
/// end, plays the outro (last 3.75 s of intro_loop), then brings the game music back.
/// </summary>
public sealed class TutorialMusic : MonoBehaviour
{
    public static TutorialMusic Instance { get; private set; }

    [SerializeField] private string tutorialEvent = "Play_Tutorial";
    [Tooltip("Game Parameters (0..100) that fade the stems in, one per tutorial step.")]
    [SerializeField] private string[] stemParameters = { "Tutorial_1", "Tutorial_2", "Tutorial_3", "Tutorial_4", "Tutorial_5" };
    [Tooltip("Main music event stopped during the tutorial and started again at the end. Empty = leave it.")]
    [SerializeField] private string mainMusicEvent = "PlayBGM";
    [SerializeField] private bool playOnStart;
    [Min(0f)] [SerializeField] private float fadeSeconds = 2f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool playing;
    [SerializeField] private bool finishing;
    [SerializeField] private int unlocked;

    private uint playingId;

    public bool Playing => playing;
    public int Unlocked => unlocked;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (playOnStart) Begin();
    }

    /// <summary>Starts the stems, all silent. Call when the tutorial starts.</summary>
    public void Begin()
    {
        if (playing) return;
        if (AudioManager.Instance == null || !AudioManager.Instance.UsingWwise)
        {
            Debug.LogWarning("[Audio] TutorialMusic needs the AudioManager running on Wwise.", this);
            return;
        }

        if (!string.IsNullOrEmpty(mainMusicEvent)) AudioManager.StopEvent(mainMusicEvent, null, fadeSeconds);
        unlocked = 0;
        SetStems(0);
        playingId = AkUnitySoundEngine.PostEvent(tutorialEvent, gameObject, (uint)AkCallbackType.AK_EndOfEvent, OnTutorialEnd, null);
        if (playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            Debug.LogWarning($"[Audio] Wwise could not post '{tutorialEvent}'. Is it in a loaded SoundBank?", this);
            return;
        }
        playing = true;
        finishing = false;
    }

    /// <summary>Unlocks tutorial steps 1..stage: their stems fade in (0 = all silent).</summary>
    public void Unlock(int stage)
    {
        if (!playing) Begin();
        unlocked = Mathf.Clamp(stage, 0, stemParameters.Length);
        SetStems(fadeSeconds);
    }

    public void UnlockNext() => Unlock(unlocked + 1);

    /// <summary>Lets the current loop finish, plays the outro, then the game music returns.</summary>
    public void Finish()
    {
        if (!playing || finishing) return;
        finishing = true;
        // Break leaves the looping stems at the end of the loop and moves on to the outro.
        AkUnitySoundEngine.ExecuteActionOnEvent(tutorialEvent, AkActionOnEventType.AkActionOnEventType_Break, gameObject, 0);
    }

    /// <summary>Stops the tutorial music right away (with a fade) and brings the game music back.</summary>
    public void End()
    {
        if (!playing) return;
        AkUnitySoundEngine.ExecuteActionOnEvent(tutorialEvent, AkActionOnEventType.AkActionOnEventType_Stop,
            gameObject, Mathf.RoundToInt(fadeSeconds * 1000f));
        Ended();
    }

    private void OnTutorialEnd(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (playing) Ended();
    }

    private void Ended()
    {
        playing = false;
        finishing = false;
        unlocked = 0;
        if (!string.IsNullOrEmpty(mainMusicEvent)) AudioManager.PostEvent(mainMusicEvent);
    }

    private void SetStems(float seconds)
    {
        int ms = Mathf.RoundToInt(seconds * 1000f);
        for (int i = 0; i < stemParameters.Length; i++)
            AkUnitySoundEngine.SetRTPCValue(stemParameters[i], i < unlocked ? 100f : 0f, AkUnitySoundEngine.AK_INVALID_GAME_OBJECT, ms);
    }
}
