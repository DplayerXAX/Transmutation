using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(SilkForestManager))]
public sealed class SilkForestManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("黏菌蛛丝树林 · 1 Unity unit = 1 metre\n调整参数后点击生成。同一种子得到同一片树林；密度受最小间距与树数上限约束。", MessageType.Info);
        DrawDefaultInspector();
        var manager = (SilkForestManager)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"当前：{manager.GeneratedTreeCount} 棵树 / {manager.GeneratedCrownCount} 球冠 / {manager.GeneratedVineCount} 组蛛丝连接", EditorStyles.boldLabel);
        if (EditorUtility.IsPersistent(manager))
        {
            EditorGUILayout.HelpBox("请把 Prefab 放入场景，或进入 Prefab 编辑模式后生成树林。", MessageType.Info);
            return;
        }
        if (GUILayout.Button("生成 / 重新生成树林", GUILayout.Height(32))) Generate(manager, false);
        if (GUILayout.Button("换一个随机种子并生成", GUILayout.Height(26))) Generate(manager, true);
        if (GUILayout.Button("保存为 Prefab（含网格）"))
        {
            string path = EditorUtility.SaveFilePanelInProject("Save silk forest prefab", "SilkForest", "prefab", "Choose a prefab location.");
            if (!string.IsNullOrEmpty(path)) SilkForestSetup.SaveForestPrefab(manager, path);
        }
        if (GUILayout.Button("清除生成的树林"))
        {
            manager.ClearGenerated(); MarkDirty(manager);
        }
    }
    private static void Generate(SilkForestManager manager, bool newSeed)
    {
        if (newSeed) manager.seed = (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff);
        manager.Regenerate(); MarkDirty(manager);
        SceneView.RepaintAll();
    }
    private static void MarkDirty(SilkForestManager manager)
    {
        EditorUtility.SetDirty(manager);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
}
