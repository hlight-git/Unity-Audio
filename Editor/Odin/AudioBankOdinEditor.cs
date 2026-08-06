using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace Hlight.Audio.Editor
{
    /// <summary>Odin path for the bank inspector — draws serialized fields through Odin (so
    /// Odin attributes on a user's AudioBank subclass render), then the same validity report
    /// and Sync button as the plain editor.</summary>
    [CustomEditor(typeof(AudioBank), true)]
    public sealed class AudioBankOdinEditor : OdinEditor
    {
        private readonly AudioBankEditorGUI.ValidityReport _report = new();

        protected override void OnEnable()
        {
            base.OnEnable();
            _report.Refresh((AudioBank)target);
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) _report.Refresh((AudioBank)target);

            AudioBankEditorGUI.DrawExtras((AudioBank)target, serializedObject, _report);
        }
    }
}
