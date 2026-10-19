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
        transform.SetParent(squirrel.transform, true);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
    }

    public bool Store(Squirrel squirrel, SquirrelNest nest)
    {
        if (Owner != squirrel || IsStored || nest == null || nest.Owner != squirrel) return false;
        IsStored = true;
        Owner = null;
        transform.SetParent(nest.transform, false);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
        return true;
    }
}
