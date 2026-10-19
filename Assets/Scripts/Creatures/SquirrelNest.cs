using UnityEngine;

[DisallowMultipleComponent]
public sealed class SquirrelNest : MonoBehaviour
{
    [SerializeField, Min(0)] private int storedFoodCount;
    public int StoredFoodCount => storedFoodCount;
    public Squirrel Owner { get; private set; }
    public bool IsMoving { get; private set; }
    private Transform restingParent;
    private Collider[] shapes;
    private bool[] enabledShapes;
    private Rigidbody nestBody;
    private bool wasKinematic;
    private bool usedGravity;

    public bool BeginMove(Squirrel squirrel)
    {
        if (Owner != squirrel || IsMoving) return false;
        restingParent = transform.parent;
        shapes = GetComponentsInChildren<Collider>(true);
        enabledShapes = new bool[shapes.Length];
        for (int i = 0; i < shapes.Length; i++)
        {
            enabledShapes[i] = shapes[i].enabled;
            shapes[i].enabled = false;
        }
        nestBody = GetComponent<Rigidbody>();
        if (nestBody != null)
        {
            wasKinematic = nestBody.isKinematic;
            usedGravity = nestBody.useGravity;
            nestBody.isKinematic = true;
            nestBody.useGravity = false;
        }
        IsMoving = true;
        transform.SetParent(squirrel.transform, true);
        transform.localPosition = new Vector3(0f, 1.2f, -0.9f);
        return true;
    }

    public void EndMove(Vector3 ground)
    {
        if (!IsMoving) return;
        transform.SetParent(restingParent, true);
        SquirrelTree.PlaceOnGround(transform, ground);
        for (int i = 0; i < shapes.Length; i++)
            if (shapes[i] != null) shapes[i].enabled = enabledShapes[i];
        if (nestBody != null)
        {
            nestBody.isKinematic = wasKinematic;
            nestBody.useGravity = usedGravity;
        }
        IsMoving = false;
        Physics.SyncTransforms();
    }

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
