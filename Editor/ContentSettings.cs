using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Project-wide settings for the Content window. Persisted to
    /// <c>ProjectSettings/ContentSettings.asset</c> so the content root is shared across the team.
    /// </summary>
    [FilePath("ProjectSettings/ContentSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class ContentSettings : ScriptableSingleton<ContentSettings>
    {
        private const string DEFAULT_CONTENT_ROOT = "Assets/_Project/Content";

        [SerializeField] private string contentRoot = DEFAULT_CONTENT_ROOT;

        /// <summary>Root folder new content types nest their subfolders under.</summary>
        public string ContentRoot => string.IsNullOrEmpty(contentRoot) ? DEFAULT_CONTENT_ROOT : contentRoot;

        /// <summary>Sets and persists <see cref="ContentRoot"/>. Silently ignored if
        /// <paramref name="root"/> is empty or not under "Assets".</summary>
        public void SetContentRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !root.StartsWith("Assets")) return;
            contentRoot = root.TrimEnd('/');
            Save(true);
        }
    }
    
    /// <summary>
    /// ContentSettingsProvider — Project Settings ▸ Grimbar Interactive ▸ Content
    /// </summary>
    internal class ContentSettingsProvider : SettingsProvider
    {
        private ContentSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
            => new ContentSettingsProvider("Project/Grimbar Interactive/Content", SettingsScope.Project)
            {
                label = "Content",
                keywords = new[] { "Content", "Grimbar Interactive" }
            };

        public override void OnGUI(string searchContext)
        {
            var settings = ContentSettings.instance;

            EditorGUILayout.LabelField("Content Root", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Newly created Content assets are filed under this folder, one subfolder per type.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            var newRoot = EditorGUILayout.TextField("Root Folder", settings.ContentRoot);
            if (EditorGUI.EndChangeCheck())
            {
                if (newRoot.StartsWith("Assets"))
                    settings.SetContentRoot(newRoot);
                else
                    EditorUtility.DisplayDialog("Invalid Root", "The content root must be under \"Assets\".", "OK");
            }

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Registered Types", EditorStyles.boldLabel);

            var types = ContentTypeRegistry.AllTypes;
            if (types.Count == 0)
            {
                EditorGUILayout.HelpBox("No [Content] types found.", MessageType.Info);
                return;
            }

            foreach (var info in types)
            {
                EditorGUILayout.LabelField(info.DisplayName, info.RootFolder);
            }
        }
    }
}
