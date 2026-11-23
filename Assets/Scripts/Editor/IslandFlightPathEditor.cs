using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(IslandFlightPath))]
public sealed class IslandFlightPathEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var route = (IslandFlightPath)target;
        if (!Application.isPlaying) route.Rebuild();
        if (!route.Valid || route.Length < .01f) EditorGUILayout.HelpBox("Assign at least four non-null, separated waypoint transforms. The island remains stationary without a valid route.", MessageType.Warning);
        EditorGUILayout.HelpBox("Move the numbered child waypoints in the Scene view. Select this route to preview the closed loop. Route edits apply at the next Play session.", MessageType.Info);
    }
    void OnSceneGUI()
    {
        var route = (IslandFlightPath)target;
        if (route.waypoints == null) return;
        if (route.Valid && route.Length > 0)
            for (float distance = 0; distance < route.Length; distance += 30)
                Handles.ArrowHandleCap(0, route.Position(distance), route.Heading(distance), 5, EventType.Repaint);
        for (int i=0; i<route.waypoints.Length; i++)
        {
            var point = route.waypoints[i]; if (!point) continue;
            Handles.Label(point.position + Vector3.up*3, $"{i+1}");
            EditorGUI.BeginChangeCheck(); Vector3 position = Handles.PositionHandle(point.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) continue;
            Undo.RecordObject(point, "Move island waypoint"); point.position = position; route.Rebuild();
        }
    }
}
