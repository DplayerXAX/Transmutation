using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Reads surface materials without changing sources or copying their script-driven properties.</summary>
public sealed class SurfaceMaterialResolver
{
    private Collider[] overlaps = new Collider[32];
    private readonly HashSet<Transform> owners = new HashSet<Transform>();
    private readonly List<Material> nearby = new List<Material>();
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Material> materials = new List<Material>();

    public bool TryFromHit(RaycastHit hit, out Material material) =>
        TryResolve(hit.collider, hit.point, hit.triangleIndex, out material);

    public bool TryNearby(Scene scene, Vector3 center, float radius, Creature self, out Material material)
    {
        material = null;
        owners.Clear();
        nearby.Clear();
        int count;
        var physics = scene.GetPhysicsScene();
        while ((count = physics.OverlapSphere(center, radius, overlaps, ~0, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            System.Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (collider.GetComponentInParent<SmoothFirstPersonController>() != null ||
                collider.transform.IsChildOf(self.transform)) continue;
            Transform owner = Owner(collider);
            if (owners.Contains(owner)) continue;
            if (!TryResolve(collider, center, -1, out Material candidate)) continue;
            owners.Add(owner);
            nearby.Add(candidate);
        }
        if (nearby.Count == 0) return false;
        material = nearby[Random.Range(0, nearby.Count)];
        return true;
    }

    private static Transform Owner(Collider collider)
    {
        Creature creature = collider.GetComponentInParent<Creature>();
        return creature != null ? creature.transform :
            collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform;
    }

    private static bool Eligible(Renderer renderer) => renderer != null && renderer.enabled &&
        renderer.gameObject.activeInHierarchy && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) &&
        renderer.GetComponentInParent<SmoothFirstPersonController>() == null;

    private bool TryResolve(Collider collider, Vector3 point, int triangle, out Material material)
    {
        material = null;
        if (collider == null || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        if (collider.TryGetComponent<Terrain>(out var terrain) && terrain.enabled)
        {
            material = terrain.materialTemplate;
            return material != null;
        }

        Transform owner = Owner(collider);
        Renderer selected = null;
        // A collider nested inside a rendered part may have its renderer on an ancestor.
        for (Transform part = collider.transform; part != null; part = part.parent)
        {
            if (part.TryGetComponent<Renderer>(out var renderer) && Eligible(renderer)) { selected = renderer; break; }
            if (part == owner) break;
        }
        if (selected == null)
        {
            renderers.Clear();
            owner.GetComponentsInChildren(false, renderers);
            float nearest = float.PositiveInfinity;
            foreach (Renderer renderer in renderers)
            {
                if (!Eligible(renderer)) continue;
                renderer.GetSharedMaterials(materials);
                if (!materials.Exists(m => m != null)) continue;
                float distance = renderer.bounds.SqrDistance(point);
                if (distance >= nearest) continue;
                nearest = distance;
                selected = renderer;
            }
        }
        if (selected == null) return false;
        selected.GetSharedMaterials(materials);

        // Triangle indices only identify a material when the physics and rendering meshes agree.
        if (triangle >= 0 && collider is MeshCollider meshCollider &&
            selected.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null &&
            filter.sharedMesh == meshCollider.sharedMesh)
        {
            long firstTriangle = 0;
            for (int slot = 0; slot < filter.sharedMesh.subMeshCount; slot++)
            {
                if (filter.sharedMesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                long endTriangle = firstTriangle + (long)filter.sharedMesh.GetIndexCount(slot) / 3;
                if (triangle < endTriangle && slot < materials.Count && materials[slot] != null)
                { material = materials[slot]; return true; }
                firstTriangle = endTriangle;
            }
        }
        int validCount = 0;
        foreach (Material entry in materials) if (entry != null) validCount++;
        if (validCount == 0) return false;
        int pick = Random.Range(0, validCount);
        foreach (Material entry in materials)
            if (entry != null && pick-- == 0) { material = entry; return true; }
        return false;
    }
}
