using UnityEngine;

/// <summary>
/// Marks a moving thing (and its child colliders) whose surfaces climbing hands should hold on to as it moves,
/// such as the Ore Colossus's walking legs and body: a grip is kept in the collider's own space and follows it.
/// Without this marker a grip is a fixed point in the world.
/// </summary>
[DisallowMultipleComponent]
public sealed class MovingGripSurface : MonoBehaviour
{
}
