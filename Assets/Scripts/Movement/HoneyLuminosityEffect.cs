using UnityEngine;

/// <summary>A fading honey contribution shared by players, creatures and coated scenery.</summary>
[DefaultExecutionOrder(-100)]
public sealed class HoneyLuminosityEffect : MonoBehaviour, ICreatureStats
{
    [SerializeField, Min(0f)] private float amount = 50f;
    [SerializeField, Min(0f)] private float remaining;
    [SerializeField, Min(0.01f)] private float duration = 60f;
    [SerializeField, HideInInspector] private float appliedAmount;
    private ICreatureStats stats;
    private SmoothFirstPersonController controller;
    private Light lamp;
    private Vector3 localEmission;
    public bool Ending { get; private set; }
    public float Remaining => remaining;
    public bool IsActive => isActiveAndEnabled && !Ending && remaining > 0f;
    public float Luminosity => IsActive ? amount * Mathf.Clamp01(remaining / duration) : 0f;
    public Vector3 EmissionPosition => transform.TransformPoint(localEmission);
    public Transform StatRoot => transform;
    public bool IsAvailableForPerception => IsActive && stats == null;
    public void SetLuminosity(float value) => throw new System.NotSupportedException("Honey brightness follows its remaining duration.");

    private void OnEnable()
    {
        stats = GetComponent<Creature>();
        if (stats == null) stats = GetComponent<PlayerCreatureStats>();
        controller = GetComponent<SmoothFirstPersonController>();
        if (stats == null) LuminosityQuery.Register(this);
    }

    public static HoneyLuminosityEffect Apply(PlayerCreatureStats target, float brightness, float seconds)
        => Apply(target.gameObject, brightness, seconds, target.EmissionPosition);

    // Species with their own changing emission replace their native amount, retaining the coating.
    public static void SetNativeLuminosity(Creature target, float brightness)
    {
        float coating = target.TryGetComponent<HoneyLuminosityEffect>(out var effect) ? effect.appliedAmount : 0f;
        target.SetLuminosity(brightness + coating);
    }

    public static HoneyLuminosityEffect Apply(GameObject target, float brightness, float seconds, Vector3 point)
    {
        if (float.IsNaN(brightness) || float.IsInfinity(brightness) || brightness < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(brightness));
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        HoneyLuminosityEffect effect = null;
        foreach (var existing in target.GetComponents<HoneyLuminosityEffect>())
            if (!existing.Ending) { effect = existing; break; }
        if (effect == null) effect = target.AddComponent<HoneyLuminosityEffect>();
        effect.RemoveAmount();
        effect.amount = brightness;
        effect.remaining = effect.duration = Mathf.Max(effect.remaining, seconds);
        effect.localEmission = target.transform.InverseTransformPoint(point);
        effect.enabled = true;
        effect.EnsureLight();
        effect.SyncAmount();
        return effect;
    }

    private void EnsureLight()
    {
        // Native creature/player lights already read the shared stat, including honey.
        if (lamp != null || GetComponent<PlayerLuminosityVisuals>() != null ||
            GetComponentInChildren<GlowBeeLight>(true) != null) return;
        var root = new GameObject("Honey coating light");
        root.transform.SetParent(transform, false);
        lamp = root.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = PlayerLuminosityVisuals.Tint;
        lamp.range = 5f;
        lamp.shadows = LightShadows.None;
    }

    private void Update()
    {
        if (controller == null || controller.enabled) AdvanceTime(Time.deltaTime);
        else SyncAmount(); // Preserve the existing meditation pause.
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
        bool visible = IsActive && (controller == null || controller.enabled);
        if (stats is Creature creature) visible &= creature.IsAvailableForPerception;
        if (stats is PlayerCreatureStats player) visible &= player.isActiveAndEnabled;
        float desired = visible ? Luminosity : 0f;
        if (stats is Object owner && owner != null)
        {
            stats.SetLuminosity(Mathf.Max(0f, stats.Luminosity - appliedAmount + desired));
            appliedAmount = desired;
        }
        if (lamp != null)
        {
            lamp.transform.localPosition = localEmission;
            lamp.intensity = LuminosityLighting.PointIntensity(desired);
            lamp.enabled = desired > 0f;
        }
        GetComponent<PlayerLuminosityVisuals>()?.Refresh();
    }

    private void RemoveAmount()
    {
        if (appliedAmount > 0f && stats is Object owner && owner != null)
            stats.SetLuminosity(Mathf.Max(0f, stats.Luminosity - appliedAmount));
        appliedAmount = 0f;
        if (lamp != null) lamp.enabled = false;
        GetComponent<PlayerLuminosityVisuals>()?.Refresh();
    }

    public void Cancel()
    {
        if (Ending) return;
        Ending = true;
        remaining = 0f;
        LuminosityQuery.Unregister(this);
        RemoveAmount();
        if (lamp != null) Destroy(lamp.gameObject);
        Destroy(this);
    }
    private void OnDisable() { LuminosityQuery.Unregister(this); RemoveAmount(); }
    private void OnDestroy()
    {
        LuminosityQuery.Unregister(this);
        RemoveAmount();
        if (lamp != null) Destroy(lamp.gameObject);
    }
}
