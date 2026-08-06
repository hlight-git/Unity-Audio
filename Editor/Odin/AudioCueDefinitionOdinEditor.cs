using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Odin path for the cue inspector — draws serialized fields through Odin (so
    /// Odin attributes on a user's AudioCueDefinition subclass render), then the same preview
    /// buttons and validity warnings as the plain editor.</summary>
    [CustomEditor(typeof(AudioCueDefinition))]
    public sealed class AudioCueDefinitionOdinEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            AudioCueEditorGUI.DrawExtras((AudioCueDefinition)target);
        }
    }
}
