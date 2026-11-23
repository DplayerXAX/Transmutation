using UnityEngine;

public sealed class SeaAngelIslandVisuals : MonoBehaviour
{
    public Transform leftWing, rightWing;
    [Min(0)] public float flapDegrees = 12;
    [Min(0)] public float flapFrequency = .18f;
    Quaternion leftRest, rightRest;
    void Awake()
    {
        if (leftWing) leftRest = leftWing.localRotation;
        if (rightWing) rightRest = rightWing.localRotation;
    }
    void FixedUpdate()
    {
        float angle = Mathf.Sin(Time.fixedTime * Mathf.PI * 2 * flapFrequency) * flapDegrees;
        if (leftWing) leftWing.localRotation = leftRest * Quaternion.Euler(0, angle, 0);
        if (rightWing) rightWing.localRotation = rightRest * Quaternion.Euler(0, -angle, 0);
    }
}
