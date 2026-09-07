using UnityEngine;

/// <summary>
/// First structural draft of the Fire Flower creature.
///
/// Intended behavior:
/// 1. Receive or sense Heat.
/// 2. Bloom after enough Heat has accumulated.
/// 3. Release Bubble creatures.
/// 4. Return to a closed state.
///
/// Heat transfer and Bubble spawning are implemented at a basic level.
/// Bloom animation, Shader control, and Bubble-specific behavior remain intentionally minimal.
/// </summary>
public sealed class FireFlower : Creature
{
    /// <summary>
    /// High-level stages of the Fire Flower's behavior.
    /// These names map the planned structure without defining final timings.
    /// </summary>
    public enum FireFlowerState
    {
        Closed,
        Heating,
        Blooming,
        Releasing,
        Recovering
    }

    [Header("Heat")]
    [Tooltip("Heat required before the flower can begin blooming.")]
    [Min(0.01f)]
    [SerializeField] private float bloomHeatThreshold = 70f;

    [Header("Bloom")]
    [Tooltip("Approximate time used by the future bloom transition.")]
    [SerializeField] private float bloomDuration = 1f;

    [Tooltip("Approximate recovery time before the flower can bloom again.")]
    [SerializeField] private float recoveryDuration = 1f;

    [Header("Bubble Output")]
    [Tooltip("Future Bubble creature prefab produced when this flower blooms.")]
    [SerializeField] private FireBubble bubblePrefab;

    [Tooltip("Transform that determines where every Bubble is released. Uses the Fire Flower position when left empty.")]
    [SerializeField] private Transform bubbleSpawnPoint;

    [Tooltip("Maximum number of Bubble creatures released by one bloom.")]
    [Min(0)]
    [SerializeField] private int bubbleCount = 3;

    [Header("Runtime State (Read Only)")]
    [Tooltip("Current high-level state of this Fire Flower.")]
    [SerializeField] private FireFlowerState currentState = FireFlowerState.Closed;

    [Tooltip("Time spent in the current state.")]
    [SerializeField] private float stateTime;

    [Tooltip("Normalized 0–1 value intended for animation or Shader presentation.")]
    [Range(0f, 1f)]
    [SerializeField] private float bloomAmount;

    /// <summary>Current high-level state, exposed read-only to other systems.</summary>
    public FireFlowerState CurrentState => currentState;

    /// <summary>Current normalized bloom amount for presentation systems.</summary>
    public float BloomAmount => bloomAmount;

    /// <summary>
    /// Establishes the Fire Flower's initial runtime state.
    /// </summary>
    protected override void InitializeCreature()
    {
        ChangeState(FireFlowerState.Closed);
    }

    /// <summary>
    /// Maps the intended order of Fire Flower behavior each frame.
    /// The called functions are placeholders for later implementation.
    /// </summary>
    protected override void TickCreature(float deltaTime)
    {
        SenseHeat(deltaTime);
        UpdateState(deltaTime);
        UpdatePresentation(deltaTime);
    }

    /// <summary>
    /// Heat is supplied by FireSource through Creature.AddHeat.
    /// This hook remains separate so environmental sensing can be added later if needed.
    /// </summary>
    private void SenseHeat(float deltaTime)
    {
        // No polling is needed in the current trigger-based Heat implementation.
    }

    /// <summary>
    /// Routes the current state to its corresponding behavior function.
    /// Detailed transition rules will be added after the first visual prototype.
    /// </summary>
    private void UpdateState(float deltaTime)
    {
        stateTime += deltaTime;

        switch (currentState)
        {
            case FireFlowerState.Closed:
                TickClosed();
                break;

            case FireFlowerState.Heating:
                TickHeating();
                break;

            case FireFlowerState.Blooming:
                TickBlooming(deltaTime);
                break;

            case FireFlowerState.Releasing:
                TickReleasing();
                break;

            case FireFlowerState.Recovering:
                TickRecovering(deltaTime);
                break;
        }
    }

    /// <summary>
    /// Future behavior while the flower is fully closed.
    /// Expected responsibility: detect the beginning of meaningful heating.
    /// </summary>
    private void TickClosed()
    {
        if (Heat > 0f)
        {
            ChangeState(FireFlowerState.Heating);
        }
    }

    /// <summary>
    /// Future behavior while Heat is accumulating.
    /// Expected responsibility: enter Blooming at bloomHeatThreshold.
    /// </summary>
    private void TickHeating()
    {
        if (Heat >= bloomHeatThreshold)
        {
            Bloom();
        }
        else if (Heat <= 0f)
        {
            ChangeState(FireFlowerState.Closed);
        }
    }

    /// <summary>
    /// Future timed opening behavior.
    /// Expected responsibility: increase bloomAmount and enter Releasing.
    /// </summary>
    private void TickBlooming(float deltaTime)
    {
        float safeDuration = Mathf.Max(0.01f, bloomDuration);
        bloomAmount = Mathf.Clamp01(stateTime / safeDuration);

        if (stateTime >= safeDuration)
        {
            ChangeState(FireFlowerState.Releasing);
        }
    }

    /// <summary>
    /// Future release behavior.
    /// Expected responsibility: spawn bubbles once, spend Heat, then recover.
    /// </summary>
    private void TickReleasing()
    {
        ReleaseBubbles();
        ChangeState(FireFlowerState.Recovering);
    }

    /// <summary>
    /// Future closing and recovery behavior.
    /// Expected responsibility: return bloomAmount to 0 before becoming Closed.
    /// </summary>
    private void TickRecovering(float deltaTime)
    {
        float safeDuration = Mathf.Max(0.01f, recoveryDuration);
        bloomAmount = 1f - Mathf.Clamp01(stateTime / safeDuration);

        if (stateTime >= safeDuration)
        {
            ChangeState(FireFlowerState.Closed);
        }
    }

    /// <summary>
    /// Future factory function for Bubble creatures.
    /// Spawn positions and initial Heat transfer will be handled here.
    /// </summary>
    private void ReleaseBubbles()
    {
        if (bubblePrefab == null || bubbleCount <= 0)
        {
            Debug.LogWarning($"{name} cannot release bubbles because no Bubble prefab or valid Bubble Count is assigned.", this);
            return;
        }

        // The Heat that triggered this bloom is conserved and moved into the bubbles.
        float transferredHeat = SpendHeat(bloomHeatThreshold);

        if (transferredHeat <= 0f)
        {
            return;
        }

        float heatPerBubble = transferredHeat / bubbleCount;

        for (int index = 0; index < bubbleCount; index++)
        {
            Vector3 spawnPosition = bubbleSpawnPoint != null
                ? bubbleSpawnPoint.position
                : transform.position;

            FireBubble bubble = Instantiate(bubblePrefab, spawnPosition, Quaternion.identity);
            bubble.AddHeat(heatPerBubble);
        }
    }

    /// <summary>
    /// Starts the bloom sequence once the flower reaches its Heat threshold.
    /// The stored Heat is not spent until bubbles are actually released.
    /// </summary>
    private void Bloom()
    {
        ChangeState(FireFlowerState.Blooming);
    }

    /// <summary>
    /// Changes the high-level state and resets its elapsed time.
    /// State-entry events can be added here later.
    /// </summary>
    private void ChangeState(FireFlowerState nextState)
    {
        currentState = nextState;
        stateTime = 0f;
    }

    /// <summary>
    /// Future bridge from simulation state to Shader, animation, and sound.
    /// Keeping this separate prevents visual code from controlling creature rules.
    /// </summary>
    private void UpdatePresentation(float deltaTime)
    {
        // TODO: Send bloomAmount and normalized Heat to the visual presentation.
    }

    /// <summary>
    /// Future direct player interaction entry for this specific creature.
    /// It currently inherits the interaction without changing flower state.
    /// </summary>
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        base.ReceiveInteraction(interaction);

        // TODO: Decide whether direct player interaction affects the Fire Flower.
    }

    /// <summary>
    /// Future creature-to-creature interaction entry.
    /// </summary>
    public override void MeetCreature(Creature other)
    {
        base.MeetCreature(other);

        // TODO: Decide how the Fire Flower responds to contact with other creatures.
    }
}
