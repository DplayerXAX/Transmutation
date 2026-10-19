using UnityEngine;

/// <summary>Planned fixed-step motion of a compost back, read by the player's relative movement.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CompostRideSurface : MonoBehaviour
{
    [SerializeField] private BoxCollider back;
    [SerializeField] private Collider ramp;
    private Vector3 fromPosition, toPosition;
    private Quaternion fromRotation = Quaternion.identity, toRotation = Quaternion.identity;
    private float stepDuration;

    public BoxCollider Back => back;
    public Collider Ramp => ramp;
    public bool Supports(Collider collider) => isActiveAndEnabled && collider != null && (collider == back || collider == ramp);

    internal void PlanMotion(Vector3 position, Quaternion rotation, float deltaTime)
    {
        Rigidbody body = GetComponent<Rigidbody>();
        fromPosition = body.position;
        fromRotation = body.rotation;
        toPosition = position;
        toRotation = rotation;
        stepDuration = deltaTime;
    }

    public Vector3 VelocityAt(Vector3 pointWS)
    {
        if (!isActiveAndEnabled || stepDuration <= 0f) return Vector3.zero;
        Vector3 localPoint = Quaternion.Inverse(fromRotation) * (pointWS - fromPosition);
        return (toPosition + toRotation * localPoint - pointWS) / stepDuration;
    }

    internal Vector3 TransformPlannedPoint(Vector3 pointOS) => toPosition + toRotation * pointOS;
    internal Quaternion PlannedRotation => toRotation;
    private void OnDisable() => stepDuration = 0f;
}
