using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

[CustomEditor(typeof(SilkForestManager))]
public sealed class SilkForestManagerEditor : Editor
{
    private bool pendingPreview;
    private double previewAfter;
    private void OnEnable() { EditorApplication.update += UpdatePreview; }
    private void OnDisable() { EditorApplication.update -= UpdatePreview; }
    private void UpdatePreview()
    {
        if (!pendingPreview || EditorApplication.timeSinceStartup < previewAfter || GUIUtility.hotControl != 0) return;
        pendingPreview = false;
        if (target != null && !EditorUtility.IsPersistent(target) && !EditorApplication.isCompiling)
        {
            Generate((SilkForestManager)target, false);
            Repaint();
        }
    }

    public override void OnInspectorGUI()
    {
        var manager = (SilkForestManager)target;
        EditorGUILayout.HelpBox("两个开孔滑条：越大，孔越大、组织越疏。松开滑条后自动更新预览。\n其他参数调整后点击生成；同一种子可复现。", MessageType.Info);
        float oldTrunk = manager.trunkOpenness, oldCrown = manager.crownOpenness;
        DrawChineseFields();
        if (oldTrunk != manager.trunkOpenness || oldCrown != manager.crownOpenness)
        {
            pendingPreview = true;
            previewAfter = EditorApplication.timeSinceStartup + 0.3;
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"当前：{manager.GeneratedTreeCount} 棵树 / {manager.GeneratedVineCount} 组连接（Y形 {manager.GeneratedYJunctionCount} 组）", EditorStyles.boldLabel);
        if (EditorUtility.IsPersistent(manager))
        {
            EditorGUILayout.HelpBox("请把 Prefab 放入场景，或进入 Prefab 编辑模式后生成树林。", MessageType.Info);
            return;
        }
        if (GUILayout.Button("生成 / 重新生成树林", GUILayout.Height(32))) Generate(manager, false);
        if (GUILayout.Button("换一个随机种子并生成", GUILayout.Height(26))) Generate(manager, true);
        if (GUILayout.Button("保存为 Prefab（含网格）"))
        {
            string path = EditorUtility.SaveFilePanelInProject("保存树林预制体", "SilkForest", "prefab", "选择树林预制体的保存位置。");
            if (!string.IsNullOrEmpty(path)) SilkForestSetup.SaveForestPrefab(manager, path);
        }
        if (GUILayout.Button("清除生成的树林"))
        {
            manager.ClearGenerated(); MarkDirty(manager);
        }
    }
    private static readonly HashSet<string> Ranges = new HashSet<string>
    {
        "heightRange", "crownCountRange", "crownRadiusRange", "trunkCrownCountRange",
        "trunkCrownRadiusRange", "trunkRadiusRange", "vineSagRange", "rootCountRange",
        "rootSpreadRange", "rootForkHeightRange", "sideBranchCountRange", "sideBranchLengthRange"
    };

    private void DrawChineseFields()
    {
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool enter = true;
        while (property.NextVisible(enter))
        {
            enter = false;
            if (property.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property, new GUIContent("脚本"));
                continue;
            }
            var field = typeof(SilkForestManager).GetField(property.name, BindingFlags.Instance | BindingFlags.Public);
            var translated = field?.GetCustomAttribute<InspectorNameAttribute>();
            var tooltip = field?.GetCustomAttribute<TooltipAttribute>();
            var label = new GUIContent(translated?.displayName ?? property.displayName, tooltip?.tooltip ?? property.tooltip);
            bool range = Ranges.Contains(property.name);
            if (!range && property.name != "forestSize")
            {
                EditorGUILayout.PropertyField(property, label, true);
                continue;
            }
            var header = field?.GetCustomAttribute<HeaderAttribute>();
            if (header != null) { EditorGUILayout.Space(); EditorGUILayout.LabelField(header.header, EditorStyles.boldLabel); }
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, label, property);
            Rect value = EditorGUI.PrefixLabel(rect, label);
            float oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 30;
            Rect left = new Rect(value.x, value.y, (value.width - 4) * 0.5f, value.height);
            Rect right = new Rect(left.xMax + 4, value.y, left.width, value.height);
            string a = range ? "最小" : "宽度", b = range ? "最大" : "长度";
            EditorGUI.BeginChangeCheck();
            if (property.propertyType == SerializedPropertyType.Vector2Int)
            {
                Vector2Int v = property.vector2IntValue;
                v.x = EditorGUI.IntField(left, a, v.x); v.y = EditorGUI.IntField(right, b, v.y);
                if (EditorGUI.EndChangeCheck()) property.vector2IntValue = v;
            }
            else
            {
                Vector2 v = property.vector2Value;
                v.x = EditorGUI.FloatField(left, a, v.x); v.y = EditorGUI.FloatField(right, b, v.y);
                if (EditorGUI.EndChangeCheck()) property.vector2Value = v;
            }
            EditorGUIUtility.labelWidth = oldWidth;
            EditorGUI.EndProperty();
        }
        serializedObject.ApplyModifiedProperties();
    }
    private void Generate(SilkForestManager manager, bool newSeed)
    {
        pendingPreview = false;
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
