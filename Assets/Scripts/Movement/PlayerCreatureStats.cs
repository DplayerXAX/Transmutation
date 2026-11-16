using UnityEngine;

/// <summary>Player stats and temporary Asura coat, outside the creature lifecycle.</summary>
[DisallowMultipleComponent]
public sealed class PlayerCreatureStats : MonoBehaviour, ICreatureStats, ICombatTarget
{
    [Tooltip("Current emitted brightness. Effects change this number directly.")]
    [Min(0f)] [SerializeField] private float luminosity;
    [SerializeField] private Vector3 emissionOffset = Vector3.up * 0.5f;

    public Transform StatRoot => transform;
    public Vector3 EmissionPosition => transform.TransformPoint(emissionOffset);
    public bool IsAvailableForPerception => isActiveAndEnabled;
    public float Luminosity => luminosity;

    public float CoatHealth { get; private set; }
    public CreaturePolarity Polarity { get; private set; } = CreaturePolarity.None;
    public event System.Action<float, CreaturePolarity> CoatChanged;
    public Component TargetComponent => this;
    public Transform CombatRoot => body != null ? body.transform : transform;
    public Vector3 AimPosition => body != null ? body.worldCenterOfMass : EmissionPosition;
    public bool IsCombatActive => isActiveAndEnabled && CombatRoot.gameObject.activeInHierarchy;
    public bool CanBeAITargeted => IsCombatActive && CoatHealth > 0f && Polarity != CreaturePolarity.None;
    private Rigidbody body;

    private void Awake() => body = GetComponentInParent<Rigidbody>();

    public bool TryApplyBlood(CreaturePolarity donor)
    {
        if (!IsCombatActive || (donor != CreaturePolarity.Light && donor != CreaturePolarity.Dark) ||
            (donor == Polarity && CoatHealth >= 100f)) return false;
        CommitCoat(donor == Polarity ? Mathf.Min(100f, CoatHealth + 50f) : 50f, donor);
        return true;
    }

    public bool ApplyDamage(float amount, Creature source = null)
    {
        if (!IsCombatActive || CoatHealth <= 0f || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return false;
        float next = Mathf.Max(0f, CoatHealth - amount);
        CommitCoat(next, next > 0f ? Polarity : CreaturePolarity.None);
        return true;
    }

    private void CommitCoat(float health, CreaturePolarity polarity)
    {
        CoatHealth = health;
        Polarity = polarity;
        if (CoatChanged == null) return;
        foreach (System.Action<float, CreaturePolarity> listener in CoatChanged.GetInvocationList())
            try { listener(CoatHealth, Polarity); }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
    }

    public void SetLuminosity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(value));
        luminosity = value;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        LuminosityQuery.Register(this);
        CreatureRegistry.Register(this);
    }
    private void OnDisable()
    {
        LuminosityQuery.Unregister(this);
        CreatureRegistry.Unregister(this);
    }
}
