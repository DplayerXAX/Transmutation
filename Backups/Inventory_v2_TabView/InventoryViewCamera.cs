using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The camera of the inventory view (BodyMounts): a second camera that turns out of the player's eyes to look at
/// the player from the front, with everything behind (and in front of) the player blurred by depth of field.
/// It also draws a thin ring on each mount spot so empty spots can be seen and the one under the cursor glows.
/// Built in code the first time the view opens; no scene setup.
/// </summary>
public sealed class InventoryViewCamera
{
    // A layer no scene volume uses, so only this view's blur reaches the view camera.
    private const int VolumeLayer = 31;

    private readonly Camera eye;
    private readonly Camera view;
    private readonly Volume volume;
    private readonly DepthOfField blur;
    private readonly Material ringMaterial;
    private readonly List<LineRenderer> rings = new List<LineRenderer>();
    private readonly Transform ringRoot;

    private Vector3 fromPosition, goalPosition;
    private Quaternion fromRotation, goalRotation;
    private float t, direction;
    private float focus = 2.5f;

    public Camera Camera => view;
    /// <summary>0 = first person, 1 = fully in the view.</summary>
    public float Blend => t;

    public InventoryViewCamera(Camera playerEye)
    {
        eye = playerEye;
        var go = new GameObject("Inventory View Camera");
        view = go.AddComponent<Camera>();
        view.CopyFrom(playerEye);
        view.depth = playerEye.depth + 1;
        view.nearClipPlane = 0.05f;
        view.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.volumeLayerMask = 1 << VolumeLayer;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        var volumeObject = new GameObject("Inventory View Blur") { layer = VolumeLayer };
        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.DontSave;
        blur = profile.Add<DepthOfField>(true);
        blur.mode.Override(DepthOfFieldMode.Bokeh);
        blur.focalLength.Override(70f);
        blur.aperture.Override(4f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.4f);
        vignette.smoothness.Override(0.6f);
        volume.sharedProfile = profile;

        ringMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
        ringRoot = new GameObject("Inventory View Rings").transform;
    }

    /// <summary>Sets where the view camera goes and what it keeps sharp.</summary>
    public void Frame(Vector3 position, Quaternion rotation, float focusDistance)
    {
        goalPosition = position;
        goalRotation = rotation;
        focus = focusDistance;
    }

    public void Open()
    {
        if (direction > 0f) return;
        direction = 1f;
        if (t <= 0f)
        {
            fromPosition = eye.transform.position;
            fromRotation = eye.transform.rotation;
        }
        view.fieldOfView = eye.fieldOfView;
        view.enabled = true;
    }

    public void Close() => direction = -1f;

    /// <summary>Runs on unscaled time, so a slowed or paused game still opens and closes it. True while the view is showing.</summary>
    public bool Step(float unscaledDeltaTime)
    {
        if (direction == 0f && t <= 0f) return false;
        t = Mathf.Clamp01(t + direction * unscaledDeltaTime / (direction > 0f ? 0.45f : 0.3f));
        float k = t * t * (3f - 2f * t);
        // Coming back it heads for wherever the eyes are now.
        if (direction < 0f) { fromPosition = eye.transform.position; fromRotation = eye.transform.rotation; }
        view.transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, goalPosition, k), Quaternion.Slerp(fromRotation, goalRotation, k));
        view.fieldOfView = Mathf.Lerp(eye.fieldOfView, 50f, k);
        volume.weight = k;
        blur.focusDistance.Override(Mathf.Lerp(30f, focus, k));
        // The eye camera keeps running (other scripts look for Camera.main) but draws nothing under the view.
        if (!eyeMuted && t > 0f) { eyeMask = eye.cullingMask; eye.cullingMask = 0; eyeMuted = true; }
        if (t <= 0f && direction < 0f)
        {
            direction = 0f;
            view.enabled = false;
            RestoreEye();
            ShowRings(0);
            return false;
        }
        return true;
    }

    /// <summary>Draws ring i round a mount spot, facing the view camera. Bright = under the cursor, dim = empty spot.</summary>
    public void Ring(int i, Vector3 centre, float radius, float brightness)
    {
        while (rings.Count <= i) rings.Add(NewRing());
        LineRenderer ring = rings[i];
        ring.enabled = true;
        Transform cam = view.transform;
        Vector3 right = cam.right, up = cam.up;
        int n = ring.positionCount;
        for (int p = 0; p < n; p++)
        {
            float a = p / (float)n * Mathf.PI * 2f;
            ring.SetPosition(p, centre + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
        }
        float alpha = Mathf.Clamp01(brightness) * t;
        Color c = new Color(1f, 0.93f, 0.8f, alpha);
        ring.startColor = ring.endColor = c;
        ring.widthMultiplier = Mathf.Lerp(0.006f, 0.016f, brightness);
    }

    /// <summary>Hides every ring from index count on.</summary>
    public void ShowRings(int count)
    {
        for (int i = count; i < rings.Count; i++) rings[i].enabled = false;
    }

    private LineRenderer NewRing()
    {
        var go = new GameObject("Ring");
        go.transform.SetParent(ringRoot, false);
        var ring = go.AddComponent<LineRenderer>();
        ring.sharedMaterial = ringMaterial;
        ring.loop = true;
        ring.positionCount = 32;
        ring.useWorldSpace = true;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.numCapVertices = 0;
        return ring;
    }

    public void Destroy()
    {
        if (view != null) Object.Destroy(view.gameObject);
        if (volume != null) { Object.Destroy(volume.sharedProfile); Object.Destroy(volume.gameObject); }
        if (ringRoot != null) Object.Destroy(ringRoot.gameObject);
        if (ringMaterial != null) Object.Destroy(ringMaterial);
        RestoreEye();
    }

    private int eyeMask;
    private bool eyeMuted;
    private void RestoreEye()
    {
        if (eye != null && eyeMuted) eye.cullingMask = eyeMask;
        eyeMuted = false;
    }
}
