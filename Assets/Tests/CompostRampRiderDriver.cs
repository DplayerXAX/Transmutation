using System.Reflection;
using UnityEngine;

/// <summary>Disposable test input, ordered between platform planning and physics like the actual player.</summary>
[DefaultExecutionOrder(-100)]
public sealed class CompostRampRiderDriver : MonoBehaviour
{
    public SmoothFirstPersonController Movement;
    public Vector3 DirectionOS;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo Vertical = typeof(SmoothFirstPersonController).GetField("verticalInput", Private);
    private static readonly FieldInfo Horizontal = typeof(SmoothFirstPersonController).GetField("horizontalInput", Private);
    private static readonly FieldInfo Speed = typeof(SmoothFirstPersonController).GetField("moveSpeed", Private);
    private static readonly MethodInfo Ground = typeof(SmoothFirstPersonController).GetMethod("GroundCheck", Private);
    private static readonly MethodInfo Tick = typeof(SmoothFirstPersonController).GetMethod("FixedUpdate", Private);

    private void FixedUpdate()
    {
        if (Movement == null) return;
        Vertical.SetValue(Movement, DirectionOS.z);
        Horizontal.SetValue(Movement, DirectionOS.x);
        Speed.SetValue(Movement, 1.5f);
        Ground.Invoke(Movement, null);
        Tick.Invoke(Movement, null);
    }
}
