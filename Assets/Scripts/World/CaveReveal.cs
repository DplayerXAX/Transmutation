using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reveal lights for the dark caves: each one uncovers the terrain within its radius from the black fog (see
/// Shaders/World/CaveReveal.hlsl, used by TerrainLinesLit), with only a slight shift towards its own colour, so a
/// glowing creature shows the rock round it even from far away. The nearest 16 to the camera are sent to the shaders
/// each frame. Glow creatures and the baton register here (CaveGlowCreature.LightStyle = Reveal).
/// </summary>
public sealed class CaveReveal : MonoBehaviour
{
    public sealed class Light
    {
        public Transform anchor;
        public float radius;
        public Color tint;
        /// <summary>How much it reveals now (0..1), e.g. the creature's current glow.</summary>
        public Func<float> strength;
    }

    private const int Max = 16;
    private static readonly List<Light> lights = new List<Light>();
    private static CaveReveal runner;
    private readonly Vector4[] centres = new Vector4[Max];
    private readonly Vector4[] tints = new Vector4[Max];
    private readonly List<Light> sorted = new List<Light>();
    private static readonly int CentresId = Shader.PropertyToID("_CaveReveal");
    private static readonly int TintsId = Shader.PropertyToID("_CaveRevealTint");
    private static readonly int CountId = Shader.PropertyToID("_CaveRevealCount");
    private static readonly int SoftId = Shader.PropertyToID("_CaveRevealSoft");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lights.Clear();
        runner = null;
        Shader.SetGlobalFloat(CountId, 0f);
    }

    /// <summary>How much of its own colour an uncovered patch takes (0 = none, 1 = fully tinted).</summary>
    public static float TintAmount = 0.55f;
    /// <summary>On: the revealed ground fades gradually into the dark. Off: the older clean stencil edge.</summary>
    public static bool SoftEdge = true;
    /// <summary>A soft light reaches this much further, since its outer part is faint.</summary>
    public static float SoftReach = 2f;

    public static Light Add(Transform anchor, float radius, Color color, Func<float> strength = null)
    {
        if (runner == null)
        {
            var go = new GameObject("Cave Reveal Lights") { hideFlags = HideFlags.DontSave };
            runner = go.AddComponent<CaveReveal>();
        }
        var light = new Light { anchor = anchor, radius = radius, tint = Color.Lerp(Color.white, Brighten(color), TintAmount), strength = strength };
        lights.Add(light);
        return light;
    }

    public static void Remove(Light light) => lights.Remove(light);

    // The tint keeps the hue but not the darkness of the colour, so it shifts colour without darkening the rock.
    private static Color Brighten(Color c)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1e-3f);
        return new Color(c.r / m, c.g / m, c.b / m);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
        sorted.Clear();
        for (int i = lights.Count - 1; i >= 0; i--)
        {
            if (lights[i].anchor == null) { lights.RemoveAt(i); continue; }
            if (!lights[i].anchor.gameObject.activeInHierarchy) continue;
            sorted.Add(lights[i]);
        }
        sorted.Sort((a, b) => (a.anchor.position - eye).sqrMagnitude.CompareTo((b.anchor.position - eye).sqrMagnitude));
        int n = Mathf.Min(Max, sorted.Count);
        for (int i = 0; i < Max; i++)
        {
            if (i < n)
            {
                Light l = sorted[i];
                Vector3 p = l.anchor.position;
                centres[i] = new Vector4(p.x, p.y, p.z, l.radius * (SoftEdge ? SoftReach : 1f));
                float s = l.strength != null ? Mathf.Clamp01(l.strength()) : 1f;
                // The last few of the nearest lights fade out, so none pops when the order changes.
                s *= Mathf.Clamp01((Max - i) / 4f);
                tints[i] = new Vector4(l.tint.r, l.tint.g, l.tint.b, s);
            }
            else { centres[i] = Vector4.zero; tints[i] = Vector4.zero; }
        }
        Shader.SetGlobalFloat(SoftId, SoftEdge ? 1f : 0f);
        Shader.SetGlobalVectorArray(CentresId, centres);
        Shader.SetGlobalVectorArray(TintsId, tints);
        // Above ground nothing is hidden, so the terrain shader need not loop over the lights.
        Shader.SetGlobalFloat(CountId, DarkTunnels.Darkness > 0.001f ? n : 0);
    }

    private void OnDestroy()
    {
        if (runner == this) { runner = null; Shader.SetGlobalFloat(CountId, 0f); }
    }
}
