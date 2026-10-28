using UnityEngine;

/// <summary>Temporary loss of sight, independent of movement, hearing and creature simulation.</summary>
[DisallowMultipleComponent]
public sealed class FlashBlindness : MonoBehaviour
{
    [SerializeField, Min(0f)] private float remaining;
    private Creature owner;
    public float Remaining => remaining;
    public static bool IsBlinded(Component observer) => observer != null &&
        observer.TryGetComponent<FlashBlindness>(out var effect) && effect.isActiveAndEnabled && effect.remaining > 0f;

    public static void Apply(Creature target, float seconds)
    {
        if (target == null || !target.IsAvailableForPerception || seconds <= 0f) return;
        if (!target.TryGetComponent<FlashBlindness>(out var effect)) effect = target.gameObject.AddComponent<FlashBlindness>();
        effect.owner = target;
        effect.remaining = Mathf.Max(effect.remaining, seconds);
        effect.enabled = true;
    }

    private void Update()
    {
        if (owner == null || owner.IsDead) { remaining = 0f; enabled = false; return; }
        if (owner.SimulationEnabled) Advance(Time.deltaTime);
    }

    public void Advance(float seconds)
    {
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, seconds));
        if (remaining <= 0f) enabled = false;
    }

    private void OnDisable() => remaining = 0f;
}
