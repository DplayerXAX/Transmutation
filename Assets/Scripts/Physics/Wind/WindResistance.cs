using UnityEngine;

/// <summary>Wind resistance for a loose prop or any body that is neither a creature nor the player.</summary>
[DisallowMultipleComponent]
public sealed class WindResistance : MonoBehaviour, IWindResistant
{
    [Tooltip("0 = carried fully by the wind, 1 = immune.")]
    [Range(0f, 1f)] [SerializeField] private float resistance;

    float IWindResistant.WindResistance => resistance;

    public float Value { get => resistance; set => resistance = Mathf.Clamp01(value); }
}
