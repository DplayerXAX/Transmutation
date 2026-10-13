using UnityEngine;

/// <summary>Selects an accessible generated slope in the copied large-map scene.</summary>
public sealed class ErosionDemoPlacement : MonoBehaviour
{
    [SerializeField] private ErosiveFluidSystem fluid;
    private bool placed;
    public bool Placed => placed;
    private void Update()
    {
        if (placed || fluid == null || !fluid.World.GenerationComplete) return;
        var source = GetComponentInChildren<ErosiveFluidSource>();
        Vector3 origin = fluid.World.transform.position;
        RaycastHit best = default; float score = float.MinValue;
        for (int z = 24; z <= 72; z += 4) for (int x = -44; x <= 44; x += 4)
        {
            if (!Physics.Raycast(origin + new Vector3(x, 160f, z), Vector3.down, out RaycastHit hit, 300f, 1 << 7) || !fluid.World.OwnsCollider(hit.collider)) continue;
            float slope = Vector3.Angle(Vector3.up, hit.normal);
            if (slope < 12f || slope > 35f) continue;
            float candidate = slope - Vector3.Distance(origin, hit.point) * 0.12f;
            if (candidate > score) { score = candidate; best = hit; }
        }
        if (best.collider != null)
        {
            source.transform.position = best.point + Vector3.up * 1f;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, best.normal).normalized;
            source.transform.rotation = Quaternion.LookRotation(downhill, Vector3.up);
        }
        source.SetEmitting(true); placed = true;
    }
}
