using UnityEngine;

/// <summary>
/// Temporary stronger jumps from eating Mushroom Essence. Sets the controller's jump multiplier while it
/// lasts and puts it back when it ends. Eating more refreshes the time; the strongest multiplier wins.
/// </summary>
[RequireComponent(typeof(SmoothFirstPersonController))]
[DisallowMultipleComponent]
public sealed class JumpBoostEffect : MonoBehaviour
{
    [SerializeField, Min(1f)] private float multiplier = 1f;
    [SerializeField, Min(0f)] private float remaining;
    private SmoothFirstPersonController controller;

    public float Multiplier => multiplier;
    public float Remaining => remaining;

    public static JumpBoostEffect Apply(SmoothFirstPersonController target, float jumpMultiplier, float seconds)
    {
        if (float.IsNaN(jumpMultiplier) || float.IsInfinity(jumpMultiplier) || jumpMultiplier < 1f)
            throw new System.ArgumentOutOfRangeException(nameof(jumpMultiplier));
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        var effect = target.GetComponent<JumpBoostEffect>();
        if (effect == null) effect = target.gameObject.AddComponent<JumpBoostEffect>();
        effect.multiplier = Mathf.Max(effect.remaining > 0f ? effect.multiplier : 1f, jumpMultiplier);
        effect.remaining = Mathf.Max(effect.remaining, seconds);
        effect.Sync();
        return effect;
    }

    private void OnEnable() => controller = GetComponent<SmoothFirstPersonController>();

    private void Update()
    {
        // Paused while the controller is off (meditation, cutscenes), like the honey effects.
        if (controller != null && !controller.enabled) return;
        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        if (remaining <= 0f) Destroy(this);
    }

    private void Sync()
    {
        if (controller == null) controller = GetComponent<SmoothFirstPersonController>();
        if (controller != null) controller.JumpMultiplier = remaining > 0f ? multiplier : 1f;
    }

    private void OnDestroy()
    {
        if (controller != null) controller.JumpMultiplier = 1f;
    }
}
