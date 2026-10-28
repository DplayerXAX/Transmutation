using UnityEngine;

/// <summary>Adds honey light to the one stored stat, then subtracts its own amount and removes itself.</summary>
[RequireComponent(typeof(PlayerCreatureStats))]
[DefaultExecutionOrder(-100)]
public sealed class HoneyLuminosityEffect : MonoBehaviour
{
    [SerializeField, Min(0f)] private float amount = 50f;
    [SerializeField, Min(0f)] private float remaining;
    private PlayerCreatureStats stats;
    private SmoothFirstPersonController controller;
    [SerializeField, HideInInspector] private bool applied;
    public bool Ending { get; private set; }
    public float Remaining => remaining;
    public bool IsActive => isActiveAndEnabled && !Ending && remaining > 0f;

    private void OnEnable()
    {
        stats = GetComponent<PlayerCreatureStats>();
        controller = GetComponent<SmoothFirstPersonController>();
    }

    public static HoneyLuminosityEffect Apply(PlayerCreatureStats target, float brightness, float seconds)
    {
        if (float.IsNaN(brightness) || float.IsInfinity(brightness) || brightness < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(brightness));
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        HoneyLuminosityEffect effect = null;
        foreach (var existing in target.GetComponents<HoneyLuminosityEffect>())
            if (!existing.Ending) { effect = existing; break; }
        if (effect == null) effect = target.gameObject.AddComponent<HoneyLuminosityEffect>();
        effect.RemoveAmount();
        effect.amount = brightness;
        effect.remaining = Mathf.Max(effect.remaining, seconds);
        effect.enabled = true;
        effect.SyncAmount();
        return effect;
    }

    private void Update()
    {
        if (controller == null || controller.enabled) AdvanceTime(Time.deltaTime);
        else SyncAmount(); // Meditation pauses the timer and temporarily removes the glow.
    }

    public void AdvanceTime(float seconds)
    {
        if (Ending) return;
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, seconds));
        if (remaining <= 0f) Cancel();
        else SyncAmount();
    }

    private void SyncAmount()
    {
        bool shouldApply = IsActive && stats != null && stats.isActiveAndEnabled &&
            (controller == null || controller.enabled);
        if (!shouldApply) { RemoveAmount(); return; }
        if (applied) return;
        stats.SetLuminosity(stats.Luminosity + amount);
        applied = true;
    }

    private void RemoveAmount()
    {
        if (applied && stats != null) stats.SetLuminosity(Mathf.Max(0f, stats.Luminosity - amount));
        applied = false;
    }

    public void Cancel()
    {
        if (Ending) return;
        Ending = true;
        remaining = 0f;
        RemoveAmount();
        Destroy(this);
    }
    private void OnDisable() => RemoveAmount();
    private void OnDestroy() => RemoveAmount();
}
