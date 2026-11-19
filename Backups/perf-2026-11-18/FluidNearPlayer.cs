using UnityEngine;

/// <summary>
/// Runs an ErosiveFluidSystem only while the player is near its springs: further away the springs stop and the
/// water already flowing is cleared, so a distant waterfall costs nothing. Checked twice a second.
/// </summary>
[RequireComponent(typeof(ErosiveFluidSystem))]
public sealed class FluidNearPlayer : MonoBehaviour
{
    [Tooltip("The water runs while the player is within this distance of any spring (metres).")]
    [Min(10f)] public float activeRange = 70f;
    [Tooltip("Extra distance before it stops again, so it does not flicker at the edge.")]
    [Min(0f)] public float margin = 15f;

    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource[] springs;
    private Transform player;
    private bool running = true;
    private float nextCheck;

    public bool Running => running;

    private void Awake()
    {
        fluid = GetComponent<ErosiveFluidSystem>();
        springs = GetComponentsInChildren<ErosiveFluidSource>();
    }

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (player == null)
        {
            var controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller == null) return;
            player = controller.transform;
        }
        float nearest = float.MaxValue;
        foreach (ErosiveFluidSource s in springs) if (s != null) nearest = Mathf.Min(nearest, (s.transform.position - player.position).magnitude);
        bool want = running ? nearest < activeRange + margin : nearest < activeRange;
        if (want == running) return;
        running = want;
        fluid.SetEmitting(want);
        if (!want) fluid.ClearDroplets();
    }
}
