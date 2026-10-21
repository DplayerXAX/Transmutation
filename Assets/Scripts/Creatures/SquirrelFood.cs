using UnityEngine;

/// <summary>A food object is either available, reserved, carried, or stored in a nest.</summary>
[DisallowMultipleComponent]
public sealed class SquirrelFood : MonoBehaviour
{
    public Squirrel Owner { get; private set; }
    public bool IsStored { get; private set; }
    public bool IsAvailable => !IsStored && Owner == null && isActiveAndEnabled;

    // Stored objects are recycled; the nest's accumulated count remains intact.
    internal void Respawn(Transform parent)
    {
        StopFoodPhysics();
        Owner = null;
        IsStored = false;
        transform.SetParent(parent, true);
        gameObject.SetActive(true);
    }

    public bool Reserve(Squirrel squirrel)
    {
        if (!IsAvailable || squirrel == null) return false;
        Owner = squirrel;
        return true;
    }

    public void Release(Squirrel squirrel)
    {
        if (Owner != squirrel || IsStored) return;
        Owner = null;
        if (!gameObject.activeSelf)
        {
            transform.SetParent(null, true);
            gameObject.SetActive(true);
        }
    }

    public void HideInside(Squirrel squirrel)
    {
        if (Owner != squirrel || IsStored) return;
        StopFoodPhysics();
        transform.SetParent(squirrel.transform, true);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
    }

    /// <summary>Returns the same collected item to the world, rather than creating extra food.</summary>
    public void SpitOut(Squirrel squirrel, Vector3 position, Vector3 velocity)
    {
        if (Owner != squirrel || IsStored) return;
        Release(squirrel);
        transform.position = position;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.mass = 0.1f;
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.position = position;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
    }

    private void StopFoodPhysics()
    {
        if (!TryGetComponent<Rigidbody>(out var body)) return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.useGravity = false;
    }

    public bool Store(Squirrel squirrel, SquirrelNest nest)
    {
        if (Owner != squirrel || IsStored || nest == null || nest.Owner != squirrel) return false;
        StopFoodPhysics();
        IsStored = true;
        Owner = null;
        transform.SetParent(nest.transform, false);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
        return true;
    }
}
