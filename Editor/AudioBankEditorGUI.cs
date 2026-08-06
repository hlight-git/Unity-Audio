using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hlight.Audio.Editor
{
    /// <summary>
    /// Bank inspector logic (validity report + enum sync), shared by the plain editor and the
    /// Odin editor so both draw identically. Static and always compiled — must never reference
    /// Odin, since it is also the code path used when Odin is absent.
    /// </summary>
    public static class AudioBankEditorGUI
    {
        /// <summary>
        /// Owned by the wrapper (plain or Odin), refreshed on enable and after a change — never
        /// from inside the draw call. MissingKeyNames()/DuplicateKeyNames() rebuild the bank's
        /// lookup table (and LogError on a non-int enum), so calling them per-repaint would spam
        /// the console and rebuild the table constantly.
        /// </summary>
        public sealed class ValidityReport
        {
            public IReadOnlyList<string> Missing { get; private set; } = Array.Empty<string>();
            public IReadOnlyList<string> Duplicates { get; private set; } = Array.Empty<string>();

            public void Refresh(AudioBank bank)
            {
                Missing = bank.MissingKeyNames();
                Duplicates = bank.DuplicateKeyNames();
            }
        }

        /// <summary>Draws the validity report and the Sync button. Call after the fields themselves are drawn.</summary>
        public static void DrawExtras(AudioBank bank, SerializedObject serializedObject, ValidityReport report)
        {
            EditorGUILayout.Space();

            if (report.Missing.Count > 0)
                EditorGUILayout.HelpBox($"Missing cue for: {string.Join(", ", report.Missing)}", MessageType.Warning);

            if (report.Duplicates.Count > 0)
                EditorGUILayout.HelpBox(
                    $"Duplicate key: {string.Join(", ", report.Duplicates)} — only the last one wins.",
                    MessageType.Error);

            if (report.Missing.Count == 0 && report.Duplicates.Count == 0)
                EditorGUILayout.HelpBox($"Every {bank.KeyType.Name} value is mapped.", MessageType.Info);

            if (GUILayout.Button("Sync with enum"))
            {
                Sync(bank, serializedObject);
                report.Refresh(bank);
            }
        }

        /// <summary>One row per enum value, in enum order, keeping cues already assigned.</summary>
        public static void Sync(AudioBank bank, SerializedObject serializedObject)
        {
            var entriesProperty = serializedObject.FindProperty("entries");

            // enumValueIndex is the position in Enum.GetValues, not the underlying number —
            // which is what we want, since rows are written in that same order.
            var existing = new Dictionary<int, UnityEngine.Object>();
            for (int i = 0; i < entriesProperty.arraySize; i++)
            {
                var element = entriesProperty.GetArrayElementAtIndex(i);
                int key = element.FindPropertyRelative("key").enumValueIndex;
                var cue = element.FindPropertyRelative("cue").objectReferenceValue;
                // Last non-null wins, matching AudioBank<TKey>.BuildTable. A null only claims
                // the slot when nothing has claimed it yet, so it cannot shadow a real cue.
                if (cue != null || !existing.ContainsKey(key)) existing[key] = cue;
            }

            int count = Enum.GetValues(bank.KeyType).Length;
            Undo.RecordObject(bank, "Sync audio bank");
            entriesProperty.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                var element = entriesProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("key").enumValueIndex = i;
                element.FindPropertyRelative("cue").objectReferenceValue =
                    existing.TryGetValue(i, out var cue) ? cue : null;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(bank);
            bank.Invalidate();
        }
    }
}
