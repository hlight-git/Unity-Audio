using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Hlight.Audio
{
    /// <summary>
    /// The loading half of <see cref="AudioBank{TKey}"/>. Clips are Addressables references,
    /// so a bank passes through download then load before it can play anything. Play stays
    /// synchronous throughout and simply returns <see cref="SoundHandle.None"/> while the bank
    /// is unloaded — a sound effect that arrives after the enemy died is worse than no sound.
    /// </summary>
    public abstract partial class AudioBank<TKey> where TKey : struct, Enum
    {
        /// <summary>Parallel to the cue table: _clips[enumIndex][clipIndex]. Null until prepared.</summary>
        private AudioClip[][] _clips;

        /// <summary>One handle per loaded reference, so Release is exact rather than best-effort.</summary>
        private readonly List<AsyncOperationHandle> _handles = new();

        private UniTask _prepare;
        private bool _preparing;
        private CancellationTokenSource _cts;

        public override bool IsReady => _clips != null;

        /// <summary>
        /// Distinct Addressables runtime keys for every clip this bank references. This is the
        /// bank's download and load unit — no label is needed, because the bank already knows
        /// exactly which assets it owns.
        /// </summary>
        public List<object> CollectKeys()
        {
            var keys = new List<object>();
            foreach (var entry in Entries)
            {
                var clips = entry.cue == null ? null : entry.cue.clips;
                if (clips == null) continue;

                foreach (var reference in clips)
                {
                    if (reference == null || !reference.RuntimeKeyIsValid()) continue;
                    var key = reference.RuntimeKey;
                    if (!keys.Contains(key)) keys.Add(key);
                }
            }
            return keys;
        }

        public override async UniTask<long> GetDownloadSizeAsync(CancellationToken ct = default)
        {
            var keys = CollectKeys();
            if (keys.Count == 0) return 0L;

            var handle = Addressables.GetDownloadSizeAsync(keys);
            try
            {
                return await handle.ToUniTask(cancellationToken: ct);
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public override UniTask PrepareAsync(IProgress<float> progress = null,
                                             CancellationToken ct = default)
        {
            if (IsReady) return UniTask.CompletedTask;
            // Hand out the in-flight load rather than reporting success while it runs.
            if (_preparing) return _prepare;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            _preparing = true;
            _prepare = PrepareCore(progress, cts.Token).Preserve();
            return _prepare;
        }

        private async UniTask PrepareCore(IProgress<float> progress, CancellationToken ct)
        {
            try
            {
                var keys = CollectKeys();
                bool downloaded = keys.Count > 0 && await DownloadAsync(keys, progress, ct);
                await LoadAsync(progress, downloaded, ct);
                progress?.Report(1f);
            }
            catch
            {
                // Never retain a partial load: a retry would otherwise add a second handle
                // per already-loaded reference.
                ReleaseHandles();
                throw;
            }
            finally
            {
                _preparing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Phase one. Skipped entirely when nothing is missing — a size of zero is exactly what
        /// an all-local build reports, so local content costs no extra step and needs no flag.
        /// Returns whether anything was actually downloaded, so <see cref="LoadAsync"/> knows
        /// how much of the progress bar is left for it.
        /// </summary>
        private async UniTask<bool> DownloadAsync(List<object> keys, IProgress<float> progress, CancellationToken ct)
        {
            var sizeHandle = Addressables.GetDownloadSizeAsync(keys);
            long size;
            try
            {
                size = await sizeHandle.ToUniTask(cancellationToken: ct);
            }
            finally
            {
                if (sizeHandle.IsValid()) Addressables.Release(sizeHandle);
            }
            if (size <= 0L) return false;

            var download = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false);
            try
            {
                while (!download.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    // Byte-based. PercentComplete counts finished sub-operations instead, which
                    // makes a progress bar jump in steps rather than move with the download.
                    progress?.Report(download.GetDownloadStatus().Percent * 0.5f);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                if (download.Status == AsyncOperationStatus.Failed)
                    throw download.OperationException ?? new Exception($"[{name}] download failed");
            }
            finally
            {
                if (download.IsValid()) Addressables.Release(download);
            }

            return true;
        }

        /// <summary>Phase two. The bundles are on disk by now, so this is fast.</summary>
        private async UniTask LoadAsync(IProgress<float> progress, bool downloaded, CancellationToken ct)
        {
            float from = downloaded ? 0.5f : 0f;
            float span = 1f - from;

            var table = new AudioClip[TableSize][];
            var entries = Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(from + span * i / entries.Length);

                var entry = entries[i];
                var clips = entry.cue == null ? null : entry.cue.clips;
                if (clips == null || clips.Length == 0) continue;

                int slot = IndexOf(entry.key);
                if (slot < 0 || slot >= table.Length) continue;

                var loaded = new AudioClip[clips.Length];
                for (int c = 0; c < clips.Length; c++)
                {
                    var reference = clips[c];
                    if (reference == null || !reference.RuntimeKeyIsValid()) continue;

                    // One bad reference must cost one sound, not the whole bank: a designer
                    // removing a clip from its Addressables group, or a build profile excluding
                    // a group, must not go silent for every cue this bank owns.
                    try
                    {
                        var handle = Addressables.LoadAssetAsync<AudioClip>(reference.RuntimeKey);
                        _handles.Add(handle);
                        loaded[c] = await handle.ToUniTask(cancellationToken: ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        Debug.LogError($"[{name}] {entry.key} clip {c} failed to load: {e.Message}", this);
                    }
                }

                // Compact out invalid/failed slots so random selection never picks a null clip.
                table[slot] = Array.FindAll(loaded, clip => clip != null);
            }

            ct.ThrowIfCancellationRequested();
            _clips = table;
        }

        public override void Release()
        {
            // Free the sources before the clips they are reading. Releasing an Addressables
            // handle destroys the AudioClip, and a playing AudioSource holding it is a
            // use-after-free, not merely a truncated sound.
            StopAll(0f);
            _cts?.Cancel();
            ReleaseHandles();
            _clips = null;
            _exclusiveHandle = SoundHandle.None;
        }

        private void ReleaseHandles()
        {
            for (int i = 0; i < _handles.Count; i++)
                if (_handles[i].IsValid()) Addressables.Release(_handles[i]);
            _handles.Clear();
        }

        private void InvalidateClips() => Release();

#if UNITY_EDITOR
        /// <summary>
        /// Test hook only — injects already-"loaded" clips directly, bypassing Addressables
        /// entirely, so a test can exercise <c>Play</c>/<c>PlayAt</c> without a real
        /// Addressables build. Indexed exactly like <see cref="ResolveClip"/>: index = enum
        /// integer value. Mirrors <see cref="AudioBank{TKey}.SetEntriesForTests"/>.
        /// </summary>
        public void SetClipsForTests(AudioClip[][] clips) => _clips = clips;
#endif

        private AudioClip ResolveClip(TKey key)
        {
            if (_clips == null) return null;

            int slot = IndexOf(key);
            if (slot < 0 || slot >= _clips.Length) return null;

            var loaded = _clips[slot];
            if (loaded == null || loaded.Length == 0) return null;

            // Already compacted, so every index here is a real clip.
            return loaded[loaded.Length == 1 ? 0 : UnityEngine.Random.Range(0, loaded.Length)];
        }
    }
}
