using UnityEngine;

/// <summary>Builds the existing procedural bone child at runtime, like the landmark's native spawning.</summary>
public sealed class IslandBoneShowcase : MonoBehaviour
{
    public MovingFrame frame;
    public Material boneMaterial, skinMaterial;
    void Start()
    {
        if (!frame) return;
        Vector3 point = frame.transform.TransformPoint(new Vector3(0,.4f,-27));
        var child = BoneChild.Spawn(null, point, frame.transform.eulerAngles.y, 123, boneMaterial, skinMaterial,
            frame.transform.TransformPoint(new Vector3(0,0,-20)));
        child.name = "Test Bone Dragon — feed a fire bubble to fly";
        child.gameObject.AddComponent<MovementFrameMember>().Join(frame);
    }
}
