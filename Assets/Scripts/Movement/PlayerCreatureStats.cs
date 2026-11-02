using UnityEngine;

/// <summary>The player's shared stats, without creature food, combat or lifespan membership.</summary>
[DisallowMultipleComponent]
public sealed class PlayerCreatureStats : MonoBehaviour, ICreatureStats
{
    [Tooltip("Current emitted brightness. Effects change this number directly.")]
    [Min(0f)] [SerializeField] private float luminosity;
    [SerializeField] private Vector3 emissionOffset = Vector3.up * 0.5f;

    public Transform StatRoot => transform;
    public Vector3 EmissionPosition => transform.TransformPoint(emissionOffset);
    public bool IsAvailableForPerception => isActiveAndEnabled;
    public float Luminosity => luminosity;

    public void SetLuminosity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new System.ArgumentOutOfRangeException(nameof(value));
        luminosity = value;
    }

    private void OnEnable() { if (Application.isPlaying) LuminosityQuery.Register(this); }
    private void OnDisable() => LuminosityQuery.Unregister(this);
}
