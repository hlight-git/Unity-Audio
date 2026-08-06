using UnityEngine;
using UnityEngine.Audio;

namespace Hlight.Audio
{
    /// <summary>
    /// One volume-controllable group. Create one asset per channel — adding a channel
    /// never requires touching package code.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioChannel", menuName = "Hlight/Audio/Channel")]
    public sealed class AudioChannel : ScriptableObject
    {
        [Tooltip("Mixer group every cue on this channel routes to.")]
        public AudioMixerGroup group;

        [Tooltip("Exposed float parameter on the mixer, in dB. Must match the mixer exactly.")]
        public string exposedParam = "MasterVolume";

        [Range(0f, 1f)]
        [Tooltip("Volume used the first time the game runs, before anything is saved.")]
        public float defaultVolume = 1f;

        /// <summary>PlayerPrefs key. Derived from the asset name, so renaming the asset orphans saved volume.</summary>
        public string SaveKey => $"Audio.{name}";
    }
}
