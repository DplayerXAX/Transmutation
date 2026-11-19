using UnityEngine;

/// <summary>
/// Runs an ErosiveFluidSystem only where it can matter: the player must be near its springs (no further than the water
/// can be seen, the system's visual distance) and above the cave system.
/// Otherwise the springs and rain stop and the water already flowing is cleared, so water out of sight costs no physics
/// and no terrain rebuilding. If frames stay very slow while water is flowing, it is also paused for a while.
/// </summary>
[RequireComponent(typeof(ErosiveFluidSystem))]
public sealed class FluidNearPlayer : MonoBehaviour
{
    [Tooltip("The water runs while the player is within this distance of any spring (metres).")]
    [Min(10f)] public float activeRange = 70f;
    [Tooltip("Extra distance before it stops again, so it does not flicker at the edge.")]
    [Min(0f)] public float margin = 15f;
    [Tooltip("The water stops when the player goes below this height (the dark caves).")]
    public float undergroundBelow = -30f;
    [Tooltip("The water may run again once the player is back above this height.")]
    public float surfaceAbove = -22f;

    private const float SlowFrame = 0.12f, SlowHold = 2f, SlowPause = 30f, GuardStartsAfter = 20f;

    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource[] springs;
    private Transform player;
    private bool running = true, underground, hasCaves;
    private float nextCheck, averageFrame, slowFor, pausedUntil;

    public bool Running => running;

    private void Awake()
    {
        fluid = GetComponent<ErosiveFluidSystem>();
        springs = GetComponentsInChildren<ErosiveFluidSource>();
    }

    // Only the combined world has the cave system; other scenes keep the distance rule alone.
    // Asked after loading, because the scene's root objects cannot be listed during Awake.
    private void Start() => hasCaves = WorldStreaming.FindRoot(gameObject.scene) != null;

    private void Update()
    {
        WatchFrameTime();
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (player == null)
        {
            var controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller == null) return;
            player = controller.transform;
        }
        float nearest = float.MaxValue;
        foreach (ErosiveFluidSource s in springs) if (s != null) nearest = Mathf.Min(nearest, (s.transform.position - player.position).magnitude);
        underground = hasCaves && (underground ? player.position.y < surfaceAbove : player.position.y < undergroundBelow);
        // Never further than the water can be seen.
        float range = Mathf.Min(activeRange, fluid.VisualDistance + 10f);
        bool inRange = running ? nearest < range + margin : nearest < range;
        SetRunning(inRange && !underground && Time.time >= pausedUntil);
    }

    // A sustained slump with water in play: clear it first, since it is the heaviest thing that can safely go.
    private void WatchFrameTime()
    {
        float frame = Time.unscaledDeltaTime;
        averageFrame = averageFrame <= 0f ? frame : Mathf.Lerp(averageFrame, frame, 0.1f);
        slowFor = Time.timeSinceLevelLoad > GuardStartsAfter && averageFrame > SlowFrame ? slowFor + frame : 0f;
        if (slowFor < SlowHold || !running || fluid.ActiveCount == 0) return;
        slowFor = 0f;
        pausedUntil = Time.time + SlowPause;
        SetRunning(false);
#if UNITY_EDITOR
        Debug.Log($"Water paused for {SlowPause:0} s: frames averaged {averageFrame * 1000f:0} ms.");
#endif
    }

    private void SetRunning(bool want)
    {
        if (want == running) return;
        running = want;
        fluid.SetEmitting(want);
        if (!want) fluid.ClearDroplets();
    }
}
