using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DensityShapeOverlay))]
public class DensityShapeOverlayEditor : Editor
{
	SerializedProperty terrain;
	SerializedProperty shape;
	SerializedProperty radius;
	SerializedProperty boxHalfExtents;
	SerializedProperty height;
	SerializedProperty ellipsoidRadii;
	SerializedProperty injectRate;
	SerializedProperty maxDensity;
	SerializedProperty stampDensity;
	SerializedProperty gizmoColor;

	void OnEnable()
	{
		terrain = serializedObject.FindProperty("terrain");
		shape = serializedObject.FindProperty("shape");
		radius = serializedObject.FindProperty("radius");
		boxHalfExtents = serializedObject.FindProperty("boxHalfExtents");
		height = serializedObject.FindProperty("height");
		ellipsoidRadii = serializedObject.FindProperty("ellipsoidRadii");
		injectRate = serializedObject.FindProperty("injectRate");
		maxDensity = serializedObject.FindProperty("maxDensity");
		stampDensity = serializedObject.FindProperty("stampDensity");
		gizmoColor = serializedObject.FindProperty("gizmoColor");
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.PropertyField(terrain);
		EditorGUILayout.PropertyField(shape);

		var shapeValue = (DensityShapeOverlay.OverlayShape)shape.enumValueIndex;
		EditorGUILayout.Space(4);
		EditorGUILayout.LabelField("Shape Size (world units)", EditorStyles.boldLabel);

		switch (shapeValue)
		{
			case DensityShapeOverlay.OverlayShape.Sphere:
				EditorGUILayout.PropertyField(radius);
				break;

			case DensityShapeOverlay.OverlayShape.Box:
				EditorGUILayout.PropertyField(boxHalfExtents);
				EditorGUILayout.HelpBox("Box uses Chebyshev falloff — the mesh is a rectangular box.", MessageType.Info);
				break;

			case DensityShapeOverlay.OverlayShape.Capsule:
				EditorGUILayout.PropertyField(radius);
				EditorGUILayout.PropertyField(height, new GUIContent("Height (total, includes caps)"));
				if (height.floatValue < radius.floatValue * 2f)
				{
					EditorGUILayout.HelpBox("Height will be clamped to at least 2 × Radius at runtime.", MessageType.Warning);
				}
				break;

			case DensityShapeOverlay.OverlayShape.Cylinder:
				EditorGUILayout.PropertyField(radius);
				EditorGUILayout.PropertyField(height);
				EditorGUILayout.HelpBox(
					"Cylinder has flat caps — from the side it looks rectangular (not rounded like Capsule).",
					MessageType.Warning);
				break;

			case DensityShapeOverlay.OverlayShape.Ellipsoid:
				EditorGUILayout.PropertyField(ellipsoidRadii);
				break;
		}

		EditorGUILayout.Space(4);
		EditorGUILayout.LabelField("Injection", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(injectRate);
		EditorGUILayout.PropertyField(maxDensity);
		EditorGUILayout.PropertyField(stampDensity);
		EditorGUILayout.PropertyField(gizmoColor);

		serializedObject.ApplyModifiedProperties();
	}
}
