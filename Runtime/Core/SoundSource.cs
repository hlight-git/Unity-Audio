using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace Hlight.Audio
{
    /// <summary>
    /// One pooled AudioSource. Never deactivated — the GameObject stays on and playback is
    /// simply stopped, so a fade coroutine is never cut off by OnDisable.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SoundSource : MonoBehaviour
    {
        private AudioSource _source;
        private Transform _follow;
        private Coroutine _fade;
        private bool _paused;
        private bool _fadingOut;

        // Assigned lazily rather than in Awake: AudioRuntime adds this component at runtime and
        // EditMode tests construct a runtime outside play mode, where Awake does not run.
        private AudioSource Source => _source != null ? _source : _source = GetComponent<AudioSource>();

        /// <summary>Reuse counter. Incremented on every Begin, so old handles go stale.</summary>
        public int Generation { get; private set; }

        public AudioCueDefinition Cue { get; private set; }
        public bool IsBusy => Cue != null;
        public bool IsPlaying => _source != null && _source.isPlaying;

        private void Awake() => Source.playOnAwake = false;

        private void LateUpdate()
        {
            if (_follow != null) transform.position = _follow.position;
            // Non-looping cue finished on its own: free the slot.
            if (Cue != null && !Cue.loop && !_paused && !Source.isPlaying && _fade == null) Release();
        }

        public int Begin(AudioCueDefinition cue, AudioClip clip, AudioMixerGroup group,
                         Transform follow, float volumeScale, float fadeIn, float now)
        {
            Cue = cue;
            _follow = follow;
            Generation++;
            _paused = false;
            _fadingOut = false;

            Source.clip = clip;
            Source.outputAudioMixerGroup = group;
            Source.loop = cue.loop;
            Source.priority = cue.priority;
            Source.pitch = cue.RandomPitch();
            Source.spatialBlend = cue.spatial ? 1f : 0f;
            if (cue.spatial)
            {
                Source.minDistance = cue.minDistance;
                Source.maxDistance = cue.maxDistance;
                Source.rolloffMode = AudioRolloffMode.Linear;
            }
            if (follow != null) transform.position = follow.position;

            float targetVolume = Mathf.Clamp01(cue.volume * volumeScale);
            Source.volume = fadeIn > 0f ? 0f : targetVolume;
            Source.Play();

            if (fadeIn > 0f) _fade = StartCoroutine(FadeTo(targetVolume, fadeIn, false));
            return Generation;
        }

        public void Stop(float fadeOut)
        {
            // _source is null-checked as well as Cue: with Reload Domain disabled a runtime can
            // outlive its GameObject, leaving destroyed SoundSources reachable through the pool.
            if (Cue == null || _source == null) return;

            // An immediate stop always wins, even mid-fade — scene teardown must be able to
            // cut a fading sound. Only another *fade* request is refused, so a second
            // Stop(2f) cannot restart the ramp and stretch the sound.
            if (fadeOut <= 0f) { Release(); return; }

            if (_fadingOut) return;
            if (_fade != null) StopCoroutine(_fade);
            _fadingOut = true;
            _fade = StartCoroutine(FadeTo(0f, fadeOut, true));
        }

        public void SetPaused(bool paused)
        {
            if (Cue == null || _source == null) return;
            _paused = paused;
            if (paused) _source.Pause(); else _source.UnPause();
        }

        private IEnumerator FadeTo(float target, float duration, bool releaseAfter)
        {
            float from = Source.volume, elapsed = 0f;
            while (elapsed < duration)
            {
                // Unscaled: fades must still run while the game is paused at timeScale 0.
                elapsed += Time.unscaledDeltaTime;
                Source.volume = Mathf.Lerp(from, target, elapsed / duration);
                yield return null;
            }
            Source.volume = target;
            _fade = null;
            if (releaseAfter) Release();
        }

        private void Release()
        {
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
            Source.Stop();
            Source.clip = null;
            Cue = null;
            _follow = null;
            _paused = false;
            _fadingOut = false;
        }
    }
}
