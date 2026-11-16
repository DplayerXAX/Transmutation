using System.Collections.Generic;
using UnityEngine;

/// <summary>One circular mesh with a horizontal, circle-clipped liquid surface.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BloodGlobeGraphic : UnityEngine.UI.MaskableGraphic
{
    private const int Segments = 96;
    private float level;
    private Color liquid;
    private readonly List<Vector2> clipped = new List<Vector2>(Segments + 2);
    public void SetCoat(float health, CreaturePolarity polarity)
    {
        level = Mathf.Clamp01(health / 100f);
        liquid = AsuraBloodPalette.For(polarity);
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        Disc(mesh, center, radius, new Color(0.025f, 0.025f, 0.045f, 0.94f));
        Ring(mesh, center, radius - 1f, radius, new Color(0.02f, 0.02f, 0.03f, 0.95f));
        Ring(mesh, center, radius - 2.5f, radius - 1f, new Color(0.8f, 0.82f, 0.9f, 0.8f));
        float inner = radius - 4f;
        float surface = Mathf.Lerp(-inner, inner, level);
        clipped.Clear();
        Vector2 previous = Point(Segments - 1, inner);
        for (int i = 0; i < Segments; i++)
        {
            Vector2 current = Point(i, inner);
            bool before = previous.y <= surface, after = current.y <= surface;
            if (before != after)
                clipped.Add(Vector2.Lerp(previous, current, (surface - previous.y) / (current.y - previous.y)));
            if (after) clipped.Add(current);
            previous = current;
        }
        if (clipped.Count < 3) return;
        int start = mesh.currentVertCount;
        foreach (Vector2 point in clipped) mesh.AddVert(center + point, liquid, Vector2.zero);
        for (int i = 1; i < clipped.Count - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        // A pale, narrow meniscus keeps Dark blood legible against the dim interior.
        if (level < 0.999f)
        {
            float width = Mathf.Sqrt(Mathf.Max(0f, inner * inner - surface * surface));
            int at = mesh.currentVertCount;
            Color edge = Color.Lerp(liquid, Color.white, 0.55f);
            mesh.AddVert(center + new Vector2(-width, surface), edge, Vector2.zero);
            mesh.AddVert(center + new Vector2(width, surface), edge, Vector2.zero);
            mesh.AddVert(center + new Vector2(width, surface - 1f), edge, Vector2.zero);
            mesh.AddVert(center + new Vector2(-width, surface - 1f), edge, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }
    }
    private static Vector2 Point(int i, float radius)
    {
        float angle = i * Mathf.PI * 2f / Segments;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
    private static void Disc(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, Color color)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < Segments; i++) mesh.AddVert(center + Point(i, radius), color, Vector2.zero);
        for (int i = 0; i < Segments; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Segments);
    }
    private static void Ring(UnityEngine.UI.VertexHelper mesh, Vector2 center, float inner, float outer, Color color)
    {
        int start = mesh.currentVertCount;
        for (int i = 0; i < Segments; i++)
        {
            mesh.AddVert(center + Point(i, inner), color, Vector2.zero);
            mesh.AddVert(center + Point(i, outer), color, Vector2.zero);
        }
        for (int i = 0; i < Segments; i++)
        {
            int a = start + i * 2, b = start + (i + 1) % Segments * 2;
            mesh.AddTriangle(a, a + 1, b); mesh.AddTriangle(b, a + 1, b + 1);
        }
    }
}
