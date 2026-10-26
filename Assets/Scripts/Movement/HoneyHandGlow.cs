using UnityEngine;

/// <summary>Owned by the coating visual root, so disabling that root hides the hand glow too.</summary>
[DefaultExecutionOrder(1300)]
public sealed class HoneyHandGlow : MonoBehaviour
{
    public Transform Target { get; set; }
    private void LateUpdate()
    {
        if (Target != null) transform.position = Target.position;
    }
}
