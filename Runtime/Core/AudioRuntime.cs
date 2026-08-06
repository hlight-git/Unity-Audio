using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Hlight.Audio
{
    [Serializable]
    public sealed class AudioRuntimeConfig
    {
        public AudioMixer mixer;
        public AudioChannel[] channels = Array.Empty<AudioChannel>();

        [Range(4, 24)]
        [Tooltip("Concurrent sounds. Keep at or below Project Settings > Audio > Max Real Voices; many Android devices expose only 15-32 tracks system-wide.")]
        public int voices = 12;
    }

    /// <summary>
    /// Owns the source pool and channel volumes. Plain C#: the game constructs one during
    /// bootstrap and assigns <see cref="Current"/>. No singleton, no auto-created GameObject.
    /// Receives loaded clips — knows nothing about where they came from.
    /// </summary>
    public sealed class AudioRuntime : IDisposable
    {
        /// <summary>
        /// Set once at bootstrap; banks resolve through it. A locator, chosen deliberately —
        /// ScriptableObjects cannot take constructor arguments, and threading a reference
        /// into every bank asset costs more than it buys. Assignable, so tests can swap it.
        /// </summary>
        public static AudioRuntime Current { get; set; }

        private readonly AudioRuntimeConfig _config;
        private readonly SoundSource[] _sources;
        private readonly GameObject _root;
        private readonly Dictionary<AudioChannel, float> _volumes = new();
        private readonly Dictionary<AudioChannel, bool> _muted = new();
        private readonly Dictionary<AudioCueDefinition, float> _lastStart = new();
        private readonly HashSet<AudioChannel> _warnedMissingParam = new();
        private bool _prefsDirty;
        private bool _paused;

        public AudioRuntime(AudioRuntimeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _root = new GameObject("[Audio]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root);

            _sources = new SoundSource[Mathf.Max(1, config.voices)];
            for (int i = 0; i < _sources.Length; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(_root.transform);
                go.AddComponent<AudioSource>().playOnAwake = false;
                _sources[i] = go.AddComponent<SoundSource>();
            }

            LoadVolumes();
            Application.focusChanged += OnFocusChanged;
        }

        private void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus) Flush();
        }

        // ---- playback ----

        public SoundHandle Play(AudioCueDefinition cue, AudioClip clip, Transform follow = null,
                                float volumeScale = 1f, float fadeIn = -1f,
                                bool bypassPolyphony = false)
        {
            if (cue == null || clip == null || cue.channel == null) return SoundHandle.None;

            float now = Time.unscaledTime;
            float last = _lastStart.TryGetValue(cue, out float t) ? t : float.NegativeInfinity;

            if (!bypassPolyphony &&
                !PolyphonyGate.Allows(CountActive(cue), last, now, cue.maxConcurrent, cue.minInterval))
                return SoundHandle.None;

            int slot = FindFreeSlot();
            if (slot < 0) return SoundHandle.None; // all voices busy; Unity would virtualize anyway

            _lastStart[cue] = now;
            float fade = fadeIn >= 0f ? fadeIn : cue.fadeIn;
            int generation = _sources[slot].Begin(cue, clip, cue.channel.group, follow, volumeScale, fade, now);
            if (_paused) _sources[slot].SetPaused(true); // don't let a sound started mid-pause play at full volume
            return new SoundHandle(slot, generation);
        }

        public void Stop(SoundHandle handle, float fadeOut = -1f)
        {
            if (!TryResolve(handle, out var source)) return;
            source.Stop(fadeOut >= 0f ? fadeOut : source.Cue.fadeOut);
        }

        public void StopCue(AudioCueDefinition cue, float fadeOut = -1f)
        {
            if (cue == null) return;
            foreach (var s in _sources)
                if (s.Cue == cue) s.Stop(fadeOut >= 0f ? fadeOut : cue.fadeOut);
        }

        public void StopAll(float fadeOut = 0f)
        {
            foreach (var s in _sources) s.Stop(fadeOut);
        }

        public bool IsPlaying(SoundHandle handle) => TryResolve(handle, out var s) && s.IsPlaying;

        /// <summary>Call from OnApplicationPause / OnApplicationFocus so a phone call silences the game.</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            foreach (var s in _sources) s.SetPaused(paused);
            if (paused) Flush();
        }

        private bool TryResolve(SoundHandle handle, out SoundSource source)
        {
            source = null;
            if (!handle.IsSome || handle.Slot < 0 || handle.Slot >= _sources.Length) return false;
            var candidate = _sources[handle.Slot];
            if (candidate.Generation != handle.Generation || !candidate.IsBusy) return false;
            source = candidate;
            return true;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _sources.Length; i++)
                if (!_sources[i].IsBusy) return i;
            return -1;
        }

        private int CountActive(AudioCueDefinition cue)
        {
            int n = 0;
            foreach (var s in _sources) if (s.Cue == cue) n++;
            return n;
        }

        // ---- volume ----

        public void SetVolume(AudioChannel channel, float volume)
        {
            if (channel == null) return;
            _volumes[channel] = Mathf.Clamp01(volume);
            Apply(channel);
            PlayerPrefs.SetFloat(channel.SaveKey, _volumes[channel]);
            _prefsDirty = true; // batched: writing on every slider tick stutters on weak devices
        }

        public float GetVolume(AudioChannel channel)
            => channel == null ? 1f
             : _volumes.TryGetValue(channel, out float v) ? v : channel.defaultVolume;

        /// <summary>Mute is separate from volume, so unmuting restores what the player chose.</summary>
        public void SetMuted(AudioChannel channel, bool muted)
        {
            if (channel == null) return;
            _muted[channel] = muted;
            Apply(channel);
            PlayerPrefs.SetInt(channel.SaveKey + ".muted", muted ? 1 : 0);
            _prefsDirty = true;
        }

        public bool IsMuted(AudioChannel channel)
            => channel != null && _muted.TryGetValue(channel, out bool m) && m;

        /// <summary>Write pending volume changes. Called automatically on pause and dispose.</summary>
        public void Flush()
        {
            if (!_prefsDirty) return;
            PlayerPrefs.Save();
            _prefsDirty = false;
        }

        private void Apply(AudioChannel channel)
        {
            if (_config.mixer == null || string.IsNullOrEmpty(channel.exposedParam)) return;
            float linear = IsMuted(channel) ? 0f : GetVolume(channel);

            if (!_config.mixer.SetFloat(channel.exposedParam, AudioVolume.ToDecibels(linear)) &&
                _warnedMissingParam.Add(channel))
            {
                Debug.LogError(
                    $"[Audio] Channel '{channel.name}' looks for exposed parameter '{channel.exposedParam}' " +
                    "but the mixer has none. Expose the group's Volume in the Audio Mixer window and rename " +
                    "the parameter to match.", channel);
            }
        }

        private void LoadVolumes()
        {
            foreach (var channel in _config.channels)
            {
                if (channel == null) continue;
                _volumes[channel] = PlayerPrefs.GetFloat(channel.SaveKey, channel.defaultVolume);
                _muted[channel] = PlayerPrefs.GetInt(channel.SaveKey + ".muted", 0) == 1;
                Apply(channel);
            }
        }

        public void Dispose()
        {
            Application.focusChanged -= OnFocusChanged;
            Flush();
            _warnedMissingParam.Clear();
            if (Current == this) Current = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
        }
    }
}
