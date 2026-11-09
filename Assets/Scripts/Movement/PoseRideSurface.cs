using UnityEngine;

/// <summary>
/// A ride surface for anything its owner poses directly (a kinematic part moved by setting its transform):
/// the owner calls Step after each move, and points on it move with the last step's motion.
/// Supports every collider under this transform.
/// </summary>
[DisallowMultipleComponent]
public sealed class PoseRideSurface : MonoBehaviour, IRideSurface
{
    private Vector3 fromPosition, toPosition;
    private Quaternion fromRotation = Quaternion.identity, toRotation = Quaternion.identity;
    private float stepDuration;
    private bool posed;

    public bool Supports(Collider collider) => isActiveAndEnabled && collider != null && collider.enabled && collider.transform.IsChildOf(transform);

    /// <summary>Records the move just made, lasting deltaTime.</summary>
    public void Step(float deltaTime)
    {
        fromPosition = posed ? toPosition : transform.position;
        fromRotation = posed ? toRotation : transform.rotation;
        toPosition = transform.position;
        toRotation = transform.rotation;
        stepDuration = deltaTime;
        posed = true;
    }

    /// <summary>Forgets motion, e.g. after a teleport.</summary>
    public void ResetMotion() { posed = false; stepDuration = 0f; }

    public Vector3 VelocityAt(Vector3 pointWS)
    {
        if (!isActiveAndEnabled || stepDuration <= 0f) return Vector3.zero;
        Vector3 local = Quaternion.Inverse(fromRotation) * (pointWS - fromPosition);
        return (toPosition + toRotation * local - pointWS) / stepDuration;
    }

    private void OnDisable() { posed = false; stepDuration = 0f; }
}
