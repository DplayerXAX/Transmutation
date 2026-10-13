using UnityEngine;

/// <summary>
/// Posts a reach event each time a climbing hand lets go to reach for a new hold.
/// </summary>
[RequireComponent(typeof(HandClimber))]
public sealed class ClimbReachSound : MonoBehaviour
{
    public string reachEvent = "Play_Climb_Reach";
    [Tooltip("Reaches snap to this music grid: 1 = beats, 2 = eighth notes, 4 = sixteenths.")]
    [Min(1)] public int gridDivision = 2;

    private HandClimber climber;
    private readonly bool[] wasGripping = new bool[2];

    private void Awake() => climber = GetComponent<HandClimber>();

    private void LateUpdate()
    {
        for (int i = 0; i < 2; i++)
        {
            bool active = climber.GetHand(i, out _, out _, out bool gripping);
            if (active && wasGripping[i] && !gripping)
                AudioManager.PostEventOnGrid(reachEvent, gameObject, gridDivision);
            wasGripping[i] = active && gripping;
        }
    }
}
