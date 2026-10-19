using System.Collections.Generic;
using UnityEngine;

/// <summary>Simple ground-plane forager. Food stays hidden in its body until deposited.</summary>
[DisallowMultipleComponent]
public sealed class Squirrel : MonoBehaviour
{
    public enum Activity { Searching, Collecting, Returning }
    [SerializeField, Min(0.01f)] private float moveSpeed = 2f;
    [SerializeField, Min(0.01f)] private float reachDistance = 0.65f;
    [SerializeField, Min(0.1f)] private float searchRadius = 100f;
    [SerializeField, Min(0.1f)] private float searchInterval = 0.5f;
    [SerializeField, Min(1)] private int capacity = 3;
    [SerializeField] private Activity activity;
    private readonly List<SquirrelFood> carried = new List<SquirrelFood>();
    private FoodManager foodManager;
    private SquirrelNest nest;
    private SquirrelFood target;
    private float nextSearch;
    public int Capacity => capacity;
    public int CarriedCount => carried.Count;
    public Activity CurrentActivity => activity;
    public SquirrelNest Nest => nest;

    public void Initialize(FoodManager foods, SquirrelNest home, float size, int baseCapacity)
    {
        ReleaseFood();
        foodManager = foods;
        nest = home;
        transform.localScale *= size;
        capacity = Mathf.Max(1, Mathf.RoundToInt(baseCapacity * size * size * size));
        activity = Activity.Searching;
        nextSearch = 0f;
    }

    private void Update()
    {
        if (foodManager == null || nest == null || !nest.isActiveAndEnabled) return;
        carried.RemoveAll(food => food == null);
        if (activity == Activity.Returning)
        {
            if (!MoveTo(nest.transform.position)) return;
            foreach (var food in carried) nest.Deposit(this, food);
            carried.Clear();
            activity = Activity.Searching;
            return;
        }
        if (target != null && (target.Owner != this || !target.isActiveAndEnabled))
        {
            target.Release(this);
            target = null;
        }
        if (target == null)
        {
            if (Time.time < nextSearch) return;
            nextSearch = Time.time + searchInterval;
            target = foodManager.ReserveNearest(this, searchRadius);
            activity = target != null ? Activity.Collecting :
                carried.Count > 0 ? Activity.Returning : Activity.Searching;
            if (target == null) return;
        }
        if (!MoveTo(target.transform.position)) return;
        target.HideInside(this);
        carried.Add(target);
        target = null;
        activity = carried.Count >= capacity ? Activity.Returning : Activity.Searching;
        nextSearch = 0f;
    }

    private bool MoveTo(Vector3 destination)
    {
        // Keep the authored height. This prototype does not require a baked NavMesh.
        destination.y = transform.position.y;
        Vector3 offset = destination - transform.position;
        float reach = reachDistance * Mathf.Max(0.1f, transform.lossyScale.x);
        if (offset.sqrMagnitude <= reach * reach) return true;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(offset), 360f * Time.deltaTime);
        transform.position = Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.deltaTime);
        return false;
    }

    private void ReleaseFood()
    {
        if (target != null) target.Release(this);
        target = null;
        foreach (var food in carried) if (food != null) food.Release(this);
        carried.Clear();
    }

    private void OnDisable() => ReleaseFood();
}
