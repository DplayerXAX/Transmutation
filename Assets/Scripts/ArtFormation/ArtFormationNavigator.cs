using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Pan and zoom the comparison board without changing any specimens.</summary>
[RequireComponent(typeof(Camera))]
public sealed class ArtFormationNavigator : MonoBehaviour
{
    public Vector3 boardCenter;
    public Vector2 boardSize;
    public Vector3 focus;
    public float distance = 38f;
    public float rowSpacing = 8.4f;
    public int rowCount;
    public int colorCount;
    public float columnSpacing = 7.6f;
    public float tilt = 65f;
    private Camera view;

    private void OnEnable()
    {
        view = GetComponent<Camera>();
        ApplyPose();
    }

    private void Update()
    {
        var mouse = Mouse.current;
        var keys = Keyboard.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > .01f)
                distance = Mathf.Clamp(distance * Mathf.Exp(-scroll * .0015f), 5f, 750f);
            if (mouse.middleButton.isPressed || mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float units = 2f * distance * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, Screen.height);
                focus -= new Vector3(delta.x, 0, delta.y / Mathf.Sin(tilt * Mathf.Deg2Rad)) * units;
            }
        }
        if (keys != null)
        {
            Vector3 direction = new Vector3(
                (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1 : 0) - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1 : 0), 0,
                (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1 : 0) - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1 : 0));
            focus += direction * distance * .55f * Time.unscaledDeltaTime * (keys.leftShiftKey.isPressed ? 3f : 1f);
            if (keys.homeKey.wasPressedThisFrame) Overview();
            if (keys.endKey.wasPressedThisFrame) { focus = new Vector3(8f, 0, 8f); distance = 46f; }
            if (keys.cKey.wasPressedThisFrame) { focus.x = (colorCount - 1) * columnSpacing; distance = 32f; }
            if (keys.pageDownKey.wasPressedThisFrame) focus.z += rowSpacing;
            if (keys.pageUpKey.wasPressedThisFrame) focus.z -= rowSpacing;
        }
        focus.x = Mathf.Clamp(focus.x, -12f, boardSize.x + 8f);
        focus.z = Mathf.Clamp(focus.z, -12f, boardSize.y + 8f);
        ApplyPose();
    }

    public void Overview()
    {
        if (!view) view = GetComponent<Camera>();
        focus = boardCenter;
        float vertical = (boardSize.y + 24f) / Mathf.Sin(tilt * Mathf.Deg2Rad);
        float horizontal = (boardSize.x + 30f) / Mathf.Max(.5f, view.aspect);
        distance = Mathf.Max(vertical, horizontal) / (2f * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f));
        ApplyPose();
    }

    private void ApplyPose()
    {
        Quaternion rotation = Quaternion.Euler(tilt, 0, 0);
        transform.SetPositionAndRotation(focus + rotation * (Vector3.back * distance), rotation);
    }
}
