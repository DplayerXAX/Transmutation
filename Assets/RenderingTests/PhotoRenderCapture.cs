using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RenderTest
{
    public class PhotoRenderCapture : MonoBehaviour
    {
        const string URPCopyDepthShaderName = "Hidden/Universal Render Pipeline/CopyDepth";

        public enum CaptureDepthUvYFlip
        {
            Auto,
            Never,
            Always
        }

        public Camera captureCam;
        public RenderTexture captureRT;

        /// <summary>Auto: flip Y on APIs where RT origin is top (common on Metal/D3D). Try Never if mask is inverted vertically.</summary>
        public CaptureDepthUvYFlip depthTextureUvYFlip = CaptureDepthUvYFlip.Auto;

        /// <summary>Turn off HDR + post on the capture camera so URP is more likely to render directly into captureRT (depth buffer actually written).</summary>
        public bool forceSimpleOffscreenPath = true;

        /// <summary>Optional: assign the PhotoReveal material so _CaptureDepthTex / _MainTex are set (material overrides globals).</summary>
        public Material photoRevealMaterial;

        /// <summary>Holds a copy of scene depth after capture (R32 raw device Z).</summary>
        RenderTexture _captureDepthCopyRT;

        Material _urpCopyDepthMaterial;

        /// <summary>While true, <see cref="OnEndCameraRendering"/> will try to grab depth for <see cref="captureCam"/>.</summary>
        bool _wantsDepthGrab;

        /// <summary>True if the current TakePhoto() already got a depth copy (from callback or fallback).</summary>
        bool _gotDepthCopy;

        static Vector4 ComputeZBufferParams(float near, float far)
        {
            float invNear = Mathf.Approximately(near, 0f) ? 0f : 1f / near;
            float invFar = Mathf.Approximately(far, 0f) ? 0f : 1f / far;
            float zc0 = 1f - far * invNear;
            float zc1 = far * invNear;
            var zBufferParams = new Vector4(zc0, zc1, zc0 * invFar, zc1 * invFar);

            if (SystemInfo.usesReversedZBuffer)
            {
                zBufferParams.y += zBufferParams.x;
                zBufferParams.x = -zBufferParams.x;
                zBufferParams.w += zBufferParams.z;
                zBufferParams.z = -zBufferParams.z;
            }

            return zBufferParams;
        }

        void EnsureDepthCopyRT(int width, int height)
        {
            if (_captureDepthCopyRT != null && _captureDepthCopyRT.width == width && _captureDepthCopyRT.height == height)
                return;

            if (_captureDepthCopyRT != null)
            {
                _captureDepthCopyRT.Release();
                Destroy(_captureDepthCopyRT);
            }

            var desc = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = GraphicsFormat.R32_SFloat,
                depthStencilFormat = GraphicsFormat.None,
                msaaSamples = 1,
                mipCount = 1,
                dimension = TextureDimension.Tex2D,
                sRGB = false
            };

            _captureDepthCopyRT = new RenderTexture(desc)
            {
                name = "PhotoCaptureDepthCopy",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _captureDepthCopyRT.Create();
        }

        void Awake()
        {
            if (captureCam == null)
                return;

            if (captureCam.cameraType != CameraType.Game && captureCam.cameraType != CameraType.VR)
            {
                Debug.LogWarning(
                    $"PhotoRenderCapture: capture camera type was {captureCam.cameraType}. Setting to Game so URP applies per-camera Depth Texture.");
                captureCam.cameraType = CameraType.Game;
            }

            var urp = captureCam.GetUniversalAdditionalCameraData();
            urp.requiresDepthTexture = true;
            if (forceSimpleOffscreenPath)
            {
                urp.renderPostProcessing = false;
                captureCam.allowHDR = false;
            }
        }

        void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        void Start()
        {
            if (captureCam != null && captureRT != null)
            {
                captureCam.targetTexture = captureRT;
                if (captureRT.depthStencilFormat == GraphicsFormat.None)
                    Debug.LogWarning(
                        "PhotoRenderCapture: captureRT has no depth buffer. Assign Depth Stencil on the RenderTexture.");
            }

            Shader.SetGlobalFloat("_HasCapture", 0);
        }

        void OnDestroy()
        {
            if (_captureDepthCopyRT != null)
            {
                _captureDepthCopyRT.Release();
                Destroy(_captureDepthCopyRT);
            }

            if (_urpCopyDepthMaterial != null)
                Destroy(_urpCopyDepthMaterial);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                TakePhoto();
        }

        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!_wantsDepthGrab || camera != captureCam || _captureDepthCopyRT == null)
                return;

            if (TryCopyDepthAllMethods(context))
                _gotDepthCopy = true;
        }

        /// <summary>URP dummy depth is often Texture2D.black/white; also reject tiny placeholder textures.</summary>
        static bool IsUselessDepthGlobal(Texture t)
        {
            if (t == null)
                return true;
            if (t is Texture2D t2d && (t2d == Texture2D.blackTexture || t2d == Texture2D.whiteTexture))
                return true;
            if (t.width <= 4 && t.height <= 4)
                return true;
            return false;
        }

        /// <summary>Prefer URP's copied depth RT (same size as capture). Returns false if not copied.</summary>
        bool TryCopyDepthFromGlobalTexture()
        {
            Texture g = Shader.GetGlobalTexture("_CameraDepthTexture");
            if (IsUselessDepthGlobal(g) || captureRT == null)
                return false;

            if (g.width != captureRT.width || g.height != captureRT.height)
                return false;

            Graphics.Blit(g, _captureDepthCopyRT);
            return true;
        }

        bool TryCopyDepthWithBlitter(CommandBuffer cmd)
        {
            if (captureRT == null || !captureRT.IsCreated() || captureRT.depthStencilFormat == GraphicsFormat.None)
                return false;

            CoreUtils.SetRenderTarget(cmd, _captureDepthCopyRT, ClearFlag.None);
            Blitter.BlitDepth(cmd, captureRT, new Vector4(1f, 1f, 0f, 0f), 0f);
            return true;
        }

        bool TryCopyDepthFromCaptureRTSurfaceMaterial()
        {
            if (captureRT == null || !captureRT.IsCreated() || captureRT.depthStencilFormat == GraphicsFormat.None)
                return false;

            var shader = Shader.Find(URPCopyDepthShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"PhotoRenderCapture: could not find shader '{URPCopyDepthShaderName}'.");
                return false;
            }

            if (_urpCopyDepthMaterial == null)
                _urpCopyDepthMaterial = new Material(shader) { name = "PhotoCaptureURPCopyDepth (Instance)" };

            Material m = _urpCopyDepthMaterial;
            m.DisableKeyword("_DEPTH_MSAA_2");
            m.DisableKeyword("_DEPTH_MSAA_4");
            m.DisableKeyword("_DEPTH_MSAA_8");
            m.DisableKeyword("_OUTPUT_DEPTH");

            int aa = Mathf.Max(1, captureRT.antiAliasing);
            if (aa == 2)
                m.EnableKeyword("_DEPTH_MSAA_2");
            else if (aa == 4)
                m.EnableKeyword("_DEPTH_MSAA_4");
            else if (aa == 8)
                m.EnableKeyword("_DEPTH_MSAA_8");

            int depthAttachId = Shader.PropertyToID("_CameraDepthAttachment");
            m.SetTexture(depthAttachId, captureRT, RenderTextureSubElement.Depth);

            Graphics.Blit(null, _captureDepthCopyRT, m, 0);
            return true;
        }

        /// <summary>Order: global same-size RT → Blitter from captureRT.depth → URP CopyDepth material.</summary>
        bool TryCopyDepthAllMethods(ScriptableRenderContext? context)
        {
            if (TryCopyDepthFromGlobalTexture())
                return true;

            if (captureRT != null && captureRT.depthStencilFormat != GraphicsFormat.None)
            {
                CommandBuffer cmd = CommandBufferPool.Get("PhotoCaptureBlitDepthSRP");
                try
                {
                    if (TryCopyDepthWithBlitter(cmd))
                    {
                        if (context.HasValue)
                            context.Value.ExecuteCommandBuffer(cmd);
                        else
                            Graphics.ExecuteCommandBuffer(cmd);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"PhotoRenderCapture: Blitter.BlitDepth failed ({e.Message}). Falling back to material copy.");
                }
                finally
                {
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                }
            }

            return TryCopyDepthFromCaptureRTSurfaceMaterial();
        }

        void PushCaptureShaderGlobals(Matrix4x4 gpuProj, Matrix4x4 view, Vector4 zbp, Vector3 worldPos)
        {
            Shader.SetGlobalMatrix("_CaptureViewMatrix", view);
            Shader.SetGlobalMatrix("_CaptureProjMatrix", gpuProj);
            Shader.SetGlobalVector("_CaptureZBufferParams", zbp);
            Shader.SetGlobalVector("_CaptureWorldPos", new Vector4(worldPos.x, worldPos.y, worldPos.z, 0f));
            float flipY = depthTextureUvYFlip == CaptureDepthUvYFlip.Auto
                ? (SystemInfo.graphicsUVStartsAtTop ? 1f : 0f)
                : depthTextureUvYFlip == CaptureDepthUvYFlip.Always ? 1f : 0f;
            Shader.SetGlobalFloat("_CaptureFlipUVY", flipY);

            Shader.SetGlobalTexture("_CaptureDepthTex", _captureDepthCopyRT);
            if (photoRevealMaterial != null)
            {
                photoRevealMaterial.SetTexture("_CaptureDepthTex", _captureDepthCopyRT);
                photoRevealMaterial.SetTexture("_MainTex", captureRT);
            }
        }

        void TakePhoto()
        {
            if (captureCam == null || captureRT == null)
                return;

            captureCam.targetTexture = captureRT;

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("PhotoRenderCapture: Camera.main is null.");
                return;
            }

            captureCam.transform.position = mainCam.transform.position;
            captureCam.transform.rotation = mainCam.transform.rotation;

            EnsureDepthCopyRT(captureRT.width, captureRT.height);

            _gotDepthCopy = false;
            _wantsDepthGrab = true;
            captureCam.Render();
            _wantsDepthGrab = false;

            if (!_gotDepthCopy)
                _gotDepthCopy = TryCopyDepthAllMethods(null);

            if (!_gotDepthCopy)
            {
                Debug.LogWarning(
                    "PhotoRenderCapture: depth copy failed. Use a capture RenderTexture with Depth (D24/D32), MSAA=1 if possible, " +
                    "and keep Force Simple Offscreen Path on (disables HDR/post so depth is written to captureRT).");
            }

            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(captureCam.projectionMatrix, true);
            Matrix4x4 view = captureCam.worldToCameraMatrix;
            Vector4 zbp = ComputeZBufferParams(captureCam.nearClipPlane, captureCam.farClipPlane);

            PushCaptureShaderGlobals(gpuProj, view, zbp, captureCam.transform.position);

            Shader.SetGlobalTexture("_CaptureTex", captureRT);
            Shader.SetGlobalFloat("_HasCapture", 1f);

            Debug.Log(_gotDepthCopy
                ? "Photo Taken: depth copied to _CaptureDepthTex."
                : "Photo Taken: color saved, depth copy failed (see warning).");
        }
    }
}
