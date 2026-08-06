using UnityEngine;

namespace Hlight.Audio
{
    /// <summary>Linear 0-1 (what a slider shows) to dB (what a mixer parameter wants).</summary>
    public static class AudioVolume
    {
        public const float SilenceDb = -80f;

        public static float ToDecibels(float linear)
        {
            linear = Mathf.Clamp01(linear);
            return linear <= 0.0001f ? SilenceDb : Mathf.Log10(linear) * 20f;
        }
    }
}
