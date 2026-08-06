using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Odin path for the channel inspector — draws every field except exposedParam
    /// through Odin's own tree, then the same exposed-param dropdown and warnings as the plain
    /// editor.</summary>
    [CustomEditor(typeof(AudioChannel))]
    public sealed class AudioChannelOdinEditor : OdinEditor
    {
        private readonly AudioChannelEditorGUI.ParamCache _cache = new();

        protected override void OnEnable()
        {
            base.OnEnable();
            _cache.Refresh((AudioChannel)target);
        }

        public override void OnInspectorGUI()
        {
            var channel = (AudioChannel)target;

            EditorGUI.BeginChangeCheck();
            foreach (var child in Tree.RootProperty.Children)
            {
                if (child.Name == "exposedParam") continue;
                child.Draw();
            }
            if (EditorGUI.EndChangeCheck()) _cache.Refresh(channel);

            serializedObject.Update();
            AudioChannelEditorGUI.DrawExposedParamField(
                serializedObject.FindProperty("exposedParam"), channel, _cache.Names);
            serializedObject.ApplyModifiedProperties();

            AudioChannelEditorGUI.DrawGroupWarning(channel);
        }
    }
}
