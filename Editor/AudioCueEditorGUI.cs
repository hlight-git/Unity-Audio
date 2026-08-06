using UnityEditor;
using UnityEngine;

namespace Hlight.Audio.Editor
{
    /// <summary>
    /// Cue inspector logic (preview + validity warnings), shared by the plain editor and the
    /// Odin editor so both draw identically and share one preview object. Static and always
    /// compiled — must never reference Odin, since it is also the code path used when Odin is
    /// absent.
    /// </summary>
    public static class AudioCueEditorGUI
    {
        private static AudioSource _preview;

        [InitializeOnLoadMethod]
        private static void StopPreviewBeforeReload()
        {
            // HideAndDontSave is what makes the preview object survive a domain reload — but the
            // static pointing at it does not, so without this it is orphaned: invisible, and no
            // longer stoppable.
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        /// <summary>Draws the preview buttons and validity warnings. Call after the fields themselves are drawn.</summary>
        public static void DrawExtras(AudioCueDefinition cue)
        {
            EditorGUILayout.Space();

            bool hasClip = cue.clips is { Length: > 0 };
            using (new EditorGUI.DisabledScope(!hasClip))
            {
                if (GUILayout.Button("Preview")) Preview(cue);
            }

            if (GUILayout.Button("Stop preview")) Stop();

            if (!hasClip)
                EditorGUILayout.HelpBox("Assign at least one Addressable AudioClip.", MessageType.Info);

            if (!cue.IsValid)
            {
                string missing = !hasClip && cue.channel == null ? "no clips and no channel"
                                : !hasClip ? "no clips"
                                : "no channel";
                EditorGUILayout.HelpBox($"This cue will not play: {missing} assigned.", MessageType.Warning);
            }

            if (cue.channel != null && cue.channel.group == null)
                EditorGUILayout.HelpBox(
                    "This cue's channel has no mixer group assigned — it will bypass the mixer entirely, " +
                    "playing at full volume and ignoring that channel's volume and mute.", MessageType.Warning);
        }

        private static void Preview(AudioCueDefinition cue)
        {
            // Same random-pick rule as the runtime table (AudioBankLoading.ResolveClip): a single
            // clip plays as-is, more than one picks at random.
            int index = cue.clips.Length == 1 ? 0 : Random.Range(0, cue.clips.Length);
            var reference = cue.clips[index];
            if (reference == null)
            {
                Debug.LogWarning($"[{cue.name}] Clip slot {index} is an empty reference.", cue);
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(reference.AssetGUID);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[{cue.name}] Clip reference does not resolve to an AudioClip.", cue);
                return;
            }

            if (_preview == null)
            {
                var go = new GameObject("[AudioPreview]") { hideFlags = HideFlags.HideAndDontSave };
                _preview = go.AddComponent<AudioSource>();
                _preview.playOnAwake = false;
            }

            _preview.clip = clip;
            _preview.volume = cue.volume;
            _preview.pitch = cue.RandomPitch();
            _preview.loop = false;
            _preview.Play();
        }

        private static void Stop()
        {
            if (_preview == null) return;
            _preview.Stop();
            Object.DestroyImmediate(_preview.gameObject);
            _preview = null;
        }
    }
}
