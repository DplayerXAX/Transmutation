using UnityEngine;

/// <summary>Shared theme districts and clear approaches. Snapshot is pure data for decoration workers.</summary>
public static class RealmDecorLayout
{
    public struct Snapshot
    {
        public bool active;
        public Vector3 tower, spawn, kelp, crystal, bone;
        public float courtyard;

        public bool OnApproach(Vector3 point, float width = 5f)
        {
            if (!active) return false;
            if (FlatDistance(point, tower) < courtyard) return true;
            return DistanceToLine(point, spawn, tower) < width || DistanceToLine(point, tower, kelp) < width
                || DistanceToLine(point, tower, crystal) < width || DistanceToLine(point, tower, bone) < width;
        }

        public int District(Vector3 point)
        {
            float wet = FlatDistance(point, kelp), stone = FlatDistance(point, crystal), fossil = FlatDistance(point, bone);
            return wet <= stone && wet <= fossil ? 0 : stone <= fossil ? 1 : 2;
        }

        public float Weight(DecorShapes.Shape shape, bool inner, Vector3 point)
        {
            if (!active) return 1f;
            if (!inner && OnApproach(point)) return 0f;
            float wet = FlatDistance(point, kelp), stone = FlatDistance(point, crystal), fossil = FlatDistance(point, bone);
            int district = District(point);
            float nearest = Mathf.Min(wet, Mathf.Min(stone, fossil));
            float second = district == 0 ? Mathf.Min(stone, fossil) : district == 1 ? Mathf.Min(wet, fossil) : Mathf.Min(wet, stone);
            // Let one theme die away before the next begins, with a quiet transition in between.
            float transition = Mathf.SmoothStep(0f, 1f, (second - nearest) / 9f);
            bool allowed;
            if (inner)
                allowed = district == 0 ? shape == DecorShapes.Shape.ShellTentacle || shape == DecorShapes.Shape.Bladders
                    : district == 1 ? shape == DecorShapes.Shape.Shards || shape == DecorShapes.Shape.Pebbles
                    : shape == DecorShapes.Shape.Pebbles || shape == DecorShapes.Shape.Ribbon;
            else
                allowed = district == 0 ? shape == DecorShapes.Shape.Polyps || shape == DecorShapes.Shape.Bladders
                        || shape == DecorShapes.Shape.Fronds || shape == DecorShapes.Shape.Creepers
                    : district == 1 ? shape == DecorShapes.Shape.Shards || shape == DecorShapes.Shape.Pebbles
                        || shape == DecorShapes.Shape.Arch || shape == DecorShapes.Shape.Cairn
                    : shape == DecorShapes.Shape.Pebbles || shape == DecorShapes.Shape.Shards;
            return allowed ? transition : 0f;
        }
    }

    public static Snapshot Capture(ProceduralWorld world) =>
        Capture(world, Object.FindFirstObjectByType<HangingSpire>(), Object.FindFirstObjectByType<BoneKiln>());

    /// <summary>Same, for given landmarks (the editor decor bake passes the Scene view previews).</summary>
    public static Snapshot Capture(ProceduralWorld world, HangingSpire spire, BoneKiln kiln)
    {
        if (world == null) return default;
        if (spire == null || !spire.Built) return default;
        Vector3 centre = world.SinkholeCentre(spire.SinkholeIndex, world.transform.position.y, out _);
        Vector3 bone = kiln != null && kiln.Root != null ? kiln.Root.position : centre + Vector3.forward * 75f;
        Vector3 axis = Vector3.ProjectOnPlane(bone - centre, Vector3.up).normalized;
        if (axis.sqrMagnitude < 0.1f) axis = Vector3.forward;
        Vector3 across = Vector3.Cross(Vector3.up, axis);
        world.SinkholeCentre(spire.SinkholeIndex, centre.y, out float radius);
        return new Snapshot { active = true, tower = centre, spawn = world.transform.position,
            bone = bone, courtyard = radius + 9f,
            kelp = spire.StageTowers.Count > 0 ? spire.StageTowers[0].position : centre + across * 55f,
            crystal = spire.StageTowers.Count > 1 ? spire.StageTowers[1].position : centre - across * 55f };
    }

    public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    private static float DistanceToLine(Vector3 point, Vector3 a, Vector3 b)
    {
        point.y = a.y = b.y = 0f;
        Vector3 line = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, line) / Mathf.Max(0.001f, line.sqrMagnitude));
        return Vector3.Distance(point, a + line * t);
    }
}
