using UnityEngine;

/// <summary>
/// Puts a creature back on its spot (for example the bounce mushroom on the spire's balcony) when it has
/// fallen well below it and lies there for a while without being carried. It "grows back" where it was.
/// </summary>
[DisallowMultipleComponent]
public sealed class ReturnHome : MonoBehaviour
{
    [Tooltip("How far below its spot it must be before it goes back.")]
    [Min(0.5f)] public float dropBelow = 5f;
    [Tooltip("Seconds it must stay down there first.")]
    [Min(0f)] public float delay = 4f;

    private Vector3 home;
    private Quaternion homeRotation;
    private Creature creature;
    private Rigidbody body;
    private float lowTimer;

    private void Start()
    {
        home = transform.position;
        homeRotation = transform.rotation;
        creature = GetComponent<Creature>();
        body = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        bool carried = creature != null && creature.IsCarried;
        lowTimer = !carried && transform.position.y < home.y - dropBelow ? lowTimer + Time.deltaTime : 0f;
        if (lowTimer < delay) return;
        lowTimer = 0f;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = home;
            body.rotation = homeRotation;
        }
        transform.SetPositionAndRotation(home, homeRotation);
    }
}
