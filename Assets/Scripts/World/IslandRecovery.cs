using UnityEngine;

public sealed class IslandRecovery : MonoBehaviour
{
    public MovingFrame frame;
    public Rigidbody player;
    public Vector3 safeLocalPosition = new(0, 3, -15);
    public float fallBelow = 70;
    void FixedUpdate()
    {
        if (!frame || !player || player.position.y >= frame.transform.position.y - fallBelow) return;
        player.position = frame.transform.TransformPoint(safeLocalPosition);
        player.linearVelocity = frame.VelocityAt(player.position);
        player.angularVelocity = Vector3.zero;
        if (player.TryGetComponent<MovementFrameMember>(out var member)) member.Join(frame);
    }
}
