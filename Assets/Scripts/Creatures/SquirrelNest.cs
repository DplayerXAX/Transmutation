using UnityEngine;

[DisallowMultipleComponent]
public sealed class SquirrelNest : MonoBehaviour
{
    [SerializeField, Min(0)] private int storedFoodCount;
    public int StoredFoodCount => storedFoodCount;
    public Squirrel Owner { get; private set; }

    public bool AssignOwner(Squirrel squirrel)
    {
        if (squirrel == null || (Owner != null && Owner != squirrel)) return false;
        Owner = squirrel;
        return true;
    }

    public bool Deposit(Squirrel squirrel, SquirrelFood food)
    {
        if (squirrel != Owner || food == null || !food.Store(squirrel, this)) return false;
        storedFoodCount++;
        return true;
    }
}
