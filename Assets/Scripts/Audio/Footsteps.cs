using UnityEngine;

/// <summary>
/// Posts a left or right footstep event each time a leg in FirstPersonBody's walk cycle plants.
/// </summary>
[RequireComponent(typeof(FirstPersonBody))]
public sealed class Footsteps : MonoBehaviour
{
    public string leftEvent = "Play_Footstep_Left";
    public string rightEvent = "Play_Footstep_Right";
    [Tooltip("Steps snap to this music grid: 1 = beats, 2 = eighth notes, 4 = sixteenths.")]
    [Min(1)] public int gridDivision = 2;

    private FirstPersonBody body;
    private int lastStep;

    private void Awake()
    {
        body = GetComponent<FirstPersonBody>();
        lastStep = StepIndex();
    }

    private void LateUpdate()
    {
        int step = StepIndex();
        if (step == lastStep) return;
        // The phase only advances while walking on the ground, so every new index is a real step.
        bool right = (step & 1) == 0;
        lastStep = step;
        AudioManager.PostEventOnGrid(right ? rightEvent : leftEvent, gameObject, gridDivision);
    }

    private int StepIndex() => Mathf.FloorToInt((body.WalkPhase - Mathf.PI * 0.5f) / Mathf.PI);
}
