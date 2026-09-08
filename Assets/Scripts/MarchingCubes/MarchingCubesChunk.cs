using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One mesh chunk of a MarchingCubesVolume volume.
/// Density stays global on the owner; this object only owns mesh + collider.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class MarchingCubesChunk : MonoBehaviour
{
	public Vector3Int Coord { get; private set; }
	public bool Dirty { get; set; } = true;

	MeshFilter meshFilter;
	MeshCollider meshCollider;
	Mesh mesh;
	bool collidersEnabled = true;

	public void Initialize(Vector3Int coord, Material material, int layer, bool enableColliders)
	{
		Coord = coord;
		Dirty = true;
		collidersEnabled = enableColliders;
		gameObject.hideFlags = HideFlags.DontSave;
		gameObject.layer = layer;
		gameObject.name = $"Chunk_{coord.x}_{coord.y}_{coord.z}";

		meshFilter = GetComponent<MeshFilter>();
		meshCollider = GetComponent<MeshCollider>();
		var renderer = GetComponent<MeshRenderer>();
		if (material != null)
		{
			renderer.sharedMaterial = material;
		}

		if (mesh == null)
		{
			mesh = new Mesh { name = gameObject.name };
			mesh.hideFlags = HideFlags.DontSave;
		}

		ApplyColliderState();
	}

	public void ApplyMesh(List<Vector3> vertices, List<int> triangles, bool enableColliders)
	{
		collidersEnabled = enableColliders;

		if (meshFilter == null)
		{
			meshFilter = GetComponent<MeshFilter>();
		}

		if (meshCollider == null)
		{
			meshCollider = GetComponent<MeshCollider>();
		}

		if (mesh == null)
		{
			mesh = new Mesh { name = gameObject.name };
		}

		mesh.Clear();
		mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		meshFilter.sharedMesh = mesh;
		ApplyColliderState();
		Dirty = false;
	}

	/// <summary>Enable or disable this chunk's MeshCollider without rebuilding the render mesh.</summary>
	public void SetCollidersEnabled(bool enableColliders)
	{
		collidersEnabled = enableColliders;
		ApplyColliderState();
	}

	void ApplyColliderState()
	{
		if (meshCollider == null)
		{
			meshCollider = GetComponent<MeshCollider>();
		}

		if (meshCollider == null)
		{
			return;
		}

		if (!collidersEnabled)
		{
			meshCollider.sharedMesh = null;
			meshCollider.enabled = false;
			return;
		}

		Mesh renderMesh = meshFilter != null ? meshFilter.sharedMesh : mesh;
		bool hasTriangles = renderMesh != null && renderMesh.vertexCount > 0;
		meshCollider.enabled = true;
		meshCollider.sharedMesh = null;
		meshCollider.sharedMesh = hasTriangles ? renderMesh : null;
	}

	void OnDestroy()
	{
		if (mesh != null)
		{
			if (Application.isPlaying)
			{
				Destroy(mesh);
			}
			else
			{
				DestroyImmediate(mesh);
			}
		}
	}
}
