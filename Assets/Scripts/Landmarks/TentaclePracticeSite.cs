using UnityEngine;

/// <summary>Two natural slingshot examples outside the kiln, built only over loaded, unobstructed ground.</summary>
public static class TentaclePracticeSite
{
    private static readonly System.Collections.Generic.Dictionary<(ProceduralWorld world, int index), int> candidates = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCandidates() => candidates.Clear();
    public static bool TryBuild(ProceduralWorld world, Vector3 mouth, Vector3 heading, float kilnRadius,
        int index, TentacleCreature template, int seed)
    {
        if (world == null || TentacleTether.Instance == null) return false;
        var key = (world, index);
        candidates.TryGetValue(key, out int next);
        for (int probe = 0; probe < 2; probe++)
        {
            int attempt = next;
            next = (next + 1) % 12;
            candidates[key] = next;
            Vector3 forward = Quaternion.Euler(0f, (index == 0 ? 65f : -65f) + (attempt % 3 - 1) * 12f, 0f) * heading;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 start = mouth + forward * (kilnRadius + 25f + attempt / 3 * 8f);
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            Material rock = null;
            bool safe = true;
            for (int row = -1; row <= 4 && safe; row++)
            for (int side = -1; side <= 1; side++)
            {
                Vector3 point = start + forward * (row * 6f) + right * (side * 9f);
                world.ColumnHeights(point, out float surface, out _, out _);
                RaycastHit ground = default;
                float nearest = float.PositiveInfinity;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, surface + 35f, point.z),
                             Vector3.down, 45f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
                    if (world.OwnsCollider(hit.collider) && hit.normal.y >= 0.65f && hit.distance < nearest)
                    { nearest = hit.distance; ground = hit; }
                if (ground.collider == null) { safe = false; break; }
                low = Mathf.Min(low, ground.point.y); high = Mathf.Max(high, ground.point.y);
                if (rock == null && ground.collider.TryGetComponent<Renderer>(out var renderer)) rock = renderer.sharedMaterial;
            }
            if (!safe || high - low > 3f) continue;
            start.y = high + 0.15f;
            Quaternion rotation = Quaternion.LookRotation(forward);
            foreach (Collider obstacle in Physics.OverlapBox(start + forward * 9f + Vector3.up * 3f,
                         new Vector3(10f, 3f, 17f), rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!world.OwnsCollider(obstacle)) { safe = false; break; }
            if (!safe) continue;
            Build(start, rotation, index, template, rock, seed);
            candidates.Remove(key);
            return true;
        }
        return false;
    }

    public static Transform Build(Vector3 at, Quaternion rotation, int index, TentacleCreature template, Material rock, int seed)
    {
        var root = new GameObject($"Tentacle Practice {index + 1}").transform;
        root.SetPositionAndRotation(at, rotation);
        if (rock == null)
        {
            rock = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/Internal-Colored"));
            rock.SetColor("_BaseColor", new Color(0.29f, 0.33f, 0.34f));
        }
        float halfSpan = index == 0 ? 5f : 7f;
        Rock("Draw ledge", new Vector3(0f, -2f, -1.5f), new Vector3(halfSpan + 2f, 2f, 7f));
        Transform left = Rock("Left sucker support", new Vector3(-halfSpan - 1.3f, 0.25f, 0f), new Vector3(1.3f, 4.25f, 2f));
        Transform right = Rock("Right sucker support", new Vector3(halfSpan + 1.3f, 0.25f, 0f), new Vector3(1.3f, 4.25f, 2f));
        Rock("Landing ledge", new Vector3(0f, -0.5f, 19f), new Vector3(6f, 3.5f, 4.5f));
        Rock("Further left support", new Vector3(-8f, 1.1f, 21f), new Vector3(1.5f, 5.1f, 2f));
        Rock("Further right support", new Vector3(8f, 1.1f, 21f), new Vector3(1.5f, 5.1f, 2f));
        Physics.SyncTransforms();
        var go = new GameObject("Anchored teaching tentacle");
        go.SetActive(false);
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        go.transform.localScale = template != null ? template.UncarriedScale : Vector3.one;
        go.AddComponent<SphereCollider>().radius = template != null && template.TryGetComponent<SphereCollider>(out var source) ? source.radius : 0.45f;
        var creature = go.AddComponent<TentacleCreature>();
        if (template != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), creature);
        JsonUtility.FromJsonOverwrite($"{{\"seed\":{seed + index},\"wanderRadius\":0,\"huntRadius\":0,\"ageRate\":0,\"simulationEnabled\":true}}", creature);
        creature.enabled = true;
        go.SetActive(true);
        Vector3 socket = root.TransformPoint(new Vector3(0f, 2.15f, 0f));
        Vector3 a = socket - root.right * halfSpan;
        Vector3 b = socket + root.right * halfSpan;
        if (left.GetComponent<Collider>().Raycast(new Ray(socket, -root.right), out RaycastHit leftHit, halfSpan + 4f))
            a = leftHit.point + leftHit.normal * 0.05f;
        if (right.GetComponent<Collider>().Raycast(new Ray(socket, root.right), out RaycastHit rightHit, halfSpan + 4f))
            b = rightHit.point + rightHit.normal * 0.05f;
        if (!TentacleTether.Instance.HangCreature(creature, a, left, b, right))
            Debug.LogWarning("Tentacle practice anchors could not be attached.", creature);
        return root;

        Transform Rock(string name, Vector3 centre, Vector3 radii)
        {
            // Faceted, closed stone meshes with identical visible and collision surfaces.
            var vertices = new Vector3[24];
            var triangles = new System.Collections.Generic.List<int>();
            for (int ring = 0; ring < 3; ring++)
            for (int side = 0; side < 8; side++)
            {
                float angle = side * Mathf.PI / 4f;
                float taper = ring == 2 ? 0.86f : ring == 0 ? 1.08f : 1f;
                vertices[ring * 8 + side] = new Vector3(Mathf.Cos(angle) * radii.x * taper,
                    (ring - 1) * radii.y, Mathf.Sin(angle) * radii.z * taper);
            }
            for (int side = 0; side < 8; side++)
            {
                int next = (side + 1) % 8;
                for (int ring = 0; ring < 2; ring++)
                {
                    int a = ring * 8 + side, b = ring * 8 + next, c = a + 8, d = b + 8;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
                if (side >= 1 && side < 7) triangles.AddRange(new[] { 16, 16 + next, 16 + side, 0, side, next });
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var stone = new GameObject(name) { layer = 7 };
            stone.transform.SetParent(root, false); stone.transform.localPosition = centre;
            stone.AddComponent<MeshFilter>().sharedMesh = mesh;
            stone.AddComponent<MeshRenderer>().sharedMaterial = rock;
            stone.AddComponent<MeshCollider>().sharedMesh = mesh;
            return stone.transform;
        }
    }
}
