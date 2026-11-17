using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the hand closes on a held thing, and how the thing turns in the hand.
/// Put a HoldGrip on a creature or object (prefab or scene) to set it for that one; otherwise the per-type defaults in
/// Assets/Resources/HoldGrips.asset (HoldGripTable) are used; with neither, PlayerCreatureCarrier keeps its old
/// automatic hold (by the visual middle, pushed out by size).
///
/// In the hand, the grip point sits on the hand (the carrier's handRest, plus `handNudge`), and the thing's rotation is
/// the view's rotation (yaw only, or yaw and pitch with `followPitch`) turned by `heldEuler`.
/// heldEuler (0,0,0) = upright, facing the way the player looks.
/// </summary>
[DisallowMultipleComponent]
public sealed class HoldGrip : MonoBehaviour
{
    [Tooltip("Optional: a child marking the grip (its position is the grip point, its rotation is ignored).")]
    public Transform gripTransform;
    [Tooltip("Grip point in this object's local space (used when Grip Transform is empty).")]
    public Vector3 gripPoint;
    [Tooltip("Rotation in the hand, relative to the view (degrees). 0,0,0 = upright, facing forward.")]
    public Vector3 heldEuler;
    [Tooltip("On: tilts up and down with the view (tools). Off: stays upright (creatures).")]
    public bool followPitch;
    [Tooltip("Moves the hand point for this thing (view space: right, up, forward; metres).")]
    public Vector3 handNudge;

    public HoldPose Pose(Transform root)
    {
        Vector3 local = gripTransform != null ? root.InverseTransformPoint(gripTransform.position) : gripPoint;
        // Carrier works with the point relative to the pivot in the thing's rotated frame (unscaled), like its centre.
        Vector3 offset = Quaternion.Inverse(root.rotation) * (root.TransformPoint(local) - root.position);
        return new HoldPose { gripLocal = offset, heldRotation = Quaternion.Euler(heldEuler), followPitch = followPitch, handNudge = handNudge };
    }
}

/// <summary>A resolved grip: offset from the pivot to the grip (pivot rotation frame), rotation in the hand, nudge.</summary>
public struct HoldPose
{
    public Vector3 gripLocal;
    public Quaternion heldRotation;
    public bool followPitch;
    public Vector3 handNudge;
}
