using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Educational Marching Cubes volume with a global density field and lazy chunked meshes.
///
/// densities[,,] starts at 0. Each sim tick: Decay toward 0 → Overlay inject → Rebuild dirty chunks.
/// Brush edits densities immediately.
/// </summary>
public class MarchingCubesVolume : MonoBehaviour
{
	[Header("Grid")]
	[Tooltip("Number of sample points along each axis. Bounds the density volume.")]
	[Range(2, 64)]
	[SerializeField] int numPointsPerAxis = 16;

	[Tooltip("Distance between neighboring sample points.")]
	[SerializeField] float spacing = 1f;

	[Tooltip("Cubes per chunk along each axis.")]
	[Range(2, 32)]
	[SerializeField] int chunkSizeCubes = 8;

	[Header("Mesh")]
	[Tooltip("Material assigned to lazily created chunk renderers.")]
	[SerializeField] Material chunkMaterial;

	[Tooltip("When off, chunk MeshColliders are cleared/disabled (visual mesh only). Useful if Volume is parented under a Dynamic Rigidbody.")]
	[SerializeField] bool generateChunkColliders = true;

	[Tooltip("When grid size changes in the Inspector, reset density to zeros and clear chunks.")]
	[SerializeField] bool resetOnValidate = true;

	[Header("Density Simulation")]
	[Tooltip("How fast densities move toward 0 (units per second).")]
	[SerializeField] float decaySpeed = 0.5f;

	[Tooltip("Seconds between decay+inject simulation ticks.")]
	[SerializeField] float simulationInterval = 0.05f;

	[Tooltip("Densities with |d| <= this are treated as 0 and skipped during decay.")]
	[SerializeField] float densityEpsilon = 0.0001f;

	[Tooltip("Force every sample on the volume shell to a large negative density so the isosurface never reaches the grid edge.")]
	[SerializeField] bool forceBoundaryEmpty = true;

	[Tooltip("Effective density used for shell samples when Force Boundary Empty is on. Keep strongly negative (positive = solid).")]
	[SerializeField] float boundaryDensity = -1e6f;

	[Header("Gizmos")]
	[Tooltip("Always draw the density volume bounds in the Scene view.")]
	[SerializeField] bool drawVolumeBounds = true;

	[Tooltip("Always draw bounds of existing (live) chunks in the Scene view.")]
	[SerializeField] bool drawChunkBounds = true;

	[Tooltip("Color for the full density-grid wire cube.")]
	[SerializeField] Color volumeGizmoColor = new Color(0.2f, 0.9f, 1f, 0.85f);

	[Tooltip("Color for live chunk wire cubes.")]
	[SerializeField] Color chunkGizmoColor = new Color(0.3f, 1f, 0.4f, 0.55f);

	public int NumPointsPerAxis => numPointsPerAxis;
	public float Spacing => spacing;
	public int ChunkSizeCubes => chunkSizeCubes;

	/// <summary>Half-size of the grid along one axis in local units.</summary>
	public float GridHalfExtent => (numPointsPerAxis - 1) * spacing * 0.5f;

	/// <summary>
	/// Sample index (0..N-1) → local position, with the grid centered on this transform.
	/// </summary>
	public Vector3 IndexToLocal(int x, int y, int z)
	{
		float half = GridHalfExtent;
		return new Vector3(x, y, z) * spacing - new Vector3(half, half, half);
	}

	/// <summary>
	/// Convert a local-space axis-aligned sphere/box into clamped sample indices.
	/// </summary>
	void LocalBoundsToIndices(
		Vector3 localCenter,
		float localRadius,
		out int minX, out int maxX,
		out int minY, out int maxY,
		out int minZ, out int maxZ)
	{
		float half = GridHalfExtent;

		minX = Mathf.FloorToInt((localCenter.x - localRadius + half) / spacing);
		maxX = Mathf.CeilToInt((localCenter.x + localRadius + half) / spacing);
		minY = Mathf.FloorToInt((localCenter.y - localRadius + half) / spacing);
		maxY = Mathf.CeilToInt((localCenter.y + localRadius + half) / spacing);
		minZ = Mathf.FloorToInt((localCenter.z - localRadius + half) / spacing);
		maxZ = Mathf.CeilToInt((localCenter.z + localRadius + half) / spacing);

		minX = Mathf.Clamp(minX, 0, numPointsPerAxis - 1);
		maxX = Mathf.Clamp(maxX, 0, numPointsPerAxis - 1);
		minY = Mathf.Clamp(minY, 0, numPointsPerAxis - 1);
		maxY = Mathf.Clamp(maxY, 0, numPointsPerAxis - 1);
		minZ = Mathf.Clamp(minZ, 0, numPointsPerAxis - 1);
		maxZ = Mathf.Clamp(maxZ, 0, numPointsPerAxis - 1);
	}

	/// <summary>
	/// Global density field. Starts at 0.
	/// Brush edits immediately; overlays inject over time; decay pulls values toward 0.
	/// </summary>
	float[,,] densities;

	readonly List<DensityShapeOverlay> densityOverlays = new List<DensityShapeOverlay>();
	readonly Dictionary<Vector3Int, MarchingCubesChunk> chunks = new Dictionary<Vector3Int, MarchingCubesChunk>();
	readonly HashSet<MarchingCubesChunk> dirtyChunks = new HashSet<MarchingCubesChunk>();

	int chunksPerAxis;
	int cubesPerAxis;
	int cachedPointsPerAxis = -1;
	int cachedChunkSize = -1;
	float simulationTimer;

	static readonly Vector3Int[] CornerOffsets =
	{
		new Vector3Int(0, 0, 0),
		new Vector3Int(1, 0, 0),
		new Vector3Int(1, 0, 1),
		new Vector3Int(0, 0, 1),
		new Vector3Int(0, 1, 0),
		new Vector3Int(1, 1, 0),
		new Vector3Int(1, 1, 1),
		new Vector3Int(0, 1, 1)
	};

	static readonly int[,] EdgeConnections =
	{
		{ 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
		{ 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
		{ 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
	};

	void Awake()
	{
		DisableRootMeshComponents();
	}

	void Start()
	{
		RefreshGridMetrics();
		InitializeDensity();
		DisableRootMeshComponents();
		simulationTimer = 0f;
	}

	void Update()
	{
		if (!Application.isPlaying || densities == null)
		{
			return;
		}

		simulationTimer += Time.deltaTime;
		if (simulationTimer < simulationInterval)
		{
			return;
		}

		float dt = simulationTimer;
		simulationTimer = 0f;

		// Order: Decay → Inject → Rebuild (inject can sustain blobs against decay).
		DecayDensities(dt);
		InjectFromOverlays(dt);
		RebuildDirtyChunks();
	}

	void OnValidate()
	{
		numPointsPerAxis = Mathf.Max(2, numPointsPerAxis);
		spacing = Mathf.Max(0.001f, spacing);
		chunkSizeCubes = Mathf.Max(2, chunkSizeCubes);
		decaySpeed = Mathf.Max(0f, decaySpeed);
		simulationInterval = Mathf.Max(0.01f, simulationInterval);
		densityEpsilon = Mathf.Max(0f, densityEpsilon);
		if (boundaryDensity >= 0f)
		{
			boundaryDensity = -1e6f;
		}

#if UNITY_EDITOR
		UnityEditor.EditorApplication.delayCall += () =>
		{
			if (this == null)
			{
				return;
			}

			ApplyColliderSettingToExistingChunks();

			if (!resetOnValidate || !isActiveAndEnabled)
			{
				return;
			}

			bool gridChanged =
				cachedPointsPerAxis != numPointsPerAxis ||
				cachedChunkSize != chunkSizeCubes ||
				densities == null ||
				densities.GetLength(0) != numPointsPerAxis;

			if (!gridChanged)
			{
				return;
			}

			ClearChunks();
			RefreshGridMetrics();
			InitializeDensity();
			DisableRootMeshComponents();
		};
#else
		ApplyColliderSettingToExistingChunks();
#endif
	}

	void ApplyColliderSettingToExistingChunks()
	{
		foreach (KeyValuePair<Vector3Int, MarchingCubesChunk> pair in chunks)
		{
			if (pair.Value != null)
			{
				pair.Value.SetCollidersEnabled(generateChunkColliders);
			}
		}
	}

	public void RegisterOverlay(DensityShapeOverlay overlay)
	{
		if (overlay == null || densityOverlays.Contains(overlay))
		{
			return;
		}

		densityOverlays.Add(overlay);
	}

	public void UnregisterOverlay(DensityShapeOverlay overlay)
	{
		if (overlay == null)
		{
			return;
		}

		densityOverlays.Remove(overlay);
	}

	/// <summary>
	/// Dirty chunks covered by one or two world spheres, then rebuild.
	/// </summary>
	public void RequestOverlayRegionUpdate(
		Vector3 worldCenterA,
		float worldRadiusA,
		Vector3 worldCenterB,
		float worldRadiusB)
	{
		if (!isActiveAndEnabled)
		{
			return;
		}

		EnsureDensityReady();
		MarkWorldSphereDirty(worldCenterA, worldRadiusA);
		MarkWorldSphereDirty(worldCenterB, worldRadiusB);
		RebuildDirtyChunks();
	}

	/// <summary>
	/// Rebuild all currently existing chunks (does not spawn a full grid).
	/// </summary>
	public void RequestMeshUpdate()
	{
		if (!isActiveAndEnabled)
		{
			return;
		}

		EnsureDensityReady();
		MarkExistingChunksDirty();
		RebuildDirtyChunks();
	}

	static bool IsBoundarySample(int x, int y, int z, int n)
	{
		return x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == n - 1;
	}

	float GetEffectiveDensity(int x, int y, int z)
	{
		if (forceBoundaryEmpty && IsBoundarySample(x, y, z, numPointsPerAxis))
		{
			return boundaryDensity;
		}

		return densities[x, y, z];
	}

	/// <summary>
	/// Move densities toward 0. Skips |d| &lt;= epsilon. Marks dirty only where values change.
	/// </summary>
	void DecayDensities(float dt)
	{
		if (decaySpeed <= 0f || dt <= 0f)
		{
			return;
		}

		float step = decaySpeed * dt;
		bool any = false;
		int minX = numPointsPerAxis;
		int maxX = -1;
		int minY = numPointsPerAxis;
		int maxY = -1;
		int minZ = numPointsPerAxis;
		int maxZ = -1;

		for (int x = 0; x < numPointsPerAxis; x++)
		{
			for (int y = 0; y < numPointsPerAxis; y++)
			{
				for (int z = 0; z < numPointsPerAxis; z++)
				{
					if (forceBoundaryEmpty && IsBoundarySample(x, y, z, numPointsPerAxis))
					{
						continue;
					}

					float d = densities[x, y, z];
					if (Mathf.Abs(d) <= densityEpsilon)
					{
						if (d != 0f)
						{
							densities[x, y, z] = 0f;
							any = true;
							if (x < minX) minX = x;
							if (x > maxX) maxX = x;
							if (y < minY) minY = y;
							if (y > maxY) maxY = y;
							if (z < minZ) minZ = z;
							if (z > maxZ) maxZ = z;
						}

						continue;
					}

					float next = Mathf.MoveTowards(d, 0f, step);
					if (Mathf.Abs(next) <= densityEpsilon)
					{
						next = 0f;
					}

					if (next == d)
					{
						continue;
					}

					densities[x, y, z] = next;
					any = true;
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
					if (z < minZ) minZ = z;
					if (z > maxZ) maxZ = z;
				}
			}
		}

		if (any)
		{
			MarkSamplesDirty(minX, maxX, minY, maxY, minZ, maxZ);
		}
	}

	/// <summary>
	/// Overlays write into densities with center-weighted falloff, clamped to each overlay's max.
	/// </summary>
	void InjectFromOverlays(float dt)
	{
		if (dt <= 0f || densityOverlays.Count == 0)
		{
			return;
		}

		for (int i = 0; i < densityOverlays.Count; i++)
		{
			DensityShapeOverlay overlay = densityOverlays[i];
			if (overlay == null || !overlay.isActiveAndEnabled)
			{
				continue;
			}

			InjectFromOverlay(overlay, dt);
		}
	}

	void InjectFromOverlay(DensityShapeOverlay overlay, float dt)
	{
		float worldRadius = overlay.GetWorldBoundsRadius();
		float injectRate = overlay.InjectRate;
		float maxDensity = overlay.MaxDensity;
		if (worldRadius <= 0f || injectRate == 0f)
		{
			return;
		}

		Vector3 localCenter = transform.InverseTransformPoint(overlay.transform.position);
		float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
		float localRadius = worldRadius / Mathf.Max(0.0001f, scale);

		LocalBoundsToIndices(
			localCenter,
			localRadius,
			out int minX, out int maxX,
			out int minY, out int maxY,
			out int minZ, out int maxZ);

		bool any = false;
		int dirtyMinX = numPointsPerAxis;
		int dirtyMaxX = -1;
		int dirtyMinY = numPointsPerAxis;
		int dirtyMaxY = -1;
		int dirtyMinZ = numPointsPerAxis;
		int dirtyMaxZ = -1;

		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				for (int z = minZ; z <= maxZ; z++)
				{
					if (forceBoundaryEmpty && IsBoundarySample(x, y, z, numPointsPerAxis))
					{
						continue;
					}

					Vector3 localSample = IndexToLocal(x, y, z);
					Vector3 worldSample = transform.TransformPoint(localSample);
					float falloff = overlay.EvaluateFalloff(worldSample);
					if (falloff <= 0f || !float.IsFinite(falloff))
					{
						continue;
					}

					float d = densities[x, y, z];
					float next;
					if (overlay.StampDensity)
					{
						// Immediate shape stamp: density matches falloff * max each tick.
						float target = injectRate > 0f
							? maxDensity * falloff
							: -maxDensity * falloff;
						next = injectRate > 0f
							? Mathf.Max(d, target)
							: Mathf.Min(d, target);
					}
					else
					{
						next = d + injectRate * falloff * dt;
						if (injectRate > 0f)
						{
							next = Mathf.Min(next, maxDensity);
						}
						else
						{
							next = Mathf.Max(next, -maxDensity);
						}
					}

					if (!float.IsFinite(next) || Mathf.Abs(next - d) <= densityEpsilon)
					{
						continue;
					}

					densities[x, y, z] = next;
					any = true;
					if (x < dirtyMinX) dirtyMinX = x;
					if (x > dirtyMaxX) dirtyMaxX = x;
					if (y < dirtyMinY) dirtyMinY = y;
					if (y > dirtyMaxY) dirtyMaxY = y;
					if (z < dirtyMinZ) dirtyMinZ = z;
					if (z > dirtyMaxZ) dirtyMaxZ = z;
				}
			}
		}

		if (any)
		{
			MarkSamplesDirty(dirtyMinX, dirtyMaxX, dirtyMinY, dirtyMaxY, dirtyMinZ, dirtyMaxZ);
		}
	}

	public void InitializeDensity()
	{
		densities = new float[numPointsPerAxis, numPointsPerAxis, numPointsPerAxis];
		// Empty field: no procedural sphere. Surface appears via brush grow / overlays.
	}

	[ContextMenu("Generate Mesh")]
	public void GenerateMesh()
	{
		EnsureDensityReady();
		MarkExistingChunksDirty();
		RebuildDirtyChunks();
	}

	/// <summary>
	/// Modify base densities with a spherical brush, spawn chunks as needed, rebuild dirty ones.
	/// Positive strength digs; negative grows.
	/// </summary>
	public void ModifyTerrain(Vector3 worldPosition, float brushRadius, float strength)
	{
		EnsureDensityReady();

		Vector3 localPosition = transform.InverseTransformPoint(worldPosition);

		LocalBoundsToIndices(
			localPosition,
			brushRadius,
			out int minX, out int maxX,
			out int minY, out int maxY,
			out int minZ, out int maxZ);

		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				for (int z = minZ; z <= maxZ; z++)
				{
					if (forceBoundaryEmpty && IsBoundarySample(x, y, z, numPointsPerAxis))
					{
						continue;
					}

					Vector3 samplePosition = IndexToLocal(x, y, z);
					float distance = Vector3.Distance(samplePosition, localPosition);
					if (distance > brushRadius)
					{
						continue;
					}

					float t = 1f - distance / brushRadius;
					t = Mathf.Clamp01(t);
					t = t * t * (3f - 2f * t);

					densities[x, y, z] -= strength * t;
				}
			}
		}

		MarkSamplesDirty(minX, maxX, minY, maxY, minZ, maxZ);
		RebuildDirtyChunks();
	}

	void EnsureDensityReady()
	{
		RefreshGridMetrics();
		if (densities == null ||
		    densities.GetLength(0) != numPointsPerAxis ||
		    densities.GetLength(1) != numPointsPerAxis ||
		    densities.GetLength(2) != numPointsPerAxis)
		{
			InitializeDensity();
		}

		DisableRootMeshComponents();
	}

	void RefreshGridMetrics()
	{
		cubesPerAxis = numPointsPerAxis - 1;
		chunksPerAxis = Mathf.Max(1, Mathf.CeilToInt(cubesPerAxis / (float)chunkSizeCubes));
		cachedPointsPerAxis = numPointsPerAxis;
		cachedChunkSize = chunkSizeCubes;
	}

	void ClearChunks()
	{
		dirtyChunks.Clear();

		foreach (KeyValuePair<Vector3Int, MarchingCubesChunk> pair in chunks)
		{
			if (pair.Value == null)
			{
				continue;
			}

			if (Application.isPlaying)
			{
				Destroy(pair.Value.gameObject);
			}
			else
			{
				DestroyImmediate(pair.Value.gameObject);
			}
		}

		chunks.Clear();

		for (int i = transform.childCount - 1; i >= 0; i--)
		{
			Transform child = transform.GetChild(i);
			if (child.GetComponent<MarchingCubesChunk>() == null)
			{
				continue;
			}

			if (Application.isPlaying)
			{
				Destroy(child.gameObject);
			}
			else
			{
				DestroyImmediate(child.gameObject);
			}
		}
	}

	void DisableRootMeshComponents()
	{
		var rootFilter = GetComponent<MeshFilter>();
		if (rootFilter != null)
		{
			rootFilter.sharedMesh = null;
		}

		var rootCollider = GetComponent<MeshCollider>();
		if (rootCollider != null)
		{
			rootCollider.sharedMesh = null;
			rootCollider.enabled = false;
		}

		var rootRenderer = GetComponent<MeshRenderer>();
		if (rootRenderer != null)
		{
			rootRenderer.enabled = false;
		}
	}

	void MarkExistingChunksDirty()
	{
		dirtyChunks.Clear();
		foreach (KeyValuePair<Vector3Int, MarchingCubesChunk> pair in chunks)
		{
			if (pair.Value == null)
			{
				continue;
			}

			pair.Value.Dirty = true;
			dirtyChunks.Add(pair.Value);
		}
	}

	void MarkWorldSphereDirty(Vector3 worldCenter, float worldRadius)
	{
		Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
		float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
		float localRadius = worldRadius / Mathf.Max(0.0001f, scale);

		LocalBoundsToIndices(
			localCenter,
			localRadius,
			out int minX, out int maxX,
			out int minY, out int maxY,
			out int minZ, out int maxZ);

		MarkSamplesDirty(minX, maxX, minY, maxY, minZ, maxZ);
	}

	/// <summary>
	/// Mark chunks that use samples in the AABB. Creates missing chunks on demand.
	/// </summary>
	void MarkSamplesDirty(int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
	{
		RefreshGridMetrics();
		if (cubesPerAxis <= 0)
		{
			return;
		}

		int cubeMinX = Mathf.Clamp(minX - 1, 0, cubesPerAxis - 1);
		int cubeMaxX = Mathf.Clamp(maxX, 0, cubesPerAxis - 1);
		int cubeMinY = Mathf.Clamp(minY - 1, 0, cubesPerAxis - 1);
		int cubeMaxY = Mathf.Clamp(maxY, 0, cubesPerAxis - 1);
		int cubeMinZ = Mathf.Clamp(minZ - 1, 0, cubesPerAxis - 1);
		int cubeMaxZ = Mathf.Clamp(maxZ, 0, cubesPerAxis - 1);

		int chunkMinX = cubeMinX / chunkSizeCubes;
		int chunkMaxX = cubeMaxX / chunkSizeCubes;
		int chunkMinY = cubeMinY / chunkSizeCubes;
		int chunkMaxY = cubeMaxY / chunkSizeCubes;
		int chunkMinZ = cubeMinZ / chunkSizeCubes;
		int chunkMaxZ = cubeMaxZ / chunkSizeCubes;

		for (int cx = chunkMinX; cx <= chunkMaxX; cx++)
		{
			for (int cy = chunkMinY; cy <= chunkMaxY; cy++)
			{
				for (int cz = chunkMinZ; cz <= chunkMaxZ; cz++)
				{
					MarchingCubesChunk chunk = GetOrCreateChunk(new Vector3Int(cx, cy, cz));
					if (chunk == null)
					{
						continue;
					}

					chunk.Dirty = true;
					dirtyChunks.Add(chunk);
				}
			}
		}
	}

	MarchingCubesChunk GetOrCreateChunk(Vector3Int coord)
	{
		if (coord.x < 0 || coord.y < 0 || coord.z < 0 ||
		    coord.x >= chunksPerAxis || coord.y >= chunksPerAxis || coord.z >= chunksPerAxis)
		{
			return null;
		}

		if (chunks.TryGetValue(coord, out MarchingCubesChunk existing))
		{
			if (existing != null)
			{
				return existing;
			}

			chunks.Remove(coord);
		}

		Material material = chunkMaterial;
		if (material == null)
		{
			var rootRenderer = GetComponent<MeshRenderer>();
			if (rootRenderer != null)
			{
				material = rootRenderer.sharedMaterial;
			}
		}

		var go = new GameObject();
		go.hideFlags = HideFlags.DontSave;
		go.transform.SetParent(transform, false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;

		var chunk = go.AddComponent<MarchingCubesChunk>();
		chunk.Initialize(coord, material, gameObject.layer, generateChunkColliders);
		chunks[coord] = chunk;
		return chunk;
	}

	void RebuildDirtyChunks()
	{
		if (dirtyChunks.Count == 0)
		{
			return;
		}

		var toRebuild = new List<MarchingCubesChunk>(dirtyChunks);
		dirtyChunks.Clear();

		for (int i = 0; i < toRebuild.Count; i++)
		{
			MarchingCubesChunk chunk = toRebuild[i];
			if (chunk == null)
			{
				continue;
			}

			RebuildChunk(chunk);
		}
	}

	void RebuildChunk(MarchingCubesChunk chunk)
	{
		Vector3Int coord = chunk.Coord;
		int cubeStartX = coord.x * chunkSizeCubes;
		int cubeStartY = coord.y * chunkSizeCubes;
		int cubeStartZ = coord.z * chunkSizeCubes;
		int cubeEndX = Mathf.Min(cubeStartX + chunkSizeCubes, cubesPerAxis);
		int cubeEndY = Mathf.Min(cubeStartY + chunkSizeCubes, cubesPerAxis);
		int cubeEndZ = Mathf.Min(cubeStartZ + chunkSizeCubes, cubesPerAxis);

		var vertices = new List<Vector3>();
		var triangles = new List<int>();
		var vertexLookup = new Dictionary<Vector3, int>();

		for (int x = cubeStartX; x < cubeEndX; x++)
		{
			for (int y = cubeStartY; y < cubeEndY; y++)
			{
				for (int z = cubeStartZ; z < cubeEndZ; z++)
				{
					ProcessCube(new Vector3Int(x, y, z), vertices, triangles, vertexLookup);
				}
			}
		}

		chunk.ApplyMesh(vertices, triangles, generateChunkColliders);
	}

	void ProcessCube(
		Vector3Int cubePosition,
		List<Vector3> vertices,
		List<int> triangles,
		Dictionary<Vector3, int> vertexLookup)
	{
		var cornerPositions = new Vector3[8];
		var cornerDensities = new float[8];

		for (int i = 0; i < 8; i++)
		{
			Vector3Int sampleCoord = cubePosition + CornerOffsets[i];
			cornerPositions[i] = IndexToLocal(sampleCoord.x, sampleCoord.y, sampleCoord.z);
			cornerDensities[i] = GetEffectiveDensity(sampleCoord.x, sampleCoord.y, sampleCoord.z);
		}

		int cubeIndex = CalculateCubeIndex(cornerDensities);
		int edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];
		if (edgeMask == 0)
		{
			return;
		}

		var edgeVertices = new Vector3[12];
		for (int edgeIndex = 0; edgeIndex < 12; edgeIndex++)
		{
			int cornerA = EdgeConnections[edgeIndex, 0];
			int cornerB = EdgeConnections[edgeIndex, 1];
			float densityA = cornerDensities[cornerA];
			float densityB = cornerDensities[cornerB];

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

	static Vector3 Interpolate(Vector3 positionA, Vector3 positionB, float densityA, float densityB)
	{
		if (Mathf.Abs(densityA - densityB) < 0.00001f)
		{
			return positionA;
		}

		float t = densityA / (densityA - densityB);
		t = Mathf.Clamp01(t);
		return positionA + (positionB - positionA) * t;
	}

	void OnDrawGizmos()
	{
		Gizmos.matrix = transform.localToWorldMatrix;

		if (drawVolumeBounds && numPointsPerAxis >= 2)
		{
			float extent = (numPointsPerAxis - 1) * spacing;
			Gizmos.color = volumeGizmoColor;
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one * extent);
		}

		if (!drawChunkBounds)
		{
			return;
		}

		Gizmos.color = chunkGizmoColor;
		foreach (KeyValuePair<Vector3Int, MarchingCubesChunk> pair in chunks)
		{
			MarchingCubesChunk chunk = pair.Value;
			if (chunk == null)
			{
				continue;
			}

			Vector3Int coord = chunk.Coord;
			int x0 = coord.x * chunkSizeCubes;
			int y0 = coord.y * chunkSizeCubes;
			int z0 = coord.z * chunkSizeCubes;
			int x1 = Mathf.Min(x0 + chunkSizeCubes, Mathf.Max(1, numPointsPerAxis - 1));
			int y1 = Mathf.Min(y0 + chunkSizeCubes, Mathf.Max(1, numPointsPerAxis - 1));
			int z1 = Mathf.Min(z0 + chunkSizeCubes, Mathf.Max(1, numPointsPerAxis - 1));

			Vector3 min = IndexToLocal(x0, y0, z0);
			Vector3 max = IndexToLocal(x1, y1, z1);
			Gizmos.DrawWireCube((min + max) * 0.5f, max - min);
		}
	}
}
