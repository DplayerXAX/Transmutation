using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Capstone.Rendering
{
    // Unity 6 / URP Render Graph. No temporal history or scene-color copy.
    public sealed class SketchContourFeature : ScriptableRendererFeature
    {
        public Material material;
        [Tooltip("Empty enables all scenes. Game cameras are filtered by their owning scene.")]
        public string scenePath = "Assets/_Recovery/0.unity";
        public bool showInSceneView = true;
        public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;
        ContourPass pass;

        public override void Create() => pass = new ContourPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            bool sceneView = camera.cameraType == CameraType.SceneView;
            if (material == null || material.passCount < 2 ||
                (sceneView && !showInSceneView) ||
                (!sceneView && camera.cameraType != CameraType.Game) ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;
            string path = sceneView ? SceneManager.GetActiveScene().path : camera.gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath) && path != scenePath)
                return;
            pass.material = material;
            pass.renderPassEvent = injectionPoint;
            renderer.EnqueuePass(pass);
        }

        sealed class ContourPass : ScriptableRenderPass
        {
            internal Material material;
            sealed class PassData
            {
                internal Material material;
                internal TextureHandle source;
            }

            internal ContourPass()
            {
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                    return;

                var desc = graph.GetTextureDesc(resources.activeColorTexture);
                desc.name = "Sketch Contours - Edge Mask";
                desc.colorFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
                desc.depthBufferBits = DepthBits.None;
                desc.msaaSamples = MSAASamples.None;
                desc.bindTextureMS = false;
                desc.clearBuffer = false;
                desc.filterMode = FilterMode.Bilinear;
                var edges = graph.CreateTexture(desc);

                using (var builder = graph.AddRasterRenderPass<PassData>("Sketch Contours / 1. Detect Edges", out var data))
                {
                    data.material = material;
                    data.source = resources.cameraDepthTexture;
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(edges, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
                }

                using (var builder = graph.AddRasterRenderPass<PassData>("Sketch Contours / 2. Three Distorted Strokes", out var data))
                {
                    data.material = material;
                    data.source = edges;
                    builder.UseTexture(edges, AccessFlags.Read);
                    // Load and blend over scene color; never sample/write the same texture.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 1));
                }
            }
        }
    }
}
