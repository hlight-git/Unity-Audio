using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Hlight.Audio
{
    /// <summary>
    /// Non-generic base. Carries only what tooling and bootstrap code need in order to
    /// handle a bank without knowing its key type.
    /// </summary>
    public abstract class AudioBank : ScriptableObject
    {
        public abstract Type KeyType { get; }

        /// <summary>True once clips are loaded. Play is a no-op before this.</summary>
        public abstract bool IsReady { get; }

        /// <summary>Enum values with no cue assigned, as display strings.</summary>
        public abstract IReadOnlyList<string> MissingKeyNames();

        /// <summary>Keys appearing in more than one entry, as display strings.</summary>
        public abstract IReadOnlyList<string> DuplicateKeyNames();

        /// <summary>Drop the cached lookup table after entries are edited.</summary>
        public abstract void Invalidate();

        /// <summary>Bytes that must be downloaded before this bank can load. Zero when everything is local.</summary>
        public abstract UniTask<long> GetDownloadSizeAsync(CancellationToken ct = default);

        /// <summary>
        /// Download whatever is missing, then load every clip. Idempotent: calling it while a
        /// prepare is already running joins that one rather than starting a second.
        /// </summary>
        /// <remarks>
        /// Throws <see cref="OperationCanceledException"/> if <see cref="Release"/> is called
        /// while the load is in flight — a caller awaiting "the bank is ready" must not fall
        /// through as though it were. A joining caller's own <paramref name="ct"/> and
        /// <paramref name="progress"/> are ignored; the first caller owns both, so a joining
        /// caller cannot cancel a load the first caller is relying on — but if the *first*
        /// caller's token fires, every joiner receives the same <see cref="OperationCanceledException"/>
        /// too. Releasing and immediately re-preparing in the same frame returns the dying task;
        /// retry a frame later.
        /// </remarks>
        public abstract UniTask PrepareAsync(IProgress<float> progress = null, CancellationToken ct = default);

        /// <summary>
        /// Release every Addressables handle this bank holds. Also stops every sound currently
        /// playing from this bank's cues, audibly and immediately — since sounds are stopped by
        /// cue rather than by bank, this includes another bank's instances of a cue shared
        /// between them.
        /// </summary>
        public abstract void Release();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ReleaseBanksOnExitPlayMode()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            // With Reload Domain disabled these fields outlive the play session while the
            // clips they point at do not, so IsReady would report a bank that cannot play.
            if (change != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
            foreach (var bank in Resources.FindObjectsOfTypeAll<AudioBank>()) bank.Release();
        }
#endif
    }

    /// <summary>
    /// Maps a game-defined enum to cues. Subclass with a concrete enum to create the asset:
    /// <code>public sealed class SfxBank : AudioBank&lt;SfxId&gt; { }</code>
    /// Binding TKey here rather than on each method is what makes a wrong enum a compile error.
    /// </summary>
    /// <remarks>
    /// TKey must be int-backed, with non-negative values densely packed from zero — the cue
    /// table is a plain array indexed by the enum's integer value.
    /// </remarks>
    public abstract partial class AudioBank<TKey> : AudioBank where TKey : struct, Enum
    {
        [Serializable]
        public struct Entry
        {
            public TKey key;
            public AudioCueDefinition cue;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [SerializeField]
        [Tooltip("Only one sound from this bank plays at a time — playing a new one crossfades out the old. Use for music and voice. " +
                 "An exclusive bank ignores its cues' per-cue polyphony (maxConcurrent/minInterval) — it already allows only one sound at a time.")]
        private bool exclusive;

        private AudioCueDefinition[] _table;   // index = enum integer value
        private SoundHandle _exclusiveHandle;

        protected Entry[] Entries => entries;

        public override Type KeyType => typeof(TKey);

        public override void Invalidate()
        {
            _table = null;
            InvalidateClips();
        }

        private void OnEnable() => _table = null;

#if UNITY_EDITOR
        private void OnValidate() => _table = null;
#endif

        // ---- playback ----

        public SoundHandle Play(TKey key, float volumeScale = 1f, float fadeIn = -1f)
            => PlayInternal(key, null, volumeScale, fadeIn);

        public SoundHandle PlayAt(TKey key, Transform follow, float volumeScale = 1f, float fadeIn = -1f)
            => PlayInternal(key, follow, volumeScale, fadeIn);

        private SoundHandle PlayInternal(TKey key, Transform follow, float volumeScale, float fadeIn)
        {
            var runtime = AudioRuntime.Current;
            var cue = Resolve(key);
            if (runtime == null || cue == null) return SoundHandle.None;

            var clip = ResolveClip(key);
            if (clip == null) return SoundHandle.None;   // bank not prepared yet

            if (exclusive && _exclusiveHandle.IsSome)
                runtime.Stop(_exclusiveHandle, fadeIn >= 0f ? fadeIn : -1f);

            var handle = runtime.Play(cue, clip, follow, volumeScale, fadeIn, exclusive);
            if (exclusive) _exclusiveHandle = handle;
            return handle;
        }

        public void Stop(TKey key, float fadeOut = -1f)
        {
            var cue = Resolve(key);
            if (cue != null) AudioRuntime.Current?.StopCue(cue, fadeOut);
        }

        /// <summary>
        /// Stops every sound started from this bank's cues. Stops by cue, not by bank instance —
        /// if another bank shares one of these cues (the supported "two banks, one cue" pattern),
        /// that bank's currently-playing instances of the shared cue are stopped too.
        /// </summary>
        public void StopAll(float fadeOut = 0f)
        {
            var runtime = AudioRuntime.Current;
            if (runtime == null) return;
            foreach (var entry in entries)
                if (entry.cue != null) runtime.StopCue(entry.cue, fadeOut);
        }

        // ---- lookup ----

        public AudioCueDefinition Resolve(TKey key)
        {
            _table ??= BuildTable();
            int i = IndexOf(key);
            return i >= 0 && i < _table.Length ? _table[i] : null;
        }

        /// <summary>Generic enum to int with no boxing.</summary>
        protected static int IndexOf(TKey key) => UnsafeUtility.EnumToInt(key);

        private AudioCueDefinition[] BuildTable()
        {
            if (Enum.GetUnderlyingType(typeof(TKey)) != typeof(int))
                Debug.LogError($"[{name}] {typeof(TKey).Name} must be int-backed: the cue table is indexed by the enum's integer value.");

            int max = 0;
            var negative = new List<TKey>();
            var allValues = new List<int>();
            foreach (TKey value in Enum.GetValues(typeof(TKey)))
            {
                int i = IndexOf(value);
                if (i < 0) negative.Add(value);
                else { max = Mathf.Max(max, i); allValues.Add(i); }
            }

            // Sparse: a value skipped between 0 and the largest one is silently unreachable
            // through Resolve()/IndexOf() below — the array has a slot for it, just no entry.
            var sparse = new List<int>();
            for (int i = 0; i <= max; i++)
                if (!allValues.Contains(i)) sparse.Add(i);

            if (negative.Count > 0)
                Debug.LogWarning($"[{name}] {typeof(TKey).Name} has negative value(s) [{string.Join(", ", negative)}]: " +
                                  "the cue table is indexed by the enum's integer value, so these can never resolve to a cue.");
            if (sparse.Count > 0)
                Debug.LogWarning($"[{name}] {typeof(TKey).Name} is missing integer value(s) [{string.Join(", ", sparse)}] between 0 and {max}: " +
                                  "the lookup table is sized by the largest enum value, so this bank wastes slots but is otherwise harmless.");

            var table = new AudioCueDefinition[max + 1];
            foreach (var entry in entries)
            {
                int i = IndexOf(entry.key);
                if (i >= 0 && i < table.Length && entry.cue != null) table[i] = entry.cue;
            }
            return table;
        }

        /// <summary>Table size — the loaded-clip array in the loading half of this partial class is kept parallel to it.</summary>
        protected int TableSize
        {
            get
            {
                _table ??= BuildTable();
                return _table.Length;
            }
        }

        // ---- validation, used by the Sync button and by tests ----

        public List<TKey> MissingKeys()
        {
            var missing = new List<TKey>();
            foreach (TKey value in Enum.GetValues(typeof(TKey)))
                if (Resolve(value) == null) missing.Add(value);
            return missing;
        }

        public List<TKey> DuplicateKeys()
        {
            var seen = new HashSet<TKey>();
            var duplicates = new List<TKey>();
            foreach (var entry in entries)
                if (!seen.Add(entry.key) && !duplicates.Contains(entry.key)) duplicates.Add(entry.key);
            return duplicates;
        }

        public override IReadOnlyList<string> MissingKeyNames()
            => MissingKeys().ConvertAll(k => k.ToString());

        public override IReadOnlyList<string> DuplicateKeyNames()
            => DuplicateKeys().ConvertAll(k => k.ToString());

#if UNITY_EDITOR
        /// <summary>Editor and test hook — only unit tests set entries directly; the Sync button
        /// writes through <c>serializedObject.FindProperty("entries")</c> instead.</summary>
        public void SetEntriesForTests(Entry[] value)
        {
            entries = value ?? Array.Empty<Entry>();
            Invalidate();
        }

        /// <summary>Test hook only — sets <see cref="exclusive"/> without the inspector's toggle.</summary>
        public void SetExclusiveForTests(bool value) => exclusive = value;
#endif
    }
}
