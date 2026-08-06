#if !ODIN_INSPECTOR
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Inspector for AudioChannel: exposed-param dropdown sourced from the mixer, plus
    /// the no-group routing warning. Compiled only when Odin is absent — see
    /// AudioChannelOdinEditor for the Odin path.</summary>
    [CustomEditor(typeof(AudioChannel))]
    public sealed class AudioChannelEditor : UnityEditor.Editor
    {
        private readonly AudioChannelEditorGUI.ParamCache _cache = new();

        private void OnEnable() => _cache.Refresh((AudioChannel)target);

        public override void OnInspectorGUI()
        {
            var channel = (AudioChannel)target;

            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "exposedParam");
            if (EditorGUI.EndChangeCheck()) _cache.Refresh(channel);

            serializedObject.Update();
            AudioChannelEditorGUI.DrawExposedParamField(
                serializedObject.FindProperty("exposedParam"), channel, _cache.Names);
            serializedObject.ApplyModifiedProperties();

            AudioChannelEditorGUI.DrawGroupWarning(channel);
        }
    }
}
#endif
