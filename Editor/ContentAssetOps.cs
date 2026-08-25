using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GI.UnityToolkit.Variables;
using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Generic asset CRUD + folder management for Content types.
    /// </summary>
    internal static class ContentAssetOps
    {
        public static string ParentFolder(string assetPath)
        {
            var slashIndex = assetPath.LastIndexOf('/');
            return slashIndex < 0 ? assetPath : assetPath[..slashIndex];
        }

        /// <summary>
        /// Creates every missing segment of an Assets-relative folder path.
        /// </summary>
        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>
        /// Mirrors the asset file name into the type's configured name property, and
        /// optionally derives a slug id. Both are auto-properties with private setters, so the
        /// compiler-generated backing fields are written through <see cref="SerializedObject"/>.
        /// </summary>
        private static void ApplyNameAndId(DataObject asset, ContentTypeInfo info, string assetName, bool includeId)
        {
            var so = new SerializedObject(asset);
            var changed = WriteString(so, info.NameProperty, assetName, info, required: true);

            if (includeId && !string.IsNullOrEmpty(info.IdProperty))
            {
                changed |= WriteString(so, info.IdProperty, Slug(assetName), info, required: false);
            }

            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool WriteString(SerializedObject so, string propertyName, string value, ContentTypeInfo info, bool required)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;

            var prop = so.FindProperty($"<{propertyName}>k__BackingField") // [field: SerializeField]
                       ?? so.FindProperty(propertyName); // plain field

            if (prop is not { propertyType: SerializedPropertyType.String })
            {
                if (required)
                {
                    Debug.LogWarning($"[Content] {info.ContentType.Name} has no serialized string " +
                                     $"property '{propertyName}'. Fix ContentAttribute.NameProperty " +
                                     "(set it to null to opt out).");
                }

                return false;
            }
            
            if (prop.stringValue == value) return false;
            
            prop.stringValue = value;
            return true;
        }

        private static string ReadString(DataObject asset, string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return null;
            
            var so = new SerializedObject(asset);
            var prop = so.FindProperty($"<{propertyName}>k__BackingField") ?? so.FindProperty(propertyName);
            return prop is { propertyType: SerializedPropertyType.String } ? prop.stringValue : null;
        }

        private static string Slug(string name)
        {
            return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        }

        public static DataObject Create(ContentTypeInfo info, string destinationFolder)
        {
            var folder = string.IsNullOrEmpty(destinationFolder) ? info.RootFolder : destinationFolder;
            EnsureFolder(folder);

            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{info.DefaultAssetName}.asset");
            var asset = (DataObject)ScriptableObject.CreateInstance(info.ContentType);
            AssetDatabase.CreateAsset(asset, path);

            ApplyNameAndId(asset, info, Path.GetFileNameWithoutExtension(path), includeId: true);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// Copies the asset beside its source. The id is always re-derived so duplicates
        /// don't share a runtime key; the display name is only re-derived when it was tracking the
        /// old file name, so a hand-authored name survives.
        /// </summary>
        public static DataObject Duplicate(ContentTypeInfo info, DataObject source)
        {
            var srcPath = AssetDatabase.GetAssetPath(source);
            var destPath = AssetDatabase.GenerateUniqueAssetPath($"{ParentFolder(srcPath)}/{source.name}.asset");
            if (!AssetDatabase.CopyAsset(srcPath, destPath)) return null;

            var copy = AssetDatabase.LoadAssetAtPath<DataObject>(destPath);
            if (copy == null) return null;

            var isNameTrackedFileName = ReadString(source, info.NameProperty) == source.name;
            var copyName = Path.GetFileNameWithoutExtension(destPath);
            if (isNameTrackedFileName)
            {
                ApplyNameAndId(copy, info, copyName, includeId: true);
            }
            else
            {
                var so = new SerializedObject(copy);
                if (WriteString(so, info.IdProperty, Slug(copyName), info, required: false))
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            AssetDatabase.SaveAssets();
            return copy;
        }

        /// <summary>
        /// Renames the asset file and mirrors the new name into the name property. The id
        /// is deliberately left alone — it is a stable runtime key that save data and content
        /// references may hold, and renaming a file must not break it.
        /// </summary>
        public static bool Rename(ContentTypeInfo info, ContentEditor editor, DataObject asset, string newName)
        {
            if (asset == null) return false;
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName) || newName == asset.name) return false;

            var previous = asset.name;
            var error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(asset), newName);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("Rename Failed", error, "OK");
                return false;
            }

            ApplyNameAndId(asset, info, newName, includeId: false);
            AssetDatabase.SaveAssets();
            editor?.NotifyRenamed(asset, previous);
            return true;
        }

        public static bool Delete(ContentTypeInfo info, ContentEditor editor, IReadOnlyList<DataObject> assets)
        {
            if (assets == null || assets.Count == 0) return false;

            var title = $"Delete {info.DisplayName}";
            var body = assets.Count == 1
                ? $"Delete '{assets[0].name}'?"
                : $"Delete these {assets.Count} assets?\n\n" +
                  string.Join("\n", assets.Take(10).Select(a => "  " + a.name)) +
                  (assets.Count > 10 ? $"\n  …and {assets.Count - 10} more" : "");

            var warning = editor?.GetDeleteWarningFor(assets);
            if (!string.IsNullOrEmpty(warning)) body += "\n\n" + warning;
            body += "\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog(title, body, "Delete", "Cancel")) return false;
            if (editor != null && !editor.ConfirmDeleteFor(assets)) return false;

            var paths = assets.Where(a => a != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p)).ToArray();

            var failed = new List<string>();
            AssetDatabase.DeleteAssets(paths, failed);
            if (failed.Count > 0)
            {
                EditorUtility.DisplayDialog("Delete Failed", string.Join("\n", failed), "OK");
            }

            return true;
        }
        
        public static void Move(IReadOnlyList<IContentMenuItem> items, string destinationFolder)
        {
            EnsureFolder(destinationFolder);

            // Resolve every destination path before opening the batch: inside
            // Start/StopAssetEditing the database is not re-scanned, so GenerateUniqueAssetPath
            // would see a stale view of what has already moved.
            var plan = new List<(string source, string destination, bool isFolder)>(items.Count);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var source = item.DiskPath;
                if (string.IsNullOrEmpty(source)) continue;
                
                // Skip items already in the destination explicitly, rather than relying on
                // MakeUnique to land back on the same path — GenerateUniqueAssetPath sees the
                // item's own current file as a "collision" and would bump it to "Name 1" instead.
                if (ParentFolder(source) == destinationFolder) continue;
                var dest = MakeUnique(destinationFolder + "/" + Path.GetFileName(source), item.IsFolder, taken);
                taken.Add(dest);
                plan.Add((source, dest, item.IsFolder));
            }
            if (plan.Count == 0) return;

            var failures = new List<string>();
            var batch = plan.Count > 1;
            try
            {
                if (batch) AssetDatabase.StartAssetEditing();
                foreach (var (source, dest, _) in plan)
                {
                    var error = AssetDatabase.MoveAsset(source, dest);
                    if (!string.IsNullOrEmpty(error)) failures.Add($"{Path.GetFileName(source)}: {error}");
                }
            }
            finally
            {
                if (batch) { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
            }

            if (failures.Count > 0)
            {
                EditorUtility.DisplayDialog("Move Failed",
                    $"{failures.Count} of {plan.Count} item(s) could not be moved:\n\n" + string.Join("\n", failures),
                    "OK");
            }
        }

        /// <summary>
        /// <see cref="AssetDatabase.GenerateUniqueAssetPath"/> only understands files (it
        /// appends a counter before the extension), so folders need their own uniquifier.
        /// </summary>
        private static string MakeUnique(string desired, bool isFolder, ICollection<string> reserved)
        {
            if (!isFolder)
            {
                string unique = AssetDatabase.GenerateUniqueAssetPath(desired);
                while (reserved.Contains(unique)) unique = AssetDatabase.GenerateUniqueAssetPath(unique);
                return unique;
            }
            return UniqueFolderPath(desired, reserved);
        }

        /// <summary>
        /// <see cref="AssetDatabase.GenerateUniqueAssetPath"/> is documented and built
        /// for files (it inserts the counter before the extension); folders get their own
        /// uniquifier so "New Folder" collisions become "New Folder 1", "New Folder 2", ...
        /// </summary>
        private static string UniqueFolderPath(string desired, ICollection<string> reserved = null)
        {
            if (!AssetDatabase.IsValidFolder(desired) && reserved?.Contains(desired) != true) return desired;
            for (var i = 1; i < 1000; i++)
            {
                var candidate = $"{desired} {i}";
                if (!AssetDatabase.IsValidFolder(candidate) && reserved?.Contains(candidate) != true) return candidate;
            }
            return desired;
        }

        public static string CreateFolder(string parentFolder, string name = "New Folder")
        {
            EnsureFolder(parentFolder);
            var path = UniqueFolderPath(parentFolder + "/" + name);
            var guid = AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(path));
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        public static bool RenameFolder(string folderPath, string newName)
        {
            var error = AssetDatabase.RenameAsset(folderPath, newName);
            if (string.IsNullOrEmpty(error)) return true;
            EditorUtility.DisplayDialog("Rename Failed", error, "OK");
            return false;
        }

        public static bool DeleteFolder(string folderPath)
        {
            var hasContents = AssetDatabase.FindAssets(string.Empty, new[] { folderPath }).Length > 0;
            var body = hasContents
                ? "This folder is not empty. Delete it and everything inside it?\n\nThis cannot be undone."
                : "Delete this empty folder?";
            return EditorUtility.DisplayDialog("Delete Folder", body, "Delete", "Cancel")
                   && AssetDatabase.DeleteAsset(folderPath);
        }

        public static void RevealFolder(string folderPath) => EditorUtility.RevealInFinder(folderPath);
    }
}
