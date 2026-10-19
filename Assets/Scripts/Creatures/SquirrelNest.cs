using UnityEngine;

[DisallowMultipleComponent]
public sealed class SquirrelNest : MonoBehaviour
{
    [SerializeField, Min(0)] private int storedFoodCount;
    public int StoredFoodCount => storedFoodCount;

    public bool Deposit(Squirrel squirrel, SquirrelFood food)
    {
        if (food == null || !food.Store(squirrel, this)) return false;
        storedFoodCount++;
        return true;
    }
}
