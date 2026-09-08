using UnityEngine;

/// <summary>
/// Shape-based density injector for MarchingCubesVolume.
///
/// Size parameters are in world units. Transform position/rotation place the shape;
/// transform scale does not change injection size.
/// </summary>
public class DensityShapeOverlay : MonoBehaviour
{
	public enum OverlayShape
	{
		Sphere = 0,
		Box = 1,
		Capsule = 2,
		Cylinder = 3,
		Ellipsoid = 4
	}

	[SerializeField] MarchingCubesVolume terrain;

	[SerializeField] OverlayShape shape = OverlayShape.Sphere;

	[Tooltip("Sphere / Capsule / Cylinder radius in world units.")]
	[SerializeField] float radius = 0.5f;

	[Tooltip("Box half-extents in world units along local axes.")]
	[SerializeField] Vector3 boxHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);

	[Tooltip("Capsule / Cylinder total height in world units along local Y (capsule height includes hemispheres).")]
	[SerializeField] float height = 2f;

	[Tooltip("Ellipsoid radii in world units along local X/Y/Z.")]
	[SerializeField] Vector3 ellipsoidRadii = new Vector3(0.5f, 1f, 0.5f);

	[Tooltip("Density added per second at the center when using accumulative inject.")]
	[SerializeField] float injectRate = 2f;

	[Tooltip("Maximum density this injector will raise samples to.")]
	[SerializeField] float maxDensity = 2f;

	[Tooltip("If enabled, each tick stamps density to maxDensity * falloff (shape matches gizmo immediately). If disabled, density accumulates over time.")]
	[SerializeField] bool stampDensity = true;

	[Tooltip("Gizmo color for the overlay shape.")]
	[SerializeField] Color gizmoColor = new Color(1f, 0.45f, 0.15f, 0.9f);

	public OverlayShape Shape => shape;
	public float Radius => Mathf.Max(0.01f, radius);
	public float Height => GetEffectiveHeight();
	public float InjectRate => injectRate;
	public float MaxDensity => maxDensity;
	public bool StampDensity => stampDensity;

	void Awake()
	{
		MigrateSerializedDefaults();
	}

	void OnEnable()
	{
		MigrateSerializedDefaults();
		ResolveTerrain();
		if (terrain != null)
		{
			terrain.RegisterOverlay(this);
		}
	}

	void Start()
	{
		MigrateSerializedDefaults();
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

	void Reset()
	{
		radius = 0.5f;
		height = 2f;
		boxHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);
		ellipsoidRadii = new Vector3(0.5f, 1f, 0.5f);
		injectRate = 2f;
		maxDensity = 2f;
		stampDensity = true;
		shape = OverlayShape.Sphere;
	}

	void OnValidate()
	{
		MigrateSerializedDefaults();
		radius = Mathf.Max(0.01f, radius);
		maxDensity = Mathf.Max(0f, maxDensity);
	}

	/// <summary>
	/// Unity deserializes newly added fields as 0 for old assets (field initializers are skipped).
	/// </summary>
	void MigrateSerializedDefaults()
	{
		if (radius < 0.01f)
		{
			radius = 0.5f;
		}

		if (height < 0.01f)
		{
			height = Mathf.Max(2f, radius * 2f);
		}

		if (boxHalfExtents.x < 0.01f && boxHalfExtents.y < 0.01f && boxHalfExtents.z < 0.01f)
		{
			boxHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);
		}

		if (ellipsoidRadii.x < 0.01f && ellipsoidRadii.y < 0.01f && ellipsoidRadii.z < 0.01f)
		{
			ellipsoidRadii = new Vector3(0.5f, 1f, 0.5f);
		}

		if (shape == OverlayShape.Capsule)
		{
			height = Mathf.Max(height, radius * 2f);
		}
	}

	float GetEffectiveHeight()
	{
		float r = Mathf.Max(0.01f, radius);
		float h = height < 0.01f ? Mathf.Max(2f, r * 2f) : height;
		if (shape == OverlayShape.Capsule)
		{
			h = Mathf.Max(h, r * 2f);
		}

		return h;
	}

	/// <summary>
	/// Conservative world-space bounding-sphere radius used to gather candidate samples.
	/// </summary>
	public float GetWorldBoundsRadius()
	{
		MigrateSerializedDefaults();

		switch (shape)
		{
			case OverlayShape.Sphere:
				return Radius;

			case OverlayShape.Box:
				return SafeExtents(boxHalfExtents).magnitude;

			case OverlayShape.Capsule:
			{
				float r = Radius;
				float halfH = GetEffectiveHeight() * 0.5f;
				return Mathf.Sqrt(r * r + halfH * halfH);
			}

			case OverlayShape.Cylinder:
			{
				float r = Radius;
				float halfH = GetEffectiveHeight() * 0.5f;
				return Mathf.Sqrt(r * r + halfH * halfH);
			}

			case OverlayShape.Ellipsoid:
			{
				Vector3 e = SafeExtents(ellipsoidRadii);
				return Mathf.Max(e.x, e.y, e.z);
			}

			default:
				return Radius;
		}
	}

	void OnDrawGizmos()
	{
		Color color = gizmoColor;
		color.a = 0.35f;
		DrawShapeGizmo(color);
	}

	void OnDrawGizmosSelected()
	{
		DrawShapeGizmo(gizmoColor);

		// Play-mode sanity: label the active shape next to the overlay.
		if (Application.isPlaying)
		{
#if UNITY_EDITOR
			UnityEditor.Handles.Label(
				transform.position + Vector3.up * (GetWorldBoundsRadius() + 0.1f),
				$"DensityShape: {shape}\nr={Radius:0.###} h={GetEffectiveHeight():0.###}");
#endif
		}
	}

	void DrawShapeGizmo(Color color)
	{
		MigrateSerializedDefaults();

		Gizmos.color = color;
		Matrix4x4 previous = Gizmos.matrix;
		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

		switch (shape)
		{
			case OverlayShape.Sphere:
				Gizmos.DrawWireSphere(Vector3.zero, Radius);
				break;

			case OverlayShape.Box:
				Gizmos.DrawWireCube(Vector3.zero, SafeExtents(boxHalfExtents) * 2f);
				break;

			case OverlayShape.Capsule:
				DrawWireCapsule(Radius, GetEffectiveHeight());
				break;

			case OverlayShape.Cylinder:
				DrawWireCylinder(Radius, GetEffectiveHeight());
				break;

			case OverlayShape.Ellipsoid:
			{
				Vector3 e = SafeExtents(ellipsoidRadii);
				Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, e);
				Gizmos.DrawWireSphere(Vector3.zero, 1f);
				break;
			}
		}

		Gizmos.matrix = previous;
	}

	static void DrawWireCapsule(float rad, float totalHeight)
	{
		float cylinderHeight = Mathf.Max(0f, totalHeight - rad * 2f);
		float halfCylinder = cylinderHeight * 0.5f;
		Vector3 top = Vector3.up * halfCylinder;
		Vector3 bottom = Vector3.down * halfCylinder;

		Gizmos.DrawWireSphere(top, rad);
		Gizmos.DrawWireSphere(bottom, rad);
		if (cylinderHeight > 0.0001f)
		{
			DrawWireCylinder(rad, cylinderHeight);
		}
	}

	static void DrawWireCylinder(float rad, float totalHeight)
	{
		float half = totalHeight * 0.5f;
		const int segments = 24;
		Vector3 prevTop = Vector3.zero;
		Vector3 prevBottom = Vector3.zero;

		for (int i = 0; i <= segments; i++)
		{
			float angle = (i / (float)segments) * Mathf.PI * 2f;
			Vector3 offset = new Vector3(Mathf.Cos(angle) * rad, 0f, Mathf.Sin(angle) * rad);
			Vector3 top = offset + Vector3.up * half;
			Vector3 bottom = offset + Vector3.down * half;

			if (i > 0)
			{
				Gizmos.DrawLine(prevTop, top);
				Gizmos.DrawLine(prevBottom, bottom);
			}

			if (i % (segments / 4) == 0)
			{
				Gizmos.DrawLine(top, bottom);
			}

			prevTop = top;
			prevBottom = bottom;
		}
	}

	/// <summary>
	/// Smooth falloff weight: 1 at the interior focus, 0 at / beyond the shape boundary.
	/// </summary>
	public float EvaluateFalloff(Vector3 worldSample)
	{
		float normalized = GetNormalizedDistance(worldSample);
		if (!float.IsFinite(normalized) || normalized >= 1f)
		{
			return 0f;
		}

		float t = 1f - Mathf.Clamp01(normalized);
		return t * t * (3f - 2f * t);
	}

	float GetNormalizedDistance(Vector3 worldSample)
	{
		MigrateSerializedDefaults();

		// Oriented world frame (ignore scale so size stays in world units).
		Vector3 local = Quaternion.Inverse(transform.rotation) * (worldSample - transform.position);

		switch (shape)
		{
			case OverlayShape.Sphere:
				return local.magnitude / Radius;

			case OverlayShape.Box:
			{
				Vector3 e = SafeExtents(boxHalfExtents);
				return Mathf.Max(
					Mathf.Abs(local.x) / e.x,
					Mathf.Abs(local.y) / e.y,
					Mathf.Abs(local.z) / e.z);
			}

			case OverlayShape.Capsule:
				return NormalizedCapsuleDistance(local, Radius, GetEffectiveHeight());

			case OverlayShape.Cylinder:
			{
				float r = Radius;
				float halfH = GetEffectiveHeight() * 0.5f;
				float radial = new Vector2(local.x, local.z).magnitude / r;
				float axial = Mathf.Abs(local.y) / halfH;
				// True capped cylinder (side silhouette is rectangular — not a capsule).
				return Mathf.Max(radial, axial);
			}

			case OverlayShape.Ellipsoid:
			{
				Vector3 e = SafeExtents(ellipsoidRadii);
				Vector3 n = new Vector3(local.x / e.x, local.y / e.y, local.z / e.z);
				return n.magnitude;
			}

			default:
				return float.PositiveInfinity;
		}
	}

	/// <summary>
	/// Inigo Quilez capsule SDF normalized by radius (0 at medial segment, 1 at surface).
	/// </summary>
	static float NormalizedCapsuleDistance(Vector3 local, float rad, float totalHeight)
	{
		float r = Mathf.Max(0.01f, rad);
		float h = Mathf.Max(r * 2f, totalHeight);
		float halfLine = h * 0.5f - r;

		// Segment from (0,-halfLine,0) to (0,+halfLine,0).
		Vector3 pa = local - new Vector3(0f, -halfLine, 0f);
		Vector3 ba = new Vector3(0f, 2f * halfLine, 0f);
		float baLenSq = Vector3.Dot(ba, ba);
		float t = baLenSq > 1e-12f ? Mathf.Clamp01(Vector3.Dot(pa, ba) / baLenSq) : 0f;
		Vector3 closest = new Vector3(0f, -halfLine, 0f) + ba * t;
		return (local - closest).magnitude / r;
	}

	static Vector3 SafeExtents(Vector3 value)
	{
		return new Vector3(
			Mathf.Max(0.01f, value.x),
			Mathf.Max(0.01f, value.y),
			Mathf.Max(0.01f, value.z));
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
			terrain = Object.FindObjectOfType<MarchingCubesVolume>();
		}
	}
}
