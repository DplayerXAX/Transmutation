using UnityEngine;

/// <summary>
/// Marks a collider as too smooth for bare hands: HandClimber can grab it, but the hands slide down
/// until they reach rough stone again (or a ledge top). A carried tentacle creature (StickyHands) holds them in place.
/// </summary>
[DisallowMultipleComponent]
public sealed class SlipperySurface : MonoBehaviour
{
    [Tooltip("Share of the climber's slide speed on this surface (1 = full).")]
    [Range(0f, 1f)] public float speedScale = 1f;
    [Tooltip("Slide faster and faster while on it (off = a steady creep that climbing can outpace).")]
    public bool accelerate = true;
}
