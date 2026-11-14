using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supplies the strokes of a glyph by name. Put a component that implements this on the
/// DropletChoir's Glyph Source field to teach the droplets a new script (for example the
/// planet's own glyph language). Strokes are 2D polylines in any units, y up, in writing order;
/// a one-point stroke is a dot. Return false to fall back to the built-in test font.
/// </summary>
public interface IDropletGlyphSource
{
    bool TryGetStrokes(string key, List<Vector2[]> strokes);
}

/// <summary>
/// Built-in test shapes: a small stroke font (A-Z, 0-9, . ! ? - : and space, one line per '\n')
/// and a few named figures ("@circle", "@spiral", "@wave", "@star", "@eye"). Each letter sits in a
/// 4 x 6 cell. These are placeholders until the planet's glyph language plugs in through
/// IDropletGlyphSource.
/// </summary>
public static class DropletGlyphs
{
    private const float Advance = 6f;   // letter width 4 + gap 2
    private const float LineHeight = 9f;

    // Each stroke is a run of two-digit points "xy" on the 4 x 6 grid; strokes are separated by spaces.
    private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
    {
        { 'A', "002640 1333" },
        { 'B', "0006 063645443303 3342413000" },
        { 'C', "4536160501103041" },
        { 'D', "0006 062644422000" },
        { 'E', "40000646 0333" },
        { 'F', "000646 0333" },
        { 'G', "45361605011030414323" },
        { 'H', "0006 4046 0343" },
        { 'I', "1636 2620 1030" },
        { 'J', "1646 3631201001" },
        { 'K', "0006 4602 1340" },
        { 'L', "060040" },
        { 'M', "0006234640" },
        { 'N', "00064046" },
        { 'O', "163645413010010516" },
        { 'P', "00063645443303" },
        { 'Q', "163645413010010516 2240" },
        { 'R', "00063645443303 2340" },
        { 'S', "453616050413334241301001" },
        { 'T', "0646 2620" },
        { 'U', "060110304146" },
        { 'V', "062046" },
        { 'W', "0610233046" },
        { 'X', "0046 0640" },
        { 'Y', "0623 4623 2320" },
        { 'Z', "06464000" },
        { '0', "163645413010010516 0145" },
        { '1', "1526 2620 1030" },
        { '2', "05163645440040" },
        { '3', "05163645443313 334241301001" },
        { '4', "30360242" },
        { '5', "460603334241301001" },
        { '6', "453616050110304142331302" },
        { '7', "064620" },
        { '8', "130405163645443313 1302011030414233" },
        { '9', "443313040516364541301001" },
        { '.', "20" },
        { '!', "2622 20" },
        { '?', "0516364544332322 20" },
        { '-', "1333" },
        { ':', "22 24" },
    };

    /// <summary>Strokes for a key: "@name" figures, otherwise text in the test font.</summary>
    public static bool TryGetStrokes(string key, List<Vector2[]> strokes)
    {
        strokes.Clear();
        if (string.IsNullOrEmpty(key)) return false;
        if (key[0] == '@') return TryFigure(key.Substring(1).ToLowerInvariant(), strokes);
        AppendText(key, strokes);
        return strokes.Count > 0;
    }

    public static void AppendText(string text, List<Vector2[]> strokes)
    {
        string[] lines = text.ToUpperInvariant().Split('\n');
        for (int line = 0; line < lines.Length; line++)
        {
            // Centre each line on x = 0 so multi-line text reads as one block.
            float width = Mathf.Max(0f, lines[line].Length * Advance - 2f);
            float x0 = -width * 0.5f, y0 = -line * LineHeight;
            for (int i = 0; i < lines[line].Length; i++)
            {
                if (!Font.TryGetValue(lines[line][i], out string code)) continue;
                foreach (string stroke in code.Split(' '))
                {
                    var points = new Vector2[stroke.Length / 2];
                    for (int p = 0; p < points.Length; p++)
                        points[p] = new Vector2(x0 + i * Advance + (stroke[p * 2] - '0'), y0 + (stroke[p * 2 + 1] - '0'));
                    strokes.Add(points);
                }
            }
        }
    }

    private static bool TryFigure(string name, List<Vector2[]> strokes)
    {
        switch (name)
        {
            case "circle":
                strokes.Add(Arc(Vector2.zero, 3f, 90f, 450f, 48));
                return true;
            case "spiral":
            {
                var points = new Vector2[90];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = i / (points.Length - 1f), a = t * Mathf.PI * 5f;
                    points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.3f + t * 3.2f);
                }
                strokes.Add(points);
                return true;
            }
            case "wave":
            {
                var points = new Vector2[60];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = i / (points.Length - 1f);
                    points[i] = new Vector2(-7f + t * 14f, Mathf.Sin(t * Mathf.PI * 3f) * 1.6f);
                }
                strokes.Add(points);
                return true;
            }
            case "star":
            {
                var points = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Deg2Rad * (90f + i * 144f);
                    points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.2f;
                }
                strokes.Add(points);
                return true;
            }
            case "eye":
                strokes.Add(Arc(new Vector2(0f, -2.4f), 4.6f, 30f, 150f, 24));
                strokes.Add(Arc(new Vector2(0f, 2.4f), 4.6f, 210f, 330f, 24));
                strokes.Add(Arc(Vector2.zero, 1f, 90f, 450f, 14));
                strokes.Add(new[] { Vector2.zero });
                return true;
        }
        return false;
    }

    private static Vector2[] Arc(Vector2 centre, float radius, float fromDegrees, float toDegrees, int count)
    {
        var points = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, i / (count - 1f));
            points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return points;
    }

    /// <summary>
    /// Places points evenly along the strokes, about <paramref name="spacing"/> apart (in stroke
    /// units), using at most <paramref name="maxPoints"/>. Each point gets its writing order 0..1:
    /// how far along the whole glyph's pen path it lies. Every stroke keeps its end points.
    /// </summary>
    public static void Sample(List<Vector2[]> strokes, float spacing, int maxPoints, List<Vector2> points, List<float> order)
    {
        points.Clear();
        order.Clear();
        if (strokes.Count == 0 || maxPoints <= 0) return;
        float total = 0f;
        var lengths = new float[strokes.Count];
        for (int s = 0; s < strokes.Count; s++)
        {
            lengths[s] = Length(strokes[s]);
            total += Mathf.Max(lengths[s], spacing); // a dot still takes one pen step
        }
        // Too few droplets for the spacing: spread the ones there are.
        spacing = Mathf.Max(spacing, total / Mathf.Max(1, maxPoints - strokes.Count));
        float penBefore = 0f;
        for (int s = 0; s < strokes.Count && points.Count < maxPoints; s++)
        {
            Vector2[] stroke = strokes[s];
            float length = lengths[s];
            int count = stroke.Length < 2 || length < 1e-4f ? 1 : Mathf.Max(2, Mathf.RoundToInt(length / spacing) + 1);
            for (int i = 0; i < count && points.Count < maxPoints; i++)
            {
                float along = count == 1 ? 0f : length * i / (count - 1);
                points.Add(PointAlong(stroke, along));
                order.Add((penBefore + along) / total);
            }
            penBefore += Mathf.Max(length, spacing);
        }
    }

    private static float Length(Vector2[] stroke)
    {
        float length = 0f;
        for (int i = 1; i < stroke.Length; i++) length += Vector2.Distance(stroke[i - 1], stroke[i]);
        return length;
    }

    private static Vector2 PointAlong(Vector2[] stroke, float distance)
    {
        for (int i = 1; i < stroke.Length; i++)
        {
            float segment = Vector2.Distance(stroke[i - 1], stroke[i]);
            if (distance <= segment || i == stroke.Length - 1)
                return Vector2.Lerp(stroke[i - 1], stroke[i], segment < 1e-5f ? 1f : Mathf.Clamp01(distance / segment));
            distance -= segment;
        }
        return stroke[0];
    }
}
