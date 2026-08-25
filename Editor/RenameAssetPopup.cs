using System;
using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Small text-field popup used for both asset and folder renames.
    /// </summary>
    internal class RenameAssetPopup : PopupWindowContent
    {
        private string _name;
        private readonly Action<string> _onConfirm;
        private bool _focused;

        public RenameAssetPopup(string current, Action<string> onConfirm)
        {
            _name = current;
            _onConfirm = onConfirm;
        }

        public override Vector2 GetWindowSize() => new Vector2(260f, 64f);

        public override void OnGUI(Rect rect)
        {
            GUILayout.Label("Rename Asset", EditorStyles.boldLabel);

            // Read the Enter key before the text field gets a chance to consume it: a single-line
            // TextField ends its own edit session on Return internally, which swallows the KeyDown
            // and leaves this checked afterward seeing nothing on the first press.
            var e = Event.current;
            var enter = e.type == EventType.KeyDown &&
                        e.keyCode is KeyCode.Return or KeyCode.KeypadEnter;

            GUI.SetNextControlName("RenameField");
            _name = EditorGUILayout.TextField(_name);
            if (!_focused) { EditorGUI.FocusTextInControl("RenameField"); _focused = true; }

            var confirm = GUILayout.Button("Rename");

            if (confirm || enter)
            {
                if (enter) e.Use();
                _onConfirm?.Invoke(_name);
                editorWindow.Close();
            }
        }
    }
}
