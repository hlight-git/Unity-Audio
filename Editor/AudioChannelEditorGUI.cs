using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Hlight.Audio.Editor
{
    /// <summary>
    /// Channel inspector logic (exposed-param dropdown sourced from the mixer, plus routing
    /// warnings), shared by the plain editor and the Odin editor so both draw identically.
    /// Static and always compiled — must never reference Odin, since it is also the code path
    /// used when Odin is absent.
    /// </summary>
    public static class AudioChannelEditorGUI
    {
        /// <summary>
        /// Reads the mixer's exposed float parameter names straight off its serialized data —
        /// AudioMixer has no public API for this. Allocates a SerializedObject, so callers
        /// should cache the result via <see cref="ParamCache"/> rather than call this per repaint.
        /// </summary>
        public static IReadOnlyList<string> GetExposedParameterNames(AudioMixer mixer)
        {
            if (mixer == null) return Array.Empty<string>();

            var array = new SerializedObject(mixer).FindProperty("m_ExposedParameters");
            if (array == null) return Array.Empty<string>();

            var names = new List<string>(array.arraySize);
            for (int i = 0; i < array.arraySize; i++)
                names.Add(array.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            return names;
        }

        /// <summary>
        /// Owned by the wrapper (plain or Odin), refreshed on enable and after a change — not
        /// from inside the draw call, since GetExposedParameterNames builds a fresh
        /// SerializedObject over the mixer every time it runs.
        /// </summary>
        public sealed class ParamCache
        {
            public IReadOnlyList<string> Names { get; private set; } = Array.Empty<string>();

            public void Refresh(AudioChannel channel) =>
                Names = GetExposedParameterNames(channel.group != null ? channel.group.audioMixer : null);
        }

        /// <summary>
        /// Draws exposedParam in place of its default text field: disabled with guidance if no
        /// group is assigned, an editable text field with setup instructions if the mixer has no
        /// exposed parameters yet, otherwise a popup of the mixer's exposed parameter names. Call
        /// instead of letting the default/Odin drawer draw exposedParam itself.
        /// </summary>
        public static void DrawExposedParamField(SerializedProperty exposedParamProperty, AudioChannel channel, IReadOnlyList<string> names)
        {
            var label = new GUIContent("Exposed Param",
                "Exposed float parameter on the mixer, in dB. Must match the mixer exactly.");

            if (channel.group == null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(exposedParamProperty, label);
                EditorGUILayout.HelpBox(
                    "Assign a mixer group above first — the exposed parameter list comes from that group's mixer.",
                    MessageType.Info);
                return;
            }

            if (names.Count == 0)
            {
                EditorGUILayout.PropertyField(exposedParamProperty, label);
                EditorGUILayout.HelpBox(
                    "This mixer has no exposed parameters yet. In the Audio Mixer window, select this group, " +
                    "right-click Volume in the Inspector and choose \"Expose 'Volume (of ...)' to script\", " +
                    "then rename the entry in the Exposed Parameters dropdown at the top-right of the Audio " +
                    "Mixer window. The renamed string is what belongs in this field.",
                    MessageType.Info);
                return;
            }

            string current = exposedParamProperty.stringValue;
            bool notInList = !string.IsNullOrEmpty(current) && !names.Contains(current);

            // A user mid-rename must not have their data changed underneath them — keep the
            // current value selectable rather than snapping the popup to something else.
            var options = new List<string>(names);
            if (notInList) options.Add(current);
            int index = Mathf.Max(0, options.IndexOf(current));

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(label, index, options.ToArray());
            if (EditorGUI.EndChangeCheck())
                exposedParamProperty.stringValue = options[newIndex];

            if (notInList)
            {
                EditorGUILayout.HelpBox(
                    $"'{current}' is not an exposed parameter on {channel.group.audioMixer.name}. Pick one " +
                    "above, or correct it by hand below.", MessageType.Warning);
                EditorGUILayout.PropertyField(exposedParamProperty, new GUIContent("Exposed Param (raw)"));
            }
        }

        /// <summary>
        /// Mirrors AudioCueEditorGUI's warning from the other side: a channel with no group
        /// bypasses the mixer entirely, so its volume and mute silently do nothing.
        /// </summary>
        public static void DrawGroupWarning(AudioChannel channel)
        {
            if (channel.group == null)
                EditorGUILayout.HelpBox(
                    "No mixer group assigned — this channel bypasses the mixer entirely; its volume and mute do nothing.",
                    MessageType.Warning);
        }
    }
}
