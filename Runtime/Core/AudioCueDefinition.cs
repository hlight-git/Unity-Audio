using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Hlight.Audio
{
    /// <summary>
    /// One sound. Pure authored data — nothing here is written at runtime, so two banks can
    /// safely share a cue. Clips are Addressables references; the bank owns what is loaded.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCue", menuName = "Hlight/Audio/Cue")]
    public sealed class AudioCueDefinition : ScriptableObject
    {
        [Tooltip("One or more clips. More than one picks at random, which stops repeats sounding mechanical.")]
        public AssetReferenceT<AudioClip>[] clips = System.Array.Empty<AssetReferenceT<AudioClip>>();

        public AudioChannel channel;

        [Range(0f, 1f)] public float volume = 1f;

        [Range(0.5f, 2f)] public float pitch = 1f;

        [Range(0f, 0.5f)]
        [Tooltip("Random offset added to pitch on every play. 0 disables.")]
        public float pitchVariation;

        [Range(0, 255)]
        [Tooltip("0 = never virtualized, 255 = first to go. Leave most cues at 128 and let Unity rank by audibility; raise only UI and voice.")]
        public int priority = 128;

        public bool loop;

        [Min(0f)] public float fadeIn;
        [Min(0f)] public float fadeOut;

        [Header("Polyphony")]
        [Min(0)]
        [Tooltip("Max instances of THIS cue playing at once. 0 = unlimited. Stops 20 coins in one frame from summing into clipping.")]
        public int maxConcurrent = 2;

        [Min(0f)]
        [Tooltip("Minimum seconds between two starts of this cue.")]
        public float minInterval = 0.03f;

        [Header("Spatial")]
        public bool spatial;
        [Min(0f)] public float minDistance = 1f;
        [Min(0f)] public float maxDistance = 50f;

        public bool IsValid => clips is { Length: > 0 } && channel != null;

        public float RandomPitch() =>
            pitchVariation <= 0f ? pitch : pitch + Random.Range(-pitchVariation, pitchVariation);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (maxDistance <= minDistance) maxDistance = minDistance + 1f;
        }
#endif
    }
}
