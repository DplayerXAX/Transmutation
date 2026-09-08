using UnityEngine;

/// <summary>
/// Spherical density overlay for MarchingCubesSphere.
///
/// Inside the sphere (center = transform.position, given radius), effective density is
/// base densities[,,] + densityOffset. This does not bake into the stored field:
/// moving or disabling the overlay restores the underlying density.
///
/// densityOffset &lt; 0 → lower density (carve)
/// densityOffset &gt; 0 → raise density (grow)
/// </summary>
public class DensitySphereOverlay : MonoBehaviour
{
	[SerializeField] MarchingCubesSphere terrain;

	[Tooltip("Sphere radius in world units (matches the Scene gizmo).")]
	[SerializeField] float radius = 2f;

	[Tooltip("Fixed density added inside the sphere. Negative lowers, positive raises.")]
	[SerializeField] float densityOffset = -2f;

	[Tooltip("Gizmo color for the overlay radius.")]
	[SerializeField] Color gizmoColor = new Color(1f, 0.45f, 0.15f, 0.9f);

	public float Radius => radius;
	public float DensityOffset => densityOffset;

	Vector3 lastPosition;
	float lastRadius;
	float lastOffset;

	void OnEnable()
	{
		ResolveTerrain();
		CacheState();
		if (terrain != null)
		{
			terrain.RegisterOverlay(this);
		}
	}

	void OnDisable()
	{
		if (terrain != null)
		{
			terrain.UnregisterOverlay(this);
		}
	}

	void Update()
	{
		bool wasMissingTerrain = terrain == null;
		ResolveTerrain();
		if (terrain == null)
		{
			return;
		}

		if (wasMissingTerrain)
		{
			terrain.RegisterOverlay(this);
			CacheState();
			return;
		}

		if (!HasChanged())
		{
			return;
		}

		CacheState();
		terrain.RequestMeshUpdate();
	}

	void OnValidate()
	{
		radius = Mathf.Max(0.01f, radius);
	}

	void OnDrawGizmos()
	{
		Color color = gizmoColor;
		color.a = 0.35f;
		Gizmos.color = color;
		Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, radius));
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = gizmoColor;
		Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, radius));
	}

	/// <summary>
	/// Fixed offset if <paramref name="worldSample"/> is inside this sphere; otherwise 0.
	/// </summary>
	public float EvaluateOffset(Vector3 worldSample)
	{
		if (Vector3.Distance(worldSample, transform.position) > radius)
		{
			return 0f;
		}

		return densityOffset;
	}

	void ResolveTerrain()
	{
		if (terrain != null)
		{
			return;
		}

		terrain = GetComponentInParent<MarchingCubesSphere>();
		if (terrain == null)
		{
			terrain = FindObjectOfType<MarchingCubesSphere>();
		}
	}

	void CacheState()
	{
		lastPosition = transform.position;
		lastRadius = radius;
		lastOffset = densityOffset;
	}

	bool HasChanged()
	{
		return lastPosition != transform.position ||
		       !Mathf.Approximately(lastRadius, radius) ||
		       !Mathf.Approximately(lastOffset, densityOffset);
	}
}
