using UnityEngine;

/// <summary>Hands a touching fire bubble to a moving piece of the mother's skeleton.</summary>
[DisallowMultipleComponent]
public sealed class BoneFireContact : MonoBehaviour
{
    public BoneKiln Owner { get; private set; }
    private Collider solid;
    private BoxCollider flightContact;

    public void Setup(BoneKiln owner, Renderer renderer)
    {
        Owner = owner;
        solid = GetComponent<Collider>();
        Bounds bounds = GetComponent<MeshFilter>() != null ? GetComponent<MeshFilter>().sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        flightContact = gameObject.AddComponent<BoxCollider>();
        flightContact.center = bounds.center;
        flightContact.size = bounds.size + Vector3.one * 0.15f;
        flightContact.isTrigger = true;
        flightContact.enabled = false;
    }

    public void SetFlying()
    {
        if (solid != null) solid.enabled = false;
        if (flightContact != null) flightContact.enabled = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        FireBubble fire = collision.collider.GetComponentInParent<FireBubble>();
        if (fire == null || Owner == null || collision.contactCount == 0) return;
        ContactPoint contact = collision.GetContact(0);
        Vector3 normal = (fire.transform.position - contact.point).normalized;
        Owner.Catch(fire, solid, contact.point, normal.sqrMagnitude > 0f ? normal : contact.normal);
    }

    private void OnTriggerEnter(Collider other)
    {
        FireBubble fire = other.GetComponentInParent<FireBubble>();
        if (fire == null || Owner == null || flightContact == null) return;
        Vector3 point = flightContact.ClosestPoint(fire.transform.position);
        Vector3 normal = fire.transform.position - flightContact.bounds.center;
        Owner.Catch(fire, flightContact, point, normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up);
    }
}
