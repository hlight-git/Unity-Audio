using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Audio.Samples.Basic
{
    /// <summary>
    /// 01 — Drag BasicAudioSample.prefab into a scene and press Play.
    /// Shows the full lifecycle: construct the runtime, prepare the bank, play by enum.
    /// </summary>
    public sealed class BasicAudioSample : MonoBehaviour
    {
        [SerializeField] private AudioRuntimeConfig config;
        [SerializeField] private SfxBank sfx;
        [SerializeField] private AudioChannel sfxChannel;
        [SerializeField] private MusicBank music;
        [SerializeField] private AudioChannel musicChannel;

        private AudioRuntime _runtime;
        private float _loadProgress;

        private async UniTaskVoid Start()
        {
            // In a real game this belongs in an ABootstrapTask, which already hands you an
            // IProgress<float> and a CancellationToken with exactly these shapes.
            _runtime = new AudioRuntime(config);
            AudioRuntime.Current = _runtime;

            long bytes = await sfx.GetDownloadSizeAsync(destroyCancellationToken);
            if (bytes > 0) Debug.Log($"Need to download {bytes / 1024} KB before this bank can play.");

            await UniTask.WhenAll(
                sfx.PrepareAsync(new Progress<float>(p => _loadProgress = p), destroyCancellationToken),
                music.PrepareAsync(ct: destroyCancellationToken));

            sfx.Play(SfxId.Click);

            // Wrong enum would not compile:
            // sfx.Play(KeyCode.A);   // CS1503

            // Polyphony: maxConcurrent = 2 on the Coin cue means only two of these are heard.
            for (int i = 0; i < 10; i++) sfx.Play(SfxId.Coin);
        }

        private void OnGUI()
        {
            if (_runtime == null) return;

            if (!sfx.IsReady || !music.IsReady)
            {
                GUILayout.Label($"Loading audio… {_loadProgress:P0}");
                return;
            }

            GUILayout.Label($"SFX volume {_runtime.GetVolume(sfxChannel):P0}");
            float volume = GUILayout.HorizontalSlider(_runtime.GetVolume(sfxChannel), 0f, 1f, GUILayout.Width(200));
            if (!Mathf.Approximately(volume, _runtime.GetVolume(sfxChannel)))
                _runtime.SetVolume(sfxChannel, volume);

            if (GUILayout.Button(_runtime.IsMuted(sfxChannel) ? "Unmute" : "Mute"))
                _runtime.SetMuted(sfxChannel, !_runtime.IsMuted(sfxChannel));

            if (GUILayout.Button("Explosion")) sfx.Play(SfxId.Explosion);

            GUILayout.Space(10);

            // Independent channel: muting SFX above must not touch this slider or its sound.
            GUILayout.Label($"Music volume {_runtime.GetVolume(musicChannel):P0}");
            float musicVolume = GUILayout.HorizontalSlider(_runtime.GetVolume(musicChannel), 0f, 1f, GUILayout.Width(200));
            if (!Mathf.Approximately(musicVolume, _runtime.GetVolume(musicChannel)))
                _runtime.SetVolume(musicChannel, musicVolume);

            if (GUILayout.Button(_runtime.IsMuted(musicChannel) ? "Unmute music" : "Mute music"))
                _runtime.SetMuted(musicChannel, !_runtime.IsMuted(musicChannel));

            // MusicBank.exclusive = true: pressing these alternately crossfades — the second
            // call stops the first (fadeOut) and starts the new track (fadeIn) instead of
            // layering both at once.
            if (GUILayout.Button("Play menu music")) music.Play(MusicId.Menu);
            if (GUILayout.Button("Play battle music")) music.Play(MusicId.Battle);
        }

        // A phone call or the user switching apps must silence the game.
        private void OnApplicationPause(bool paused) => _runtime?.SetPaused(paused);
        private void OnApplicationFocus(bool focused) => _runtime?.SetPaused(!focused);

        private void OnDestroy()
        {
            sfx?.Release();
            music?.Release();
            _runtime?.Dispose();
        }
    }
}
