#if !ODIN_INSPECTOR
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Inspector for every AudioBank subclass: validity report plus enum sync.
    /// Compiled only when Odin is absent — see AudioBankOdinEditor for the Odin path.</summary>
    [CustomEditor(typeof(AudioBank), true)]
    public sealed class AudioBankEditor : UnityEditor.Editor
    {
        private readonly AudioBankEditorGUI.ValidityReport _report = new();

        private void OnEnable() => _report.Refresh((AudioBank)target);

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) _report.Refresh((AudioBank)target);

            AudioBankEditorGUI.DrawExtras((AudioBank)target, serializedObject, _report);
        }
    }
}
#endif
