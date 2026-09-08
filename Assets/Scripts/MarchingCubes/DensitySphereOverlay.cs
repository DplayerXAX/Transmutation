using UnityEngine;

/// <summary>
/// Spherical density injector for MarchingCubesVolume.
///
/// Each simulation tick writes into the stored density field (not a sample-time offset):
/// densities += injectRate * falloff * dt, clamped to maxDensity.
/// Falloff is 1 at the center and 0 at the radius edge (smoothstep).
/// </summary>
public class DensitySphereOverlay : MonoBehaviour
{
	[SerializeField] MarchingCubesVolume terrain;

	[Tooltip("Sphere radius in world units (matches the Scene gizmo).")]
	[SerializeField] float radius = 2f;

	[Tooltip("Density added per second at the center. Edge receives less via falloff.")]
	[SerializeField] float injectRate = 2f;

	[Tooltip("Maximum density this injector will raise samples to.")]
	[SerializeField] float maxDensity = 2f;

	[Tooltip("Gizmo color for the overlay radius.")]
	[SerializeField] Color gizmoColor = new Color(1f, 0.45f, 0.15f, 0.9f);

	public float Radius => radius;
	public float InjectRate => injectRate;
	public float MaxDensity => maxDensity;

	void OnEnable()
	{
		ResolveTerrain();
		if (terrain != null)
		{
			terrain.RegisterOverlay(this);
		}
	}

	void Start()
	{
		ResolveTerrain();
		if (terrain == null || !terrain.gameObject.scene.IsValid())
		{
			return;
		}

		terrain.RegisterOverlay(this);
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
		if (terrain != null)
		{
			return;
		}

		ResolveTerrain();
		if (terrain != null)
		{
			terrain.RegisterOverlay(this);
		}
	}

	void OnValidate()
	{
		radius = Mathf.Max(0.01f, radius);
		maxDensity = Mathf.Max(0f, maxDensity);
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
	/// Smooth falloff weight: 1 at center, 0 at/ beyond radius.
	/// </summary>
	public float EvaluateFalloff(Vector3 worldSample)
	{
		float distance = Vector3.Distance(worldSample, transform.position);
		if (distance >= radius)
		{
			return 0f;
		}

		float t = 1f - distance / radius;
		t = Mathf.Clamp01(t);
		return t * t * (3f - 2f * t);
	}

	void ResolveTerrain()
	{
		if (terrain != null)
		{
			return;
		}

		terrain = GetComponentInParent<MarchingCubesVolume>();
		if (terrain == null)
		{
			terrain = FindObjectOfType<MarchingCubesVolume>();
		}
	}
}
