using UnityEngine;

/// <summary>
/// Slow idle motion for menu decorations: bobs up and down and turns around a random axis.
/// </summary>
public class MainMenuFloat : MonoBehaviour
{
    public float bobHeight = 0.25f;
    public float bobSpeed = 0.4f;
    public float turnSpeed = 6f;

    private Vector3 basePosition;
    private Vector3 axis;
    private float phase;

    private void Awake()
    {
        basePosition = transform.localPosition;
        axis = Random.onUnitSphere;
        phase = Random.value * 10f;
    }

    private void Update()
    {
        transform.localPosition = basePosition + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight;
        transform.Rotate(axis, turnSpeed * Time.deltaTime, Space.World);
    }
}
