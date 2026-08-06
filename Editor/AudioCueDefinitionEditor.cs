#if !ODIN_INSPECTOR
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Play a cue from the inspector without entering play mode.
    /// Compiled only when Odin is absent — see AudioCueDefinitionOdinEditor for the Odin path.</summary>
    [CustomEditor(typeof(AudioCueDefinition))]
    public sealed class AudioCueDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            AudioCueEditorGUI.DrawExtras((AudioCueDefinition)target);
        }
    }
}
#endif
