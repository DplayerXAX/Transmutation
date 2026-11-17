using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Per creature type grip defaults (Assets/Resources/HoldGrips.asset). Edit it in the Inspector.</summary>
[CreateAssetMenu(menuName = "Capstone/Hold Grip Table", fileName = "HoldGrips")]
public sealed class HoldGripTable : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [Tooltip("Class name of a Creature subclass, e.g. BubbleFlower. Subclasses use their base class's entry if they have none.")]
        public string creatureType;
        [Tooltip("Grip point in the thing's visible box: -1..1 on each axis of its local bounds. 0,0,0 = its middle, 0,-1,0 = the middle of its bottom.")]
        public Vector3 gripInBounds;
        [Tooltip("Rotation in the hand, relative to the view (degrees). 0,0,0 = upright, facing forward.")]
        public Vector3 heldEuler;
        [Tooltip("On: tilts up and down with the view (tools). Off: stays upright (creatures).")]
        public bool followPitch;
        [Tooltip("Moves the hand point for this type (view space: right, up, forward; metres).")]
        public Vector3 handNudge;
    }

    public List<Entry> entries = new List<Entry>();

    private static HoldGripTable loaded;
    private static bool tried;

    public static HoldGripTable Instance
    {
        get
        {
            if (!tried) { tried = true; loaded = Resources.Load<HoldGripTable>("HoldGrips"); }
            return loaded;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = null; tried = false; }

    public Entry Find(Type type)
    {
        for (Type t = type; t != null && t != typeof(Creature) && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (Entry e in entries)
                if (e != null && e.creatureType == t.Name) return e;
        return null;
    }

    /// <summary>The grip of a held thing: its HoldGrip, else its type's entry, else none (automatic hold).</summary>
    public static bool TryResolve(Creature creature, out HoldPose pose)
    {
        pose = default;
        if (creature == null) return false;
        if (creature.TryGetComponent(out HoldGrip grip)) { pose = grip.Pose(creature.transform); return true; }
        HoldGripTable table = Instance;
        Entry e = table != null ? table.Find(creature.GetType()) : null;
        if (e == null) return false;
        Transform root = creature.transform;
        Vector3 local = Vector3.zero;
        if (LocalBounds(root, out Bounds b))
            local = b.center + Vector3.Scale(b.extents, e.gripInBounds);
        Vector3 offset = Quaternion.Inverse(root.rotation) * (root.TransformPoint(local) - root.position);
        pose = new HoldPose { gripLocal = offset, heldRotation = Quaternion.Euler(e.heldEuler), followPitch = e.followPitch, handNudge = e.handNudge };
        return true;
    }

    /// <summary>The thing's visible box in its own local space (same renderers CreatureBounds measures).</summary>
    private static bool LocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer ||
                !CreatureBounds.BelongsTo(r.transform, root)) continue;
            Bounds w = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = w.center + Vector3.Scale(w.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = root.InverseTransformPoint(corner);
                if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                else bounds.Encapsulate(local);
            }
        }
        return found;
    }
}
