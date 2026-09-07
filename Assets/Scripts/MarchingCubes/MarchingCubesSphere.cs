using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal educational Marching Cubes implementation with editable density.
///
/// Initialization:
///   GetInitialDensity() → float[,,] densities → GenerateMesh() → Mesh
///
/// Runtime digging:
///   ModifyTerrain() → nearby densities change → GenerateMesh() → Mesh + MeshCollider
///
/// Vertex sharing (Dictionary) is unchanged — modification only edits the density field.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class MarchingCubesSphere : MonoBehaviour
{
	[Header("Grid")]
	[Tooltip("Number of sample points along each axis (16 → 16×16×16 points, 15×15×15 cubes).")]
	[Range(2, 64)]
	[SerializeField] int numPointsPerAxis = 16;

	[Tooltip("World-space distance between neighboring sample points.")]
	[SerializeField] float spacing = 1f;

	[Header("Sphere Density Field")]
	[Tooltip("Center of the procedural sphere in local grid space.")]
	[SerializeField] Vector3 center = new Vector3(7.5f, 7.5f, 7.5f);

	[Tooltip("Radius of the sphere. Initial density = radius - distance(position, center) + noise.")]
	[SerializeField] float radius = 6f;

	[Tooltip("Perlin frequency for the initial density noise.")]
	[SerializeField] float noiseScale = 0.2f;

	[Tooltip("How strongly noise displaces the sphere surface. Noise is centered around 0.")]
	[SerializeField] float noiseStrength = 1.5f;

	[Header("Mesh")]
	[Tooltip("Updated after every mesh rebuild so Raycasts hit the new surface.")]
	[SerializeField] MeshCollider meshCollider;

	[Tooltip("Rebuild the mesh automatically when values change in the Inspector.")]
	[SerializeField] bool generateOnValidate = true;

	public int NumPointsPerAxis => numPointsPerAxis;
	public float Spacing => spacing;

	/// <summary>
	/// Stored density field. Sample point (x,y,z) has position (x,y,z)*spacing
	/// and density densities[x,y,z]. Digging edits these values in place;
	/// the array size never changes.
	/// </summary>
	float[,,] densities;

	// -------------------------------------------------------------------------
	// Corner numbering (matches the educational diagram and standard MC tables):
	//
	//              7────────────6
	//             /|           /|
	//            / |          / |
	//           4────────────5  |
	//           |  |           | |
	//           |  3───────────|─2
	//           | /            |/
	//           |/             |/
	//           0──────────────1
	//
	// A Sample Point is a grid location with a position and a density value.
	// A Cube is the volume between 8 neighboring sample points.
	// With N points per axis there are (N - 1) cubes per axis, because each
	// cube spans from sample i to sample i+1.
	// -------------------------------------------------------------------------
	static readonly Vector3Int[] CornerOffsets =
	{
		new Vector3Int(0, 0, 0), // 0
		new Vector3Int(1, 0, 0), // 1
		new Vector3Int(1, 0, 1), // 2
		new Vector3Int(0, 0, 1), // 3
		new Vector3Int(0, 1, 0), // 4
		new Vector3Int(1, 1, 0), // 5
		new Vector3Int(1, 1, 1), // 6
		new Vector3Int(0, 1, 1)  // 7
	};

	/// <summary>
	/// Each of the 12 cube edges connects two corners.
	/// Edge i → Corner edgeConnections[i, 0] to Corner edgeConnections[i, 1].
	/// </summary>
	static readonly int[,] EdgeConnections =
	{
		{ 0, 1 }, // edge 0
		{ 1, 2 }, // edge 1
		{ 2, 3 }, // edge 2
		{ 3, 0 }, // edge 3
		{ 4, 5 }, // edge 4
		{ 5, 6 }, // edge 5
		{ 6, 7 }, // edge 6
		{ 7, 4 }, // edge 7
		{ 0, 4 }, // edge 8
		{ 1, 5 }, // edge 9
		{ 2, 6 }, // edge 10
		{ 3, 7 }  // edge 11
	};

	MeshFilter meshFilter;
	Mesh generatedMesh;

	void Awake()
	{
		meshFilter = GetComponent<MeshFilter>();
		if (meshCollider == null)
		{
			meshCollider = GetComponent<MeshCollider>();
		}
	}

	void Start()
	{
		InitializeDensity();
		GenerateMesh();
	}

	void OnValidate()
	{
		numPointsPerAxis = Mathf.Max(2, numPointsPerAxis);
		spacing = Mathf.Max(0.001f, spacing);
		radius = Mathf.Max(0.01f, radius);

#if UNITY_EDITOR
		if (!generateOnValidate || !isActiveAndEnabled)
		{
			return;
		}

		// Defer so Unity finishes applying Inspector changes before we rebuild.
		UnityEditor.EditorApplication.delayCall += () =>
		{
			if (this != null)
			{
				InitializeDensity();
				GenerateMesh();
			}
		};
#endif
	}

	/// <summary>
	/// Fill densities[,,] once from the procedural sphere.
	/// After this, Marching Cubes and digging only read/write the array.
	/// </summary>
	public void InitializeDensity()
	{
		densities = new float[numPointsPerAxis, numPointsPerAxis, numPointsPerAxis];

		for (int x = 0; x < numPointsPerAxis; x++)
		{
			for (int y = 0; y < numPointsPerAxis; y++)
			{
				for (int z = 0; z < numPointsPerAxis; z++)
				{
					Vector3 position = new Vector3(x, y, z) * spacing;
					densities[x, y, z] = GetInitialDensity(position);
				}
			}
		}
	}

	/// <summary>
	/// Procedural sphere + 3-axis averaged Perlin noise, used only at initialization.
	/// density &gt; 0 → inside, density &lt; 0 → outside, density = 0 → surface.
	/// </summary>
	float GetInitialDensity(Vector3 position)
	{
		float sphere = radius - Vector3.Distance(position, center);

		float noise = 0f;
		noise += Mathf.PerlinNoise(position.x * noiseScale, position.y * noiseScale);
		noise += Mathf.PerlinNoise(position.y * noiseScale, position.z * noiseScale);
		noise += Mathf.PerlinNoise(position.x * noiseScale, position.z * noiseScale);
		noise /= 3f;

		// Perlin is 0–1; center at 0.5 so noise wrinkles the surface in and out.
		return sphere + (noise - 0.5f) * noiseStrength;
	}

	[ContextMenu("Generate Mesh")]
	public void GenerateMesh()
	{
		if (meshFilter == null)
		{
			meshFilter = GetComponent<MeshFilter>();
		}

		if (densities == null)
		{
			InitializeDensity();
		}

		var vertices = new List<Vector3>();
		var triangles = new List<int>();

		// Maps a vertex position → its index in `vertices`.
		// Adjacent cubes often share the same edge intersection; the dictionary
		// reuses that vertex instead of appending a duplicate.
		var vertexLookup = new Dictionary<Vector3, int>();

		// numPointsPerAxis sample points per axis → (numPointsPerAxis - 1) cubes.
		// Example: 16 points form 15 cubes along each axis.
		for (int x = 0; x < numPointsPerAxis - 1; x++)
		{
			for (int y = 0; y < numPointsPerAxis - 1; y++)
			{
				for (int z = 0; z < numPointsPerAxis - 1; z++)
				{
					ProcessCube(new Vector3Int(x, y, z), vertices, triangles, vertexLookup);
				}
			}
		}

		if (generatedMesh == null)
		{
			generatedMesh = new Mesh { name = "MarchingCubesSphere" };
		}
		else
		{
			generatedMesh.Clear();
		}

		// Default UInt16 indices wrap past 65535 verts → stretched "far away" triangles.
		generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		generatedMesh.SetVertices(vertices);
		generatedMesh.SetTriangles(triangles, 0);
		generatedMesh.RecalculateNormals();
		generatedMesh.RecalculateBounds();

		meshFilter.sharedMesh = generatedMesh;

		// Refresh collider so the next mouse Raycast hits the updated surface.
		if (meshCollider == null)
		{
			meshCollider = GetComponent<MeshCollider>();
		}

		if (meshCollider != null)
		{
			meshCollider.sharedMesh = null;
			meshCollider.sharedMesh = generatedMesh;
		}
	}

	/// <summary>
	/// Modify the density field with a smooth spherical brush, then rebuild the mesh.
	/// Positive <paramref name="strength"/> digs (removes terrain);
	/// negative strength grows (adds terrain).
	///
	/// Does not delete sample points — only changes densities[x,y,z].
	/// </summary>
	public void ModifyTerrain(Vector3 worldPosition, float brushRadius, float strength)
	{
		if (densities == null)
		{
			InitializeDensity();
		}

		// Raycast hit is world-space; density samples live in this object's local space.
		Vector3 localPosition = transform.InverseTransformPoint(worldPosition);

		// Axis-aligned bounds of the brush in sample-index space.
		int minX = Mathf.FloorToInt((localPosition.x - brushRadius) / spacing);
		int maxX = Mathf.CeilToInt((localPosition.x + brushRadius) / spacing);
		int minY = Mathf.FloorToInt((localPosition.y - brushRadius) / spacing);
		int maxY = Mathf.CeilToInt((localPosition.y + brushRadius) / spacing);
		int minZ = Mathf.FloorToInt((localPosition.z - brushRadius) / spacing);
		int maxZ = Mathf.CeilToInt((localPosition.z + brushRadius) / spacing);

		minX = Mathf.Clamp(minX, 0, numPointsPerAxis - 1);
		maxX = Mathf.Clamp(maxX, 0, numPointsPerAxis - 1);
		minY = Mathf.Clamp(minY, 0, numPointsPerAxis - 1);
		maxY = Mathf.Clamp(maxY, 0, numPointsPerAxis - 1);
		minZ = Mathf.Clamp(minZ, 0, numPointsPerAxis - 1);
		maxZ = Mathf.Clamp(maxZ, 0, numPointsPerAxis - 1);

		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				for (int z = minZ; z <= maxZ; z++)
				{
					Vector3 samplePosition = new Vector3(x, y, z) * spacing;
					float distance = Vector3.Distance(samplePosition, localPosition);

					if (distance > brushRadius)
					{
						continue;
					}

					// Smooth spherical falloff: strong at center, soft at the edge.
					float t = 1f - distance / brushRadius;
					t = Mathf.Clamp01(t);
					t = t * t * (3f - 2f * t); // smoothstep

					densities[x, y, z] -= strength * t;
				}
			}
		}

		GenerateMesh();
	}

	/// <summary>
	/// Process one cube: read 8 corner densities from the stored field,
	/// look up which edges are cut, interpolate vertices, emit triangles.
	/// </summary>
	void ProcessCube(
		Vector3Int cubePosition,
		List<Vector3> vertices,
		List<int> triangles,
		Dictionary<Vector3, int> vertexLookup)
	{
		var cornerPositions = new Vector3[8];
		var cornerDensities = new float[8];

		// Sample Point = position + stored density at each of the 8 cube corners.
		for (int i = 0; i < 8; i++)
		{
			Vector3Int sampleCoord = cubePosition + CornerOffsets[i];
			cornerPositions[i] = new Vector3(sampleCoord.x, sampleCoord.y, sampleCoord.z) * spacing;
			cornerDensities[i] = densities[sampleCoord.x, sampleCoord.y, sampleCoord.z];
		}

		// cubeIndex is an 8-bit mask: bit i is set if corner i is inside (density > 0).
		// Values range from 0 (all outside) to 255 (all inside).
		int cubeIndex = CalculateCubeIndex(cornerDensities);

		// edgeTable tells WHICH of the 12 edges the isosurface crosses.
		int edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];
		if (edgeMask == 0)
		{
			// No edges intersected → this cube is entirely inside or outside.
			return;
		}

		// Cache the interpolated vertex on each active edge.
		// Mesh vertices live ON CUBE EDGES (where density crosses 0),
		// not on the sample points themselves (which are usually strictly + or −).
		//
		// Decide active edges from corner density signs (not only edgeMask) so a
		// bad lookup-table entry can never leave an edge at Vector3.zero — that
		// produced spikes where several triangles shared the origin vertex.
		var edgeVertices = new Vector3[12];
		for (int edgeIndex = 0; edgeIndex < 12; edgeIndex++)
		{
			int cornerA = EdgeConnections[edgeIndex, 0];
			int cornerB = EdgeConnections[edgeIndex, 1];
			float densityA = cornerDensities[cornerA];
			float densityB = cornerDensities[cornerB];

			// No zero-crossing if both corners are inside or both outside.
			if ((densityA > 0f) == (densityB > 0f))
			{
				continue;
			}

			edgeVertices[edgeIndex] = Interpolate(
				cornerPositions[cornerA],
				cornerPositions[cornerB],
				densityA,
				densityB);
		}

		// triTable tells HOW those edge intersections form triangles.
		// Each group of 3 edge indices = one triangle. -1 marks the end.
		for (int i = 0; MarchingCubesTables.TriTable[cubeIndex, i] != -1; i += 3)
		{
			int edgeA = MarchingCubesTables.TriTable[cubeIndex, i];
			int edgeB = MarchingCubesTables.TriTable[cubeIndex, i + 1];
			int edgeC = MarchingCubesTables.TriTable[cubeIndex, i + 2];

			triangles.Add(GetOrAddVertex(edgeVertices[edgeA], vertices, vertexLookup));
			triangles.Add(GetOrAddVertex(edgeVertices[edgeB], vertices, vertexLookup));
			triangles.Add(GetOrAddVertex(edgeVertices[edgeC], vertices, vertexLookup));
		}
	}

	/// <summary>
	/// Return the index of <paramref name="position"/> in <paramref name="vertices"/>.
	/// If it already exists in the lookup dictionary, reuse that index;
	/// otherwise append a new vertex and register it.
	///
	/// Shared cube edges produce identical interpolated positions, so neighboring
	/// cubes (and multiple triangles) can share one vertex entry.
	/// </summary>
	static int GetOrAddVertex(
		Vector3 position,
		List<Vector3> vertices,
		Dictionary<Vector3, int> vertexLookup)
	{
		if (vertexLookup.TryGetValue(position, out int existingIndex))
		{
			return existingIndex;
		}

		int newIndex = vertices.Count;
		vertices.Add(position);
		vertexLookup.Add(position, newIndex);
		return newIndex;
	}

	/// <summary>
	/// Build the 8-bit configuration index from corner densities.
	/// Bit i set ⇒ corner i is inside the surface (density &gt; 0).
	/// </summary>
	static int CalculateCubeIndex(float[] cornerDensities)
	{
		int cubeIndex = 0;

		for (int i = 0; i < 8; i++)
		{
			if (cornerDensities[i] > 0f)
			{
				cubeIndex |= 1 << i;
			}
		}

		return cubeIndex;
	}

	/// <summary>
	/// Linearly interpolate along an edge to find where density = 0.
	///
	///   A (+) ---------------- B (−)
	///              ↑
	///         surface crossing
	/// </summary>
	static Vector3 Interpolate(Vector3 positionA, Vector3 positionB, float densityA, float densityB)
	{
		// Avoid divide-by-zero if densities are nearly equal.
		if (Mathf.Abs(densityA - densityB) < 0.00001f)
		{
			return positionA;
		}

		float t = densityA / (densityA - densityB);
		t = Mathf.Clamp01(t);
		return positionA + (positionB - positionA) * t;
	}
}
