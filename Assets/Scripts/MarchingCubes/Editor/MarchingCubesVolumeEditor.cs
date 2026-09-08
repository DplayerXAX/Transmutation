using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MarchingCubesVolume))]
public class MarchingCubesVolumeEditor : Editor
{
	void OnSceneGUI()
	{
		var mc = (MarchingCubesVolume)target;

		float extent = (mc.NumPointsPerAxis - 1) * mc.Spacing;
		Vector3 localCenter = Vector3.one * (extent * 0.5f);

		Handles.matrix = mc.transform.localToWorldMatrix;
		Handles.Label(
			localCenter + Vector3.up * (extent * 0.5f + mc.Spacing * 0.5f),
			$"MC Volume {mc.NumPointsPerAxis}³  extent={extent:0.##}  chunk={mc.ChunkSizeCubes}");
	}
}
