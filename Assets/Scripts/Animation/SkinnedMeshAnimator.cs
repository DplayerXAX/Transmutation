using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Drives blend-shape weights on one or more SkinnedMeshRenderers.
/// Currently supports a simple 0-to-1 ping-pong loop per registered key.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class SkinnedMeshAnimator : MonoBehaviour
{
    [Serializable]
    public sealed class AnimationKey
    {
        [Tooltip("Unique name used to play or stop this animation from code.")]
        public string keyName = "Default";

        [Tooltip("Optional direct reference. When empty, the component searches this object and its children.")]
        public SkinnedMeshRenderer skinnedMeshRenderer;

        [Tooltip("GameObject name of the SkinnedMeshRenderer to search for under this component.")]
        public string rendererObjectName = "Visual";

        [Tooltip("Blend-shape name on the target mesh.")]
        public string blendShapeName = "Key 1";

        [Tooltip("Optional direct blend-shape index. Leave at -1 to resolve by name.")]
        public int blendShapeIndex = -1;

        [Tooltip("Seconds for one full ping-pong cycle (0 -> 1 -> 0).")]
        [Min(0.001f)]
        public float duration = 1f;

        [Tooltip("Normalized offset added to the ping-pong phase.")]
        [Range(0f, 1f)]
        public float phaseOffset;

        [Tooltip("Lowest normalized value in the ping-pong loop.")]
        [Range(0f, 1f)]
        public float minValue;

        [Tooltip("Highest normalized value in the ping-pong loop.")]
        [Range(0f, 1f)]
        public float maxValue = 1f;

        [Tooltip("Starts playing automatically when the scene begins.")]
        public bool playOnAwake = true;
    }

    private sealed class ResolvedKey
    {
        public AnimationKey Settings;
        public SkinnedMeshRenderer Renderer;
        public int BlendShapeIndex = -1;
        public bool IsPlaying;
        public float CurrentValue;
        public bool IsValid => Renderer != null && BlendShapeIndex >= 0;
    }

    [Header("Animation Keys")]
    [SerializeField] private List<AnimationKey> animationKeys = new List<AnimationKey>();

    [Header("Playback")]
    [Tooltip("Global speed multiplier applied to every key.")]
    [Min(0f)]
    [SerializeField] private float globalSpeed = 1f;

    [Tooltip("When disabled, no keys update until Play is called.")]
    [SerializeField] private bool playOnAwake = true;

    [Header("Debug")]
    [SerializeField] private bool logSetupWarnings = true;

    private readonly List<ResolvedKey> resolvedKeys = new List<ResolvedKey>();
    private readonly Dictionary<string, ResolvedKey> keysByName = new Dictionary<string, ResolvedKey>(StringComparer.Ordinal);
    private bool hasTriedAutoPlay;

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        // Prefab instances can finish wiring references after Awake.
        if (!HasAnyValidKey())
        {
            Initialize();
        }

        TryAutoPlay();
    }

    private void LateUpdate()
    {
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            ResolvedKey key = resolvedKeys[i];

            if (!key.IsPlaying || !key.IsValid)
            {
                continue;
            }

            float duration = Mathf.Max(0.001f, key.Settings.duration);
            float phase = (Time.time * globalSpeed) / duration + key.Settings.phaseOffset;
            float pingPong = Mathf.PingPong(phase, 1f);
            float minValue = Mathf.Min(key.Settings.minValue, key.Settings.maxValue);
            float maxValue = Mathf.Max(key.Settings.minValue, key.Settings.maxValue);

            key.CurrentValue = Mathf.Lerp(minValue, maxValue, pingPong);
            ApplyBlendShapeWeight(key);
        }
    }

    private void Initialize()
    {
        RebuildResolvedKeys();
        TryAutoPlay();
    }

    private void TryAutoPlay()
    {
        if (!playOnAwake || hasTriedAutoPlay)
        {
            return;
        }

        hasTriedAutoPlay = true;

        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            if (resolvedKeys[i].Settings.playOnAwake)
            {
                PlayResolved(resolvedKeys[i]);
            }
        }
    }

    /// <summary>Rebuilds renderer and blend-shape lookups from the serialized key list.</summary>
    public void RebuildResolvedKeys()
    {
        resolvedKeys.Clear();
        keysByName.Clear();
        hasTriedAutoPlay = false;

        for (int i = 0; i < animationKeys.Count; i++)
        {
            AnimationKey settings = animationKeys[i];

            if (settings == null)
            {
                continue;
            }

            ResolvedKey resolved = new ResolvedKey
            {
                Settings = settings,
                Renderer = ResolveRenderer(settings),
                IsPlaying = false
            };

            if (resolved.Renderer == null)
            {
                if (logSetupWarnings)
                {
                    Debug.LogWarning(
                        $"[SkinnedMeshAnimator] Could not find SkinnedMeshRenderer for key '{settings.keyName}'.",
                        this
                    );
                }
            }
            else if (resolved.Renderer.sharedMesh == null)
            {
                if (logSetupWarnings)
                {
                    Debug.LogWarning(
                        $"[SkinnedMeshAnimator] '{resolved.Renderer.name}' has no mesh assigned for key '{settings.keyName}'.",
                        resolved.Renderer
                    );
                }
            }
            else
            {
                resolved.BlendShapeIndex = ResolveBlendShapeIndex(
                    resolved.Renderer,
                    settings.blendShapeName,
                    settings.blendShapeIndex
                );

                if (resolved.BlendShapeIndex < 0 && logSetupWarnings)
                {
                    Debug.LogWarning(
                        $"[SkinnedMeshAnimator] Blend shape '{settings.blendShapeName}' was not found on '{resolved.Renderer.name}'. Available shapes: {BuildBlendShapeList(resolved.Renderer.sharedMesh)}",
                        resolved.Renderer
                    );
                }
            }

            resolvedKeys.Add(resolved);
            RegisterKeyName(settings.keyName, resolved);
        }
    }

    /// <summary>Starts the ping-pong loop for every valid key.</summary>
    public void PlayAll()
    {
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            PlayResolved(resolvedKeys[i]);
        }
    }

    /// <summary>Starts the ping-pong loop for one key.</summary>
    public void Play(string keyName)
    {
        if (!TryGetResolvedKey(keyName, out ResolvedKey resolved))
        {
            Debug.LogWarning($"[SkinnedMeshAnimator] Unknown animation key '{keyName}'.", this);
            return;
        }

        PlayResolved(resolved);
    }

    /// <summary>Stops one key and leaves its blend shape at the current value.</summary>
    public void Stop(string keyName)
    {
        if (!TryGetResolvedKey(keyName, out ResolvedKey resolved))
        {
            Debug.LogWarning($"[SkinnedMeshAnimator] Unknown animation key '{keyName}'.", this);
            return;
        }

        resolved.IsPlaying = false;
    }

    /// <summary>Stops every key.</summary>
    public void StopAll()
    {
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            resolvedKeys[i].IsPlaying = false;
        }
    }

    /// <summary>Returns the current normalized value for a key, or -1 when the key is missing.</summary>
    public float GetCurrentValue(string keyName)
    {
        return TryGetResolvedKey(keyName, out ResolvedKey resolved) ? resolved.CurrentValue : -1f;
    }

    /// <summary>Sets a key's blend shape immediately without starting playback.</summary>
    public void SetValue(string keyName, float normalizedValue)
    {
        if (!TryGetResolvedKey(keyName, out ResolvedKey resolved))
        {
            Debug.LogWarning($"[SkinnedMeshAnimator] Unknown animation key '{keyName}'.", this);
            return;
        }

        resolved.CurrentValue = Mathf.Clamp01(normalizedValue);
        resolved.IsPlaying = false;
        ApplyBlendShapeWeight(resolved);
    }

    private void PlayResolved(ResolvedKey resolved)
    {
        if (!resolved.IsValid)
        {
            if (logSetupWarnings)
            {
                Debug.LogWarning(
                    $"[SkinnedMeshAnimator] Cannot play '{resolved.Settings.keyName}' because the renderer or blend shape is invalid.",
                    this
                );
            }

            return;
        }

        resolved.IsPlaying = true;
        ApplyBlendShapeWeight(resolved);
    }

    private void ApplyBlendShapeWeight(ResolvedKey resolved)
    {
        if (!resolved.IsValid)
        {
            return;
        }

        resolved.Renderer.SetBlendShapeWeight(resolved.BlendShapeIndex, resolved.CurrentValue * 100f);
    }

    private SkinnedMeshRenderer ResolveRenderer(AnimationKey settings)
    {
        if (settings.skinnedMeshRenderer != null)
        {
            return settings.skinnedMeshRenderer;
        }

        SkinnedMeshRenderer localRenderer = GetComponent<SkinnedMeshRenderer>();
        if (localRenderer != null)
        {
            return localRenderer;
        }

        if (string.IsNullOrWhiteSpace(settings.rendererObjectName))
        {
            return GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].gameObject.name == settings.rendererObjectName)
            {
                return renderers[i];
            }
        }

        return null;
    }

    private static int ResolveBlendShapeIndex(SkinnedMeshRenderer renderer, string blendShapeName, int explicitIndex)
    {
        if (renderer == null || renderer.sharedMesh == null)
        {
            return -1;
        }

        Mesh mesh = renderer.sharedMesh;

        if (explicitIndex >= 0 && explicitIndex < mesh.blendShapeCount)
        {
            return explicitIndex;
        }

        if (string.IsNullOrWhiteSpace(blendShapeName))
        {
            return -1;
        }

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (mesh.GetBlendShapeName(i) == blendShapeName)
            {
                return i;
            }
        }

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (string.Equals(mesh.GetBlendShapeName(i), blendShapeName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);

            if (shapeName.EndsWith(blendShapeName, StringComparison.Ordinal) ||
                shapeName.EndsWith("." + blendShapeName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (mesh.GetBlendShapeName(i).IndexOf(blendShapeName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string BuildBlendShapeList(Mesh mesh)
    {
        if (mesh == null || mesh.blendShapeCount == 0)
        {
            return "(none)";
        }

        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append('\'');
            builder.Append(mesh.GetBlendShapeName(i));
            builder.Append('\'');
        }

        return builder.ToString();
    }

    private bool HasAnyValidKey()
    {
        for (int i = 0; i < resolvedKeys.Count; i++)
        {
            if (resolvedKeys[i].IsValid)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterKeyName(string keyName, ResolvedKey resolved)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            if (logSetupWarnings)
            {
                Debug.LogWarning("[SkinnedMeshAnimator] Animation key has an empty key name.", this);
            }

            return;
        }

        if (keysByName.ContainsKey(keyName))
        {
            if (logSetupWarnings)
            {
                Debug.LogWarning($"[SkinnedMeshAnimator] Duplicate animation key name '{keyName}'.", this);
            }

            return;
        }

        keysByName.Add(keyName, resolved);
    }

    private bool TryGetResolvedKey(string keyName, out ResolvedKey resolved)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            resolved = null;
            return false;
        }

        return keysByName.TryGetValue(keyName, out resolved);
    }

    private void OnValidate()
    {
        globalSpeed = Mathf.Max(0f, globalSpeed);

        for (int i = 0; i < animationKeys.Count; i++)
        {
            AnimationKey key = animationKeys[i];

            if (key == null)
            {
                continue;
            }

            key.duration = Mathf.Max(0.001f, key.duration);
            key.minValue = Mathf.Clamp01(key.minValue);
            key.maxValue = Mathf.Clamp01(key.maxValue);
        }
    }
}
