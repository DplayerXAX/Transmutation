using System.Collections.Generic;
using UnityEngine;

/// <summary>Only visuals below the exact "transparent" tag are hidden. Physics and AI keep running.</summary>
public sealed class HoneyHiddenCreature : MonoBehaviour
{
    private sealed class Visual
    {
        public Renderer Renderer;
        public bool OriginalForceOff;
        public Material[] Materials;
    }
    private readonly List<Visual> visuals = new List<Visual>();
    private Material revealMaterial;
    private bool showing;

    public static bool HasTaggedVisual(Creature creature)
    {
        foreach (Renderer renderer in creature.GetComponentsInChildren<Renderer>(true))
            if (IsTagged(renderer.transform, creature.transform)) return true;
        return false;
    }

    private static bool IsTagged(Transform child, Transform root)
    {
        for (Transform current = child; current != null; current = current.parent)
        {
            if (current.tag == "transparent") return true;
            if (current == root) break;
        }
        return false;
    }

    public void SetSight(bool visible)
    {
        if (!isActiveAndEnabled) return;
        // Restore before rescanning: tags and procedural child renderers may change at runtime.
        Restore();
        visuals.Clear();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!IsTagged(renderer.transform, transform)) continue;
            visuals.Add(new Visual { Renderer = renderer, OriginalForceOff = renderer.forceRenderingOff,
                Materials = renderer.sharedMaterials });
            if (visible)
            {
                if (revealMaterial == null)
                {
                    revealMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    revealMaterial.SetColor("_BaseColor", new Color(0.55f, 1.1f, 0.8f));
                }
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = revealMaterial;
                renderer.sharedMaterials = materials;
                renderer.forceRenderingOff = false;
            }
            else renderer.forceRenderingOff = true;
        }
        showing = visible;
    }

    private void Restore()
    {
        foreach (Visual visual in visuals)
        {
            if (visual.Renderer == null) continue;
            visual.Renderer.forceRenderingOff = visual.OriginalForceOff;
            if (showing) visual.Renderer.sharedMaterials = visual.Materials;
        }
        showing = false;
    }
    private void OnDisable() { Restore(); visuals.Clear(); }
    private void OnDestroy() { Restore(); if (revealMaterial != null) Destroy(revealMaterial); }
}
