using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Render_Test
{
    [Serializable]
    [VolumeComponentMenu("Custom/SphereVolumeComponent")]
    public class SphereVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter Intensity = new ClampedFloatParameter(value: 0f, min: 0f, max: 1, overrideState: true);
        public bool IsActive() => Intensity.value > 0f;
    }
}
