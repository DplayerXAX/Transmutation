using UnityEngine;

/// <summary>Adds frame velocity after autonomous fixed-step motors, before physics simulation.</summary>
[DefaultExecutionOrder(10000)]
public sealed class FrameVelocityCommit : MonoBehaviour
{
    MovingFrame frame;
    void Awake() => frame = GetComponent<MovingFrame>();
    void FixedUpdate() { if (frame && frame.isActiveAndEnabled) frame.CommitVelocities(); }
}
