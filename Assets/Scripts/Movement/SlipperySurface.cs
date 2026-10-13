using UnityEngine;

/// <summary>
/// Marks a collider as too smooth for bare hands: HandClimber can grab it, but the hands slide down
/// until they reach rough stone again. A carried tentacle creature (StickyHands) holds them in place.
/// </summary>
[DisallowMultipleComponent]
public sealed class SlipperySurface : MonoBehaviour
{
}
