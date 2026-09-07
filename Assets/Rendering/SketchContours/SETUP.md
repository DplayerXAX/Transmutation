# Sketch contours — Scene Zero

## Already configured

Target: Unity 6000.3.6f1, URP 17.3, Render Graph enabled.

`Assets/Settings/PC_Renderer.asset` and `Mobile_Renderer.asset` contain an enabled **Sketch Contours** renderer feature, using `Assets/Rendering/SketchContours/SketchContours.mat`. Its Scene Path is `Assets/_Recovery/0.unity`. Existing scene objects, materials, lighting, and the PC renderer's SSAO are preserved.

1. Return to Unity and let scripts and assets import. Use **Assets > Refresh** if necessary.
2. Open `Assets/_Recovery/0.unity` and view the Game tab, or enter Play mode.
3. Contours are also enabled in Scene view while Scene Zero is the active scene. Play mode animates each stroke distortion smoothly; edit-mode updates depend on editor repainting.

No component on the camera, Volume override, noise texture, or replacement object material is required. The camera's Post Processing checkbox does not toggle this custom renderer feature.

## Disable / re-enable

Select the renderer asset used by your quality setting (`PC_Renderer` or `Mobile_Renderer`) and uncheck / check **Sketch Contours** in its Renderer Features list. Disable it in both assets if you switch quality settings. This stops its passes and input requests; other features may still request depth/normals.

For a quick visual comparison, set the material's **Opacity** to 0, then restore it to 1. Opacity 0 hides the lines but does not stop the GPU passes.

## Adjust the look

Select `SketchContours.mat` in the Project window, not a mesh's material slot.

| Control | Default | Meaning |
|---|---:|---|
| Line Color / Opacity | Dark olive / 1 | Stroke color and visibility |
| Edge Sample Radius | 1 pixel | Increase for thicker contours |
| Normal Sensitivity | 0.3 | Increase to catch smaller changes in surface direction; 0 ignores normal changes |
| Depth Sensitivity | 1.5 | Increase to detect smaller depth differences |
| Depth Comparison Range | 20 world units | Normalizes depth differences; increasing it reduces depth sensitivity without clipping distant objects |
| Stroke Distortion | 0.003 UV | Separation/waviness of the three independently offset strokes |
| Stroke Frequency | 30 | Frequency of sinusoidal line distortion |
| Noise Height | 0.01 UV | Noise displacement strength; maximum offset per axis is half this value |
| Noise Scale | 128 | Spatial frequency of the generated noise |
| Stroke Animation Speed | 0.2 | Slow continuous drift with different speeds/directions per stroke; 0 freezes animation |

For a gentler look, try Noise Height 0.002. Noise Scale controls the frequency of irregularities; Noise Height controls their displacement strength. Set Stroke Distortion and Noise Height to 0 for the undistorted baseline. Zero Stroke Distortion makes the three samples coincide and removes motion. Stroke Animation Speed 0 freezes the drawing while retaining distortion. The default speed gives roughly a 31-second cycle for the first horizontal phase, with different velocities for the other phases.

## Other scenes / effect ordering

Change **Scene Path** on each renderer feature to another scene's project-relative path, or clear it to enable all scenes. Update it if you move or rename Scene Zero. The filter uses the Game camera's owning scene; for additive scenes or persistent cameras, use the appropriate camera scene or clear the filter. No scene file changes are needed.

The default **Injection Point: Before Rendering Post Processing** allows subsequent grading/bloom/antialiasing to affect the lines. **After Rendering Post Processing** can keep strokes crisper. Avoid After Rendering, and avoid moving earlier than depth/normals generation. Temporal antialiasing can soften deliberately moving contours. Camera-stack overlays are skipped so UI overlay cameras do not draw the effect again.

For a new renderer: **Add Renderer Feature > Sketch Contour Feature**, assign `SketchContours.mat`, and set Scene Path. Keep Render Graph enabled; the feature does not implement legacy Compatibility Mode.

## Implementation and limits

Pass 1 compares opposite diagonal pairs of current-frame camera depth and view-space normal XY, matching the supplied Buffer B logic. It outputs white for non-edges, black for edges, into one full-resolution R8 texture (RGBA8 fallback). Sky pixels are handled explicitly; depth is converted to world units for perspective and orthographic cameras.

Pass 2 samples that mask three times using the original phase pairs `(0,0)`, `(1.047,3.142)`, `(2.094,1.571)`. Multiplying the masks combines all three contours. Sinusoidal offsets drift continuously at independent phase velocities for the three strokes. There is no global jitter or stepped update rate. Static procedural value noise substitutes for the missing noise texture. Noise Height retains the internal property name _NoiseAmount to preserve existing material values. Alpha blending overlays just the ink and preserves scene alpha. There is no raymarching, recoloring, scene-color copy, or previous-frame history.

The effect currently outlines the visible opaque scene, including terrain, rather than selected objects. A selection mask can be added separately. Transparent objects generally do not supply camera depth/normals; they are not reliable contour sources and contours behind them can appear over them. Custom opaque shaders need appropriate URP DepthOnly / DepthNormals passes for reliable results. Texture/color boundaries alone do not create lines.

Two full-screen draws plus any depth/normal work URP needs. A 1080p R8 edge texture is approximately 2.1 MB. Distortion uses UV units as in the source, so displacement in pixels increases with resolution. Edge thresholds also preserve the source's height-based scaling. Profile your target hardware before stacking many effects.

## Verification in Unity

After import, check the Console for shader/import errors. In the Frame Debugger, locate **Sketch Contours / 1. Detect Edges** and **Sketch Contours / 2. Three Distorted Strokes**. Compare with the feature disabled: only contours should change. Check a capsule silhouette, a sharp corner, an object against the sky, and an overlapping object. Set all displacement controls to zero, then restore Stroke Distortion to see the three strokes separate. Open another scene to confirm the scene filter skips the effect.

Source basis: the Buffer B and Image code supplied in the conversation. Original author/license metadata was not provided; preserve the original attribution/license when available before redistribution.
