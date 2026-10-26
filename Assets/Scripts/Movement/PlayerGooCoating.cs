using System;
using UnityEngine;

/// <summary>Session-local supply consumed by this player's hand climbing.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SmoothFirstPersonController))]
[DefaultExecutionOrder(-100)]
public sealed class PlayerGooCoating : MonoBehaviour
{
    [Min(0.01f)] [SerializeField] private float secondsPerCreature = 20f;
    [Min(1f)] [SerializeField] private float capacityMultiplier = 1.5f;
    private float remainingSeconds;
    private readonly System.Collections.Generic.List<HandClimber> handClimbers = new System.Collections.Generic.List<HandClimber>();

    public float RemainingSeconds => remainingSeconds;
    public float SecondsPerCreature => secondsPerCreature;
    public float MaximumSeconds => secondsPerCreature * capacityMultiplier;
    public bool CanClimb => isActiveAndEnabled && remainingSeconds > 0f;
    public float Coverage => Mathf.Clamp01(remainingSeconds / secondsPerCreature);
    public float ExtraThickness => Mathf.InverseLerp(secondsPerCreature, MaximumSeconds, remainingSeconds);
    public event Action<float> SupplyChanged;

    private void Awake()
    {
        RefreshClimberReferences();
    }

    public void RefreshClimberReferences()
    {
        handClimbers.Clear();
        handClimbers.AddRange(transform.root.GetComponentsInChildren<HandClimber>(true));
    }

    internal void RegisterClimber(HandClimber climber)
    {
        if (!handClimbers.Contains(climber)) handClimbers.Add(climber);
    }

    /// <summary>The player's coating, from whatever object started an interaction (camera, carrier or player).</summary>
    public static PlayerGooCoating FindFor(GameObject source)
    {
        if (source == null) return null;
        PlayerGooCoating coating = source.GetComponentInParent<PlayerGooCoating>();
        if (coating != null) return coating;
        // The prefab's CameraHolder is a sibling of Player, not its child.
        PlayerCreatureCarrier carrier = source.GetComponentInParent<PlayerCreatureCarrier>();
        if (carrier == null) carrier = source.transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
        return carrier != null && carrier.PlayerController != null ? carrier.PlayerController.GetComponent<PlayerGooCoating>() : null;
    }

    public bool TryApplyGoo()
    {
        if (!isActiveAndEnabled || remainingSeconds >= MaximumSeconds) return false;
        SetRemaining(Mathf.Min(MaximumSeconds, remainingSeconds + secondsPerCreature));
        return true;
    }

    private void Update() => AdvanceAttachedTime(Time.deltaTime);

    // One owner advances time; traversal components never spend supply themselves.
    internal void AdvanceAttachedTime(float deltaTime)
    {
        if (!CanClimb || deltaTime <= 0f) return;
        float rate = 0f;
        var player = GetComponent<SmoothFirstPersonController>();
        foreach (HandClimber climber in handClimbers)
            // Hands on a goo splat use none of the coating; fast climbing (Shift) uses it faster.
            if (climber != null && climber.isActiveAndEnabled && climber.Controls(player) && climber.IsClimbing && !climber.OnGooPaint)
                rate = Mathf.Max(rate, climber.GooDrainRate);
        if (rate <= 0f) return;
        SetRemaining(Mathf.Max(0f, remainingSeconds - deltaTime * rate));
        if (remainingSeconds <= 0f) ReleaseAttachments();
    }

    private void SetRemaining(float seconds)
    {
        remainingSeconds = seconds;
        SupplyChanged?.Invoke(remainingSeconds);
    }

    private void ReleaseAttachments()
    {
        var player = GetComponent<SmoothFirstPersonController>();
        foreach (HandClimber climber in handClimbers)
            if (climber != null && climber.Controls(player)) climber.StopForGooDepletion();
    }

    private void OnDisable() => ReleaseAttachments();

    private void OnValidate()
    {
        secondsPerCreature = Mathf.Max(0.01f, secondsPerCreature);
        capacityMultiplier = Mathf.Max(1f, capacityMultiplier);
        remainingSeconds = Mathf.Clamp(remainingSeconds, 0f, MaximumSeconds);
    }
}
