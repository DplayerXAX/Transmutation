using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable top-face spring. Put on a Rigidbody root and assign its platform collider.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class BouncySurface : MonoBehaviour
{
    [SerializeField] private BoxCollider platformCollider;
    [Tooltip("Target upward speed in metres per second, independent of the body's mass.")]
    [Min(0.1f)] [SerializeField] private float launchSpeed = 22f;
    [Min(0.01f)] [SerializeField] private float bounceCooldown = 0.15f;
    [SerializeField] private bool bouncy = true;

    private Creature owner;
    private Rigidbody ownBody;
    private readonly Dictionary<Rigidbody, float> nextLaunchTimes = new Dictionary<Rigidbody, float>();
    private readonly HashSet<Rigidbody> topContacts = new HashSet<Rigidbody>();

    public float LaunchSpeed => launchSpeed;
    public bool IsBouncy => bouncy && isActiveAndEnabled &&
        (owner == null || (owner.isActiveAndEnabled && owner.SimulationEnabled && !owner.IsCarried));

    private void Awake()
    {
        owner = GetComponent<Creature>();
        ownBody = GetComponent<Rigidbody>();
        if (platformCollider == null) platformCollider = GetComponentInChildren<BoxCollider>();
    }

    public void SetBouncy(bool value)
    {
        bouncy = value;
        if (!value) ClearContacts();
    }

    private void OnCollisionEnter(Collision collision) => CheckTopContact(collision);
    private void OnCollisionStay(Collision collision) => CheckTopContact(collision);

    private void CheckTopContact(Collision collision)
    {
        Rigidbody body = collision.rigidbody;
        if (!IsBouncy || !CanLaunch(body) || platformCollider == null) return;
        Vector3 up = platformCollider.transform.up;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.thisCollider != platformCollider) continue;
            Vector3 localPoint = platformCollider.transform.InverseTransformPoint(contact.point) - platformCollider.center;
            if (localPoint.y < platformCollider.size.y * 0.5f - 0.05f ||
                Mathf.Abs(Vector3.Dot(contact.normal, up)) < 0.7f ||
                Vector3.Dot(body.worldCenterOfMass - contact.point, up) <= 0f) continue;
            topContacts.Add(body);
            TryLaunch(body);
            return;
        }
        topContacts.Remove(body);
    }

    private void OnCollisionExit(Collision collision)
    {
        Rigidbody body = collision.rigidbody;
        if (body == null || !topContacts.Remove(body)) return;
        // A jump can remove contact before Stay runs. Only boost an upward departure
        // from a known top contact; simply walking off an edge is not a jump.
        if (body.linearVelocity.y > 0f) TryLaunch(body);
        nextLaunchTimes.Remove(body);
    }

    public bool TryLaunch(Rigidbody body)
    {
        if (!IsBouncy || !CanLaunch(body)) return false;
        if (nextLaunchTimes.TryGetValue(body, out float nextTime) && Time.time < nextTime) return false;
        float speedChange = launchSpeed - body.linearVelocity.y;
        if (speedChange <= 0f) return false;
        body.AddForce(Vector3.up * speedChange, ForceMode.VelocityChange);
        nextLaunchTimes[body] = Time.time + bounceCooldown;
        return true;
    }

    private bool CanLaunch(Rigidbody body) => body != null && body != ownBody && !body.isKinematic &&
        (body.constraints & RigidbodyConstraints.FreezePositionY) == 0;

    private void OnDisable() => ClearContacts();

    private void ClearContacts()
    {
        nextLaunchTimes.Clear();
        topContacts.Clear();
    }
}
