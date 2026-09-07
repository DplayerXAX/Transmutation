using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MarchingCubesSphere))]
public class MarchingCubesSphereEditor : Editor
{
	void OnSceneGUI()
	{
		var mc = (MarchingCubesSphere)target;

		float extent = (mc.NumPointsPerAxis - 1) * mc.Spacing;
		Vector3 localCenter = Vector3.one * (extent * 0.5f);
		Vector3 localSize = Vector3.one * extent;

		Handles.matrix = mc.transform.localToWorldMatrix;
		Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
		Handles.DrawWireCube(localCenter, localSize);

		Handles.Label(
			localCenter + Vector3.up * (extent * 0.5f + mc.Spacing * 0.5f),
			$"MC Grid {mc.NumPointsPerAxis}³  extent={extent:0.##}");
	}
}
